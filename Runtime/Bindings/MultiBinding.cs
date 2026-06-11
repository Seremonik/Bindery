using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine.UIElements;

/// <summary>
/// One-way binding that reads multiple source members from the data source, passes
/// them to string.Format, and writes the result to the element property the binding
/// is registered on ('property' in UXML).
///
/// Uses Expression-compiled delegates on the hot path — no reflection after setup.
/// Formatting uses InvariantCulture so output does not vary with the OS locale.
///
/// Usage in UXML:
///   <ext:MultiBinding property="text" format="{0:F0} / {1:F0} HP">
///       <sources>
///           <ext:Source path="Health" />
///           <ext:Source path="MaxHealth" />
///       </sources>
///   </ext:MultiBinding>
///
/// 'property' — string-typed property on the element to write to
/// 'format'   — string.Format pattern; {0} = first source, {1} = second, etc.
/// 'sources'  — ordered list of Source objects, each with a 'path' attribute
/// </summary>
namespace Bindery
{

[UxmlObject]
public partial class Source
{
    [UxmlAttribute] public string path { get; set; }
}

[UxmlObject]
public partial class MultiBinding : CustomBinding
{
    [UxmlAttribute] public string format { get; set; }

    [UxmlObjectReference("sources")]
    public List<Source> sources { get; set; }

    private Func<object, object>[] _getValues;
    private Action<object, object> _setValue;
    private HashSet<string>        _sourceNames;
    private object[]               _values; // reused across updates to avoid per-change allocation

    private INotifyBindablePropertyChanged _notifier;
    private EventHandler<BindablePropertyChangedEventArgs> _changeHandler;
    private bool _setup;
    private bool _failed;

    protected override BindingResult Update(in BindingContext context)
    {
        if (!_setup && !_failed) Setup(context.targetElement, context.bindingId);
        if (_failed) return new BindingResult(BindingStatus.Failure);
        return new BindingResult(_setup ? BindingStatus.Success : BindingStatus.Pending);
    }

    // The data source was replaced — drop the compiled delegates and the event
    // subscription on the old source; the next Update re-runs Setup.
    protected override void OnDataSourceChanged(in DataSourceContextChanged context) => Teardown();

    // Covers both element detach and binding removal via ClearBinding.
    protected override void OnDeactivated(in BindingActivationContext context) => Teardown();

    private void Setup(VisualElement element, in BindingId bindingId)
    {
        if (sources == null || sources.Count == 0)
        {
            Fail($"MultiBinding on {element.GetType().Name}: no <sources> defined.");
            return;
        }

        var ds = BindingReflection.FindDataSource(element);
        if (ds == null) return; // no data source yet — stay Pending and retry

        var dsType = ds.GetType();

        _getValues   = new Func<object, object>[sources.Count];
        _values      = new object[sources.Count];
        _sourceNames = new HashSet<string>();

        for (int i = 0; i < sources.Count; i++)
        {
            var path   = sources[i].path;
            var getter = BindingReflection.BuildGetter(dsType, path);
            if (getter == null)
            {
                Fail($"MultiBinding: '{path}' not found on {dsType.Name} (must be a public field or property).");
                return;
            }
            _getValues[i] = getter;
            _sourceNames.Add(path);
        }

        var targetProperty = bindingId.ToString();
        _setValue = BindingReflection.BuildSetter(element.GetType(), targetProperty, out var targetType);
        if (_setValue == null)
        {
            Fail($"MultiBinding: property '{targetProperty}' not found on {element.GetType().Name} or has no setter.");
            return;
        }
        if (!targetType.IsAssignableFrom(typeof(string)))
        {
            Fail($"MultiBinding: property '{targetProperty}' on {element.GetType().Name} is of type {targetType.Name} — MultiBinding can only write to string properties.");
            return;
        }

        if (ds is INotifyBindablePropertyChanged notifiable)
        {
            _notifier = notifiable;
            _changeHandler = (_, args) =>
            {
                if (_sourceNames.Contains(args.propertyName))
                    ApplyConversion(ds, element);
            };
            notifiable.propertyChanged += _changeHandler;
        }

        _setup = true;
        ApplyConversion(ds, element);
    }

    private void ApplyConversion(object ds, VisualElement element)
    {
        for (int i = 0; i < _getValues.Length; i++)
            _values[i] = _getValues[i](ds);

        var converted = string.IsNullOrEmpty(format)
            ? string.Concat(_values)
            : string.Format(CultureInfo.InvariantCulture, format, _values);

        _setValue(element, converted);
    }

    private void Fail(string message)
    {
        UnityEngine.Debug.LogError(message);
        _failed = true;
    }

    private void Teardown()
    {
        if (_notifier != null && _changeHandler != null)
            _notifier.propertyChanged -= _changeHandler;

        _notifier      = null;
        _changeHandler = null;
        _getValues     = null;
        _setValue      = null;
        _sourceNames   = null;
        _values        = null;
        _setup         = false;
        _failed        = false; // a new data source may satisfy the binding
    }
}

} // namespace Bindery
