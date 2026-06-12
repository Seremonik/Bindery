using System;
using System.Collections;
using System.Globalization;
using NUnit.Framework;
using R3;
using Unity.Properties;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Bindery.Tests
{

/// <summary>
/// ViewModel exercising every binding-relevant feature: a formatted property,
/// a generated change callback, an ICommand with CanExecute, and a plain method.
/// </summary>
[BindableObject]
public partial class ShopViewModel : BindableObject
{
    [BindableProperty] public ReactiveProperty<float> Health    = new(40f);
    [BindableProperty] public ReactiveProperty<float> MaxHealth = new(100f);

    public ReactiveCommand HealCommand { get; }

    public int   HealCount;
    public int   FightCount;
    public float LastChangedValue   = float.NaN;
    // Records whether the model was already consistent the first time the
    // change callback ran — proves On<Name>Changed runs before Notify.
    public bool  CallbackSawNewValue;

    public ShopViewModel()
    {
        HealCommand = Health.Select(h => h < MaxHealth.Value).ToReactiveCommand();
        Track(HealCommand);
        Track(HealCommand.Subscribe(_ =>
        {
            HealCount++;
            Health.Value = MaxHealth.Value;
        }));
    }

    partial void OnHealthChanged(float newValue)
    {
        LastChangedValue   = newValue;
        CallbackSawNewValue = Math.Abs(Health.Value - newValue) < 0.0001f;
    }

    // Bound by ClickBinding's method shape.
    public void Fight() => FightCount++;
}

/// <summary>
/// Correctness tests for the binding pipeline. Unlike the performance suite these
/// assert behavior, so a failure here on an IL2CPP player build means an AOT or
/// managed-stripping problem — exactly the paths that never surface in the editor.
/// They are intentionally fast and assertion-only (no [Performance]).
/// </summary>
public class BindingCorrectnessTests
{
    private GameObject    _go;
    private UIDocument    _doc;
    private PanelSettings _panelSettings;
    private VisualElement _root;

    [UnitySetUp]
    public IEnumerator UnitySetUp()
    {
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _go  = new GameObject("BinderyCorrectness");
        _doc = _go.AddComponent<UIDocument>();
        _doc.panelSettings = _panelSettings;
        yield return null;
        _root = _doc.rootVisualElement;
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.Destroy(_go);
        UnityEngine.Object.Destroy(_panelSettings);
    }

    // Bindings resolve their data source and run Setup over a few frames.
    private IEnumerator Settle() { for (int i = 0; i < 5; i++) yield return null; }

    private static void Click(Button button)
    {
        using var e = new NavigationSubmitEvent { target = button };
        button.SendEvent(e);
    }

    // ---- Generated property bag: the path built-in ui:DataBinding relies on ----

    [UnityTest]
    public IEnumerator PropertyBag_ReadsAndWrites_GeneratedProperties()
    {
        var vm = new ShopViewModel();
        object boxed = vm;

        Assert.IsTrue(PropertyContainer.TryGetValue(ref boxed, new PropertyPath("Health"), out float read),
            "Generated property bag did not expose Health — bag registration failed (likely AOT/stripping).");
        Assert.AreEqual(40f, read, 0.0001f);

        Assert.IsTrue(PropertyContainer.TrySetValue(ref boxed, new PropertyPath("Health"), 73f),
            "Generated property bag could not write Health.");
        Assert.AreEqual(73f, vm.Health.Value, 0.0001f, "Write did not reach the ReactiveProperty.");
        yield break;
    }

    // ---- FormatBinding: reflective source lookup by string name ----

    [UnityTest]
    public IEnumerator FormatBinding_WritesFormattedValue_AndTracksChanges()
    {
        var vm = new ShopViewModel();
        _root.dataSource = vm;
        var label = new Label("init");
        label.SetBinding(new BindingId("text"), new FormatBinding { source = "Health", format = "{0:F0} HP" });
        _root.Add(label);
        yield return Settle();

        Assert.AreEqual("40 HP", label.text, "FormatBinding did not apply initial value (reflective source lookup may have been stripped).");

        vm.Health.Value = 75f;
        yield return null;
        Assert.AreEqual("75 HP", label.text, "FormatBinding did not react to a change.");
    }

    // ---- MultiBinding: multiple reflective source lookups + format ----

    [UnityTest]
    public IEnumerator MultiBinding_CombinesSources()
    {
        var vm = new ShopViewModel();
        _root.dataSource = vm;
        var label = new Label("init");
        label.SetBinding(new BindingId("text"), new MultiBinding
        {
            format  = "{0:F0} / {1:F0}",
            sources = new System.Collections.Generic.List<Source>
            {
                new Source { path = "Health" },
                new Source { path = "MaxHealth" },
            },
        });
        _root.Add(label);
        yield return Settle();

        Assert.AreEqual("40 / 100", label.text);

        vm.Health.Value = 88f;
        yield return null;
        Assert.AreEqual("88 / 100", label.text, "MultiBinding did not recompute when a source changed.");
    }

    // ---- ClickBinding, ICommand shape: the highest stripping risk (command
    //      resolved purely by string name) plus CanExecute auto-disable ----

    [UnityTest]
    public IEnumerator ClickBinding_ICommand_ExecutesAndAutoDisables()
    {
        var vm = new ShopViewModel();
        _root.dataSource = vm;
        var button = new Button { text = "Heal" };
        button.SetBinding(new BindingId("name"), new ClickBinding { command = "HealCommand" });
        _root.Add(button);
        yield return Settle();

        Assert.IsTrue(button.enabledSelf, "Button should start enabled (Health 40 < 100, CanExecute true).");

        Click(button);
        yield return null;

        Assert.AreEqual(1, vm.HealCount, "HealCommand did not execute (command lookup by name may have been stripped).");
        Assert.AreEqual(100f, vm.Health.Value, 0.0001f);
        Assert.IsFalse(button.enabledSelf, "Button should auto-disable once CanExecute turns false.");
    }

    // ---- ClickBinding, method shape: parameterless void by name ----

    [UnityTest]
    public IEnumerator ClickBinding_Method_Invokes()
    {
        var vm = new ShopViewModel();
        _root.dataSource = vm;
        var button = new Button { text = "Fight" };
        button.SetBinding(new BindingId("name"), new ClickBinding { command = "Fight" });
        _root.Add(button);
        yield return Settle();

        Click(button);
        yield return null;
        Click(button);
        yield return null;

        Assert.AreEqual(2, vm.FightCount, "ClickBinding did not invoke the bound method.");
    }

    // ---- Generated change callback: fires with the new value, before Notify ----

    [UnityTest]
    public IEnumerator GeneratedCallback_RunsWithNewValue()
    {
        var vm = new ShopViewModel();
        yield return null;

        vm.Health.Value = 55f;

        Assert.AreEqual(55f, vm.LastChangedValue, 0.0001f, "OnHealthChanged did not receive the new value.");
        Assert.IsTrue(vm.CallbackSawNewValue, "Model state was not yet consistent inside the callback (callback ran after Notify?).");
    }

    // ---- Disposal: subscriptions stop after the ViewModel is disposed ----

    [UnityTest]
    public IEnumerator Disposal_StopsNotifications()
    {
        var vm = new ShopViewModel();
        _root.dataSource = vm;
        var label = new Label("init");
        label.SetBinding(new BindingId("text"), new FormatBinding { source = "Health", format = "{0:F0}" });
        _root.Add(label);
        yield return Settle();

        Assert.AreEqual("40", label.text);

        vm.Dispose();
        // After disposal the ReactiveProperty is disposed; mutating it must not
        // throw back through the binding and must not update the label.
        Assert.DoesNotThrow(() =>
        {
            try { vm.Health.Value = 999f; } catch (ObjectDisposedException) { /* expected for a disposed RP */ }
        });
        yield return null;
        Assert.AreEqual("40", label.text, "Label updated after the ViewModel was disposed.");
    }
}

} // namespace Bindery.Tests
