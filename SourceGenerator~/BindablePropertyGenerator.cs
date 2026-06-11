using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Bindery.Generator
{

/// <summary>
/// Generates UI Toolkit property-bag plumbing for [BindableObject] classes.
///
/// Deliberately uses the ISourceGenerator API (Roslyn 3.8) rather than
/// IIncrementalGenerator so the same DLL also works on older Unity LTS
/// releases — 3.8 is the lowest common denominator across Unity's compilers.
/// </summary>
[Generator]
public class BindablePropertyGenerator : ISourceGenerator
{
    const string BindableObjectAttributeName   = "Bindery.BindableObjectAttribute";
    const string BindablePropertyAttributeName = "Bindery.BindablePropertyAttribute";

    static readonly DiagnosticDescriptor NoReactiveLib = new DiagnosticDescriptor(
        "BG0001", "No reactive library found",
        "[BindableObject] classes were found but neither R3 nor UniRx is referenced by this assembly",
        "BindableGenerator", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NotPartial = new DiagnosticDescriptor(
        "BG0002", "Bindable class must be partial",
        "Class '{0}' is marked [BindableObject] but is not declared 'partial'",
        "BindableGenerator", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NotBindableObject = new DiagnosticDescriptor(
        "BG0003", "Bindable class must inherit BindableObject",
        "Class '{0}' is marked [BindableObject] but does not inherit from Bindery.BindableObject",
        "BindableGenerator", DiagnosticSeverity.Error, isEnabledByDefault: true);

    static readonly DiagnosticDescriptor NotReactiveProperty = new DiagnosticDescriptor(
        "BG0004", "[BindableProperty] field is not a ReactiveProperty<T>",
        "Field '{0}' is marked [BindableProperty] but its type is not ReactiveProperty<T> — it will be ignored",
        "BindableGenerator", DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public void Initialize(GeneratorInitializationContext context) =>
        context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());

    public void Execute(GeneratorExecutionContext context)
    {
        if (context.SyntaxReceiver is not SyntaxReceiver receiver) return;

        // Resolve candidate symbols first so diagnostics (including the missing
        // reactive library error) only fire for assemblies that actually use
        // [BindableObject]. Dedupe via symbol equality — a class split across
        // multiple partial files must only be processed once, otherwise
        // AddSource throws on the duplicate hint name.
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var candidates = new List<(ClassDeclarationSyntax Decl, INamedTypeSymbol Symbol)>();

        foreach (var classDecl in receiver.CandidateClasses)
        {
            var model = context.Compilation.GetSemanticModel(classDecl.SyntaxTree);
            if (model.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol classSymbol) continue;
            if (!HasAttribute(classSymbol, BindableObjectAttributeName)) continue;
            if (!seen.Add(classSymbol)) continue;
            candidates.Add((classDecl, classSymbol));
        }

        if (candidates.Count == 0) return;

        // Auto-detect reactive library from referenced assemblies.
        // R3 takes precedence if both are somehow present.
        var refs = context.Compilation.ReferencedAssemblyNames;
        bool hasR3    = refs.Any(r => r.Name == "R3");
        bool hasUniRx = refs.Any(r => r.Name == "UniRx");

        if (!hasR3 && !hasUniRx)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                NoReactiveLib, candidates[0].Decl.Identifier.GetLocation()));
            return;
        }

        var reactiveLib = hasR3 ? "R3" : "UniRx";

        foreach (var (decl, classSymbol) in candidates)
        {
            if (!decl.Modifiers.Any(SyntaxKind.PartialKeyword))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NotPartial, decl.Identifier.GetLocation(), classSymbol.Name));
                continue;
            }

            if (!InheritsBindableObject(classSymbol))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    NotBindableObject, decl.Identifier.GetLocation(), classSymbol.Name));
                continue;
            }

            var props = new List<PropInfo>();
            foreach (var field in classSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (!HasAttribute(field, BindablePropertyAttributeName)) continue;

                var innerType = GetInnerType(field.Type);
                if (innerType == null)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        NotReactiveProperty,
                        field.Locations.FirstOrDefault() ?? Location.None,
                        field.Name));
                    continue;
                }

                props.Add(new PropInfo(innerType, ToPascalCase(field.Name), field.Name));
            }

            if (props.Count == 0) continue;

            // Fully qualified hint name so same-named classes in different
            // namespaces don't collide.
            var hint = classSymbol.ToDisplayString().Replace('.', '_');
            context.AddSource($"{hint}.Bindable.g.cs", BuildSource(classSymbol, props, reactiveLib));
        }
    }

    static bool HasAttribute(ISymbol symbol, string attributeDisplayName) =>
        symbol.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == attributeDisplayName);

    static bool InheritsBindableObject(INamedTypeSymbol classSymbol)
    {
        for (var baseType = classSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            if (baseType.ToDisplayString() == "Bindery.BindableObject")
                return true;
        }
        return false;
    }

    static string BuildSource(INamedTypeSymbol classSymbol, List<PropInfo> props, string reactiveLib)
    {
        var ns = classSymbol.ContainingNamespace?.IsGlobalNamespace == false
            ? classSymbol.ContainingNamespace.ToDisplayString() : null;
        var cn = classSymbol.Name;

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine($"// Reactive library auto-detected: {reactiveLib}");
        sb.AppendLine("#pragma warning disable 0414 // _s_propertyBagInit is assigned but never read — it exists to trigger registration");
        sb.AppendLine("using Unity.Properties;");
        sb.AppendLine($"using {reactiveLib};");
        sb.AppendLine();

        if (ns != null) sb.AppendLine($"namespace {ns}\n{{");

        sb.AppendLine($"public partial class {cn}");
        sb.AppendLine("{");

        // Static field registers the property bag once. Using a field rather than a
        // static constructor so users can still define their own static constructors.
        sb.AppendLine($"    private static readonly bool _s_propertyBagInit = RegisterPropertyBag_{cn}();");
        sb.AppendLine($"    private static bool RegisterPropertyBag_{cn}()");
        sb.AppendLine("    {");
        sb.AppendLine($"        PropertyBag.Register(new {cn}PropertyBag());");
        sb.AppendLine("        return true;");
        sb.AppendLine("    }");
        sb.AppendLine();

        // Optional per-property callbacks for reacting to changes in user code.
        foreach (var p in props)
            sb.AppendLine($"    partial void On{p.PropName}Changed({p.TypeName} newValue);");
        sb.AppendLine();

        // Subscribe each ReactiveProperty to Notify + user callback, skipping the
        // initial emission that fires on subscribe. Runs from the BindableObject
        // constructor, i.e. before the user's constructor body — hence the null
        // guards: fields assigned inside the constructor are still null here.
        sb.AppendLine("    protected override void InitBindings()");
        sb.AppendLine("    {");
        sb.AppendLine("        base.InitBindings();");
        foreach (var p in props)
        {
            sb.AppendLine($"        if ({p.MemberName} == null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            UnityEngine.Debug.LogError(\"[BindableObject] {cn}.{p.MemberName} is null while bindings are wired. Assign [BindableProperty] fields with field initializers — InitBindings() runs before the constructor body.\");");
            sb.AppendLine("        }");
            sb.AppendLine("        else");
            sb.AppendLine("        {");
            sb.AppendLine($"            Track({p.MemberName}); // dispose the ReactiveProperty together with this object");
            sb.AppendLine($"            Track({p.MemberName}.Skip(1).Subscribe(v =>");
            sb.AppendLine("            {");
            sb.AppendLine($"                Notify(\"{p.PropName}\");");
            sb.AppendLine($"                On{p.PropName}Changed(v);");
            sb.AppendLine("            }));");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
        sb.AppendLine();

        // ContainerPropertyBag maps UXML binding paths to ReactiveProperty<T>.Value.
        sb.AppendLine($"    sealed class {cn}PropertyBag : ContainerPropertyBag<{cn}>");
        sb.AppendLine("    {");
        sb.AppendLine($"        public {cn}PropertyBag()");
        sb.AppendLine("        {");
        foreach (var p in props)
            sb.AppendLine($"            AddProperty(new {p.PropName}Property());");
        sb.AppendLine("        }");
        sb.AppendLine();

        foreach (var p in props)
        {
            sb.AppendLine($"        sealed class {p.PropName}Property : Property<{cn}, {p.TypeName}>");
            sb.AppendLine("        {");
            sb.AppendLine($"            public override string Name => \"{p.PropName}\";");
            sb.AppendLine( "            public override bool IsReadOnly => false;");
            sb.AppendLine($"            public override {p.TypeName} GetValue(ref {cn} container) => container.{p.MemberName}.Value;");
            sb.AppendLine($"            public override void SetValue(ref {cn} container, {p.TypeName} value) => container.{p.MemberName}.Value = value;");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }"); // close PropertyBag class
        sb.AppendLine("}");     // close partial class

        if (ns != null) sb.AppendLine("}");
        return sb.ToString();
    }

    // ReactiveProperty<float> → "float"; null when the type is not a ReactiveProperty<T>
    static string? GetInnerType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Name: "ReactiveProperty", TypeArguments: { Length: 1 } args })
        {
            return args[0].SpecialType switch
            {
                SpecialType.System_Single  => "float",
                SpecialType.System_Double  => "double",
                SpecialType.System_Int32   => "int",
                SpecialType.System_Int64   => "long",
                SpecialType.System_Boolean => "bool",
                SpecialType.System_String  => "string",
                _                          => args[0].ToDisplayString()
            };
        }
        return null;
    }

    // "_sliderValue" / "sliderValue" → "SliderValue"
    static string ToPascalCase(string name)
    {
        name = name.TrimStart('_');
        return name.Length == 0 ? name : char.ToUpper(name[0]) + name.Substring(1);
    }

    class PropInfo
    {
        public PropInfo(string typeName, string propName, string memberName)
        {
            TypeName   = typeName;
            PropName   = propName;
            MemberName = memberName;
        }
        public string TypeName   { get; }
        public string PropName   { get; }
        public string MemberName { get; }
    }
}

class SyntaxReceiver : ISyntaxReceiver
{
    public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

    public void OnVisitSyntaxNode(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax c || c.AttributeLists.Count == 0) return;

        // Cheap syntactic filter — exact attribute identity is verified against
        // the semantic model in Execute.
        foreach (var list in c.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = attr.Name.ToString();
                if (name.EndsWith("BindableObject") || name.EndsWith("BindableObjectAttribute"))
                {
                    CandidateClasses.Add(c);
                    return;
                }
            }
        }
    }
}

} // namespace Bindery.Generator
