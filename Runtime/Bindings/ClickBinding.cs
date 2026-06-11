using System;
using System.Reflection;
using System.Windows.Input;
using UnityEngine.UIElements;

/// <summary>
/// Custom binding that wires a Button to a command on the data source.
///
/// Supports two command shapes:
///   ICommand                 — wires click to Execute() and auto-drives enabledSelf
///                              from CanExecute. R3's ReactiveCommand implements
///                              ICommand; UniRx's ReactiveCommand does NOT, so the
///                              CanExecute auto-disable only works with R3.
///   Action / void Method()   — invokes the action on click, no CanExecute management
///
/// Usage in UXML:
///   ext:ClickBinding property="name" command="ResetCommand"
/// </summary>
namespace Bindery
{

[UxmlObject]
public partial class ClickBinding : CustomBinding
{
    [UxmlAttribute]
    public string command { get; set; }

    private Button       _button;
    private Action       _clickHandler;
    private ICommand     _trackedCommand;
    private EventHandler _canExecuteHandler;
    private bool _setup;
    private bool _failed;

    protected override BindingResult Update(in BindingContext context)
    {
        if (!_setup && !_failed) Setup(context.targetElement);
        if (_failed) return new BindingResult(BindingStatus.Failure);
        return new BindingResult(_setup ? BindingStatus.Success : BindingStatus.Pending);
    }

    // The data source was replaced — unhook from the old command; the next
    // Update re-runs Setup against the new source.
    protected override void OnDataSourceChanged(in DataSourceContextChanged context) => Teardown();

    // Covers both element detach and binding removal via ClearBinding.
    protected override void OnDeactivated(in BindingActivationContext context) => Teardown();

    private void Setup(VisualElement element)
    {
        if (element is not Button button)
        {
            Fail($"ClickBinding can only be attached to a Button (got {element?.GetType().Name ?? "null"}).");
            return;
        }
        if (string.IsNullOrEmpty(command))
        {
            Fail("ClickBinding: 'command' attribute is missing or empty.");
            return;
        }

        var source = BindingReflection.FindDataSource(element);
        if (source == null) return; // no data source yet — stay Pending and retry

        _button = button;
        if (!Bind(source))
        {
            _button = null;
            Fail($"ClickBinding: '{command}' on {source.GetType().Name} is not an ICommand property, an Action property, or a parameterless void method.");
            return;
        }

        _setup = true;
    }

    private bool Bind(object source)
    {
        var prop = source.GetType()
            .GetProperty(command, BindingFlags.Instance | BindingFlags.Public);

        // ICommand — auto-drives enabledSelf from CanExecute
        if (prop != null && prop.GetValue(source) is ICommand iCommand)
        {
            _trackedCommand = iCommand;
            _clickHandler   = () => iCommand.Execute(null);
            _button.clicked += _clickHandler;

            _canExecuteHandler = (_, __) => _button.SetEnabled(iCommand.CanExecute(null));
            iCommand.CanExecuteChanged += _canExecuteHandler;
            _button.SetEnabled(iCommand.CanExecute(null)); // apply initial state
            return true;
        }

        // Action, or void Method() — click only, no CanExecute management
        _clickHandler = Resolve(source, command);
        if (_clickHandler == null) return false;

        _button.clicked += _clickHandler;
        return true;
    }

    private static Action Resolve(object source, string path)
    {
        var type = source.GetType();

        // void Method()
        var method = type.GetMethod(path, BindingFlags.Instance | BindingFlags.Public);
        if (method != null && method.ReturnType == typeof(void) && method.GetParameters().Length == 0)
            return (Action)Delegate.CreateDelegate(typeof(Action), source, method);

        var prop = type.GetProperty(path, BindingFlags.Instance | BindingFlags.Public);

        // Action property
        if (prop?.PropertyType == typeof(Action))
            return prop.GetValue(source) as Action;

        return null;
    }

    private void Fail(string message)
    {
        UnityEngine.Debug.LogError(message);
        _failed = true;
    }

    private void Teardown()
    {
        if (_button != null && _clickHandler != null)
            _button.clicked -= _clickHandler;

        if (_trackedCommand != null && _canExecuteHandler != null)
            _trackedCommand.CanExecuteChanged -= _canExecuteHandler;

        _button            = null;
        _clickHandler      = null;
        _trackedCommand    = null;
        _canExecuteHandler = null;
        _setup             = false;
        _failed            = false; // a new data source may satisfy the binding
    }
}

} // namespace Bindery
