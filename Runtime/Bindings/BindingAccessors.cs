using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Bindery
{

/// <summary>
/// Registry of compiled accessor delegates consulted by Bindery's custom
/// bindings before falling back to reflection + expression trees.
///
/// Why it exists: on IL2CPP there is no JIT, so Expression.Compile() returns a
/// delegate that *interprets* the expression tree on every call — slow and
/// allocating. Delegates registered here are ordinary compiled code, AOT-safe
/// and as fast as hand-written accessors.
///
/// Getters for [BindableProperty] fields are registered automatically by the
/// source generator (keyed by the PascalCase property name used in UXML).
/// Setters for the common string properties of built-in elements are
/// registered below. Both maps are open: call RegisterGetter/RegisterSetter to
/// cover custom members or element types the generator cannot see.
///
/// Main-thread only, like the binding system itself.
/// </summary>
public static class BindingAccessors
{
    static readonly Dictionary<(Type, string), Func<object, object>> Getters = new();
    static readonly Dictionary<(Type, string), (Action<object, object> Setter, Type PropertyType)> Setters = new();

    static BindingAccessors()
    {
        // FormatBinding/MultiBinding write string properties on elements; cover
        // the common targets so the write path also avoids expression trees.
        // TextElement.text covers Label, Button and friends via the base-type walk.
        RegisterSetter(typeof(TextElement), "text", typeof(string),
            static (e, v) => ((TextElement)e).text = (string)v);
        RegisterSetter(typeof(AbstractProgressBar), "title", typeof(string),
            static (e, v) => ((AbstractProgressBar)e).title = (string)v);
        RegisterSetter(typeof(BaseField<string>), "value", typeof(string),
            static (e, v) => ((BaseField<string>)e).value = (string)v);
        RegisterSetter(typeof(VisualElement), "tooltip", typeof(string),
            static (e, v) => ((VisualElement)e).tooltip = (string)v);
    }

    /// <summary>Registers a boxed getter for a member, keyed by the name used in UXML.</summary>
    public static void RegisterGetter(Type type, string memberName, Func<object, object> getter) =>
        Getters[(type, memberName)] = getter;

    /// <summary>Registers a setter for an element property, keyed by the declaring type.</summary>
    public static void RegisterSetter(Type type, string propertyName, Type propertyType, Action<object, object> setter) =>
        Setters[(type, propertyName)] = (setter, propertyType);

    internal static bool TryGetGetter(Type type, string memberName, out Func<object, object> getter)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (Getters.TryGetValue((t, memberName), out getter))
                return true;
        }
        getter = null;
        return false;
    }

    internal static bool TryGetSetter(Type type, string propertyName, out Action<object, object> setter, out Type propertyType)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (Setters.TryGetValue((t, propertyName), out var entry))
            {
                setter       = entry.Setter;
                propertyType = entry.PropertyType;
                return true;
            }
        }
        setter       = null;
        propertyType = null;
        return false;
    }
}

} // namespace Bindery
