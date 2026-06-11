using System;
using System.Globalization;
using UnityEngine.UIElements;

/// <summary>
/// One-way binding that reads a source member (field or property) from the data
/// source, applies an optional string format, and writes the resulting string to
/// the element property the binding is registered on ('property' in UXML).
///
/// Uses Expression-compiled delegates on the hot path — no reflection after setup.
/// Formatting uses InvariantCulture so output does not vary with the OS locale.
///
/// Usage in UXML:
///   ext:FormatBinding property="text" source="SliderValue" format="Value: {0:F1}"
///
/// 'property' — string-typed property on the element to write to (e.g. "text")
/// 'source'   — field or property name on the data source to read from
/// 'format'   — optional string.Format pattern; omit to use ToString()
/// </summary>
namespace Bindery
{

[UxmlObject]
public partial class FormatBinding : CustomBinding
{
    [UxmlAttribute] public string source { get; set; }
    [UxmlAttribute] public string format { get; set; }

    private Func<object, object>   _getValue;
    private Action<object, object> _setValue;

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
        if (string.IsNullOrEmpty(source))
        {
            Fail($"FormatBinding on {element.GetType().Name}: 'source' attribute is missing or empty.");
            return;
        }

        var ds = BindingReflection.FindDataSource(element);
        if (ds == null) return; // no data source yet — stay Pending and retry

        _getValue = BindingReflection.BuildGetter(ds.GetType(), source);
        if (_getValue == null)
        {
            Fail($"FormatBinding: '{source}' not found on {ds.GetType().Name} (must be a public field or property).");
            return;
        }

        var targetProperty = bindingId.ToString();
        _setValue = BindingReflection.BuildSetter(element.GetType(), targetProperty, out var targetType);
        if (_setValue == null)
        {
            Fail($"FormatBinding: property '{targetProperty}' not found on {element.GetType().Name} or has no setter.");
            return;
        }
        if (!targetType.IsAssignableFrom(typeof(string)))
        {
            Fail($"FormatBinding: property '{targetProperty}' on {element.GetType().Name} is of type {targetType.Name} — FormatBinding can only write to string properties.");
            return;
        }

        if (ds is INotifyBindablePropertyChanged notifiable)
        {
            _notifier = notifiable;
            _changeHandler = (_, args) =>
            {
                if (args.propertyName == source)
                    ApplyConversion(ds, element);
            };
            notifiable.propertyChanged += _changeHandler;
        }

        _setup = true;
        ApplyConversion(ds, element);
    }

    private void ApplyConversion(object ds, VisualElement element)
    {
        var raw = _getValue(ds);
        var converted = string.IsNullOrEmpty(format)
            ? Convert.ToString(raw, CultureInfo.InvariantCulture)
            : string.Format(CultureInfo.InvariantCulture, format, raw);
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
        _getValue      = null;
        _setValue      = null;
        _setup         = false;
        _failed        = false; // a new data source may satisfy the binding
    }
}

} // namespace Bindery
