using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using R3;
using Unity.PerformanceTesting;
using Unity.Properties;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Bindery.PerformanceTests
{

/// <summary>
/// ViewModel used by all performance tests — one bindable float per item,
/// mirroring the common "list of stat bars" shape.
/// </summary>
[BindableObject]
public partial class PerfItemViewModel : BindableObject
{
    [BindableProperty] public ReactiveProperty<float> Value = new(0f);
}

/// <summary>Invokes a callback once per frame while Measure.Frames captures.</summary>
class FrameDriver : MonoBehaviour
{
    public Action Tick;
    private void Update() => Tick?.Invoke();
}

/// <summary>
/// Performance characterization of Bindery's hot paths against two baselines:
/// hand-written ReactiveProperty subscriptions (the floor) and Unity's built-in
/// DataBinding through the generated property bag.
///
/// All bound elements live in a display:none container — this excludes text
/// layout/render costs (identical across approaches) so the numbers isolate
/// binding-pipeline cost.
/// </summary>
public class BindingPerformanceTests
{
    private GameObject     _go;
    private UIDocument     _doc;
    private PanelSettings  _panelSettings;
    private VisualElement  _container;
    private readonly List<IDisposable> _manualSubscriptions = new();

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // float -> string conversion for the built-in DataBinding baseline.
        // Registration survives within a session; ignore duplicate adds.
        try
        {
            ConverterGroups.RegisterGlobalConverter((ref float v) =>
                v.ToString("F1", CultureInfo.InvariantCulture));
        }
        catch (Exception) { /* already registered in this session */ }
    }

    [UnitySetUp]
    public IEnumerator UnitySetUp()
    {
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _go  = new GameObject("BinderyPerf");
        _doc = _go.AddComponent<UIDocument>();
        _doc.panelSettings = _panelSettings;
        yield return null; // let the panel attach

        _container = new VisualElement { name = "perf-container" };
        _container.style.display = DisplayStyle.None;
        _doc.rootVisualElement.Add(_container);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var d in _manualSubscriptions) d.Dispose();
        _manualSubscriptions.Clear();
        UnityEngine.Object.Destroy(_go);
        UnityEngine.Object.Destroy(_panelSettings);
    }

    // ------------------------------------------------------------------ helpers

    private PerfItemViewModel[] CreateBinderyItems(int count, VisualElement parent = null)
    {
        parent ??= _container;
        var vms = new PerfItemViewModel[count];
        for (int i = 0; i < count; i++)
        {
            var vm = new PerfItemViewModel();
            var label = new Label("0") { dataSource = vm };
            label.SetBinding(new BindingId("text"), new FormatBinding { source = "Value", format = "{0:F1}" });
            parent.Add(label);
            vms[i] = vm;
        }
        return vms;
    }

    private PerfItemViewModel[] CreateBuiltinItems(int count, VisualElement parent = null)
    {
        parent ??= _container;
        var vms = new PerfItemViewModel[count];
        for (int i = 0; i < count; i++)
        {
            var vm = new PerfItemViewModel();
            var label = new Label("0") { dataSource = vm };
            label.SetBinding(new BindingId("text"), new DataBinding
            {
                dataSourcePath = new PropertyPath("Value"),
                bindingMode    = BindingMode.ToTarget,
            });
            parent.Add(label);
            vms[i] = vm;
        }
        return vms;
    }

    private ReactiveProperty<float>[] CreateManualItems(int count)
    {
        var rps = new ReactiveProperty<float>[count];
        for (int i = 0; i < count; i++)
        {
            var rp = new ReactiveProperty<float>(0f);
            var label = new Label("0");
            _container.Add(label);
            _manualSubscriptions.Add(rp.Subscribe(v => label.text = v.ToString("F1", CultureInfo.InvariantCulture)));
            _manualSubscriptions.Add(rp);
            rps[i] = rp;
        }
        return rps;
    }

    private static IEnumerator Settle(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    private IEnumerator MeasureIdleFrames()
    {
        yield return Settle(10); // let binding Setup complete
        yield return Measure.Frames().WarmupCount(10).MeasurementCount(50).Run();
    }

    private IEnumerator MeasureStormFrames(Action<float> mutateAll)
    {
        yield return Settle(10);
        var driver = _go.AddComponent<FrameDriver>();
        float frame = 0f;
        driver.Tick = () => { frame += 1f; mutateAll(frame); };
        yield return Measure.Frames().WarmupCount(10).MeasurementCount(40).Run();
        driver.Tick = null;
    }

    // GC.GetAllocatedBytesForCurrentThread() is a stub on Unity's Mono (always
    // returns 0), so measure heap growth instead: force a clean heap, run the
    // loop, and read the delta before any collection can hide it.
    private static void MeasureAllocPerChange(Action change)
    {
        const int n = 1000;
        change(); // warm any lazy paths before counting

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetTotalMemory(false);

        for (int i = 0; i < n; i++) change();

        long delta = GC.GetTotalMemory(false) - before;
        Measure.Custom(new SampleGroup("GC Alloc per change", SampleUnit.Byte), Math.Max(0, delta) / (double)n);
    }

    // ------------------------------------------------- idle cost (no changes)

    [UnityTest, Performance]
    public IEnumerator Idle_EmptyPanel()
    {
        yield return MeasureIdleFrames();
    }

    [UnityTest, Performance]
    public IEnumerator Idle_BuiltinDataBinding_1000()
    {
        CreateBuiltinItems(1000);
        yield return MeasureIdleFrames();
    }

    [UnityTest, Performance]
    public IEnumerator Idle_Bindery_1000()
    {
        CreateBinderyItems(1000);
        yield return MeasureIdleFrames();
    }

    [UnityTest, Performance]
    public IEnumerator Idle_Bindery_5000()
    {
        CreateBinderyItems(5000);
        yield return MeasureIdleFrames();
    }

    // ------------------------------------- change storm (every value, every frame)

    [UnityTest, Performance]
    public IEnumerator Storm_Manual_1000()
    {
        var rps = CreateManualItems(1000);
        yield return MeasureStormFrames(f =>
        {
            for (int i = 0; i < rps.Length; i++) rps[i].Value = f + i;
        });
    }

    [UnityTest, Performance]
    public IEnumerator Storm_BuiltinDataBinding_1000()
    {
        var vms = CreateBuiltinItems(1000);
        yield return MeasureStormFrames(f =>
        {
            for (int i = 0; i < vms.Length; i++) vms[i].Value.Value = f + i;
        });
    }

    [UnityTest, Performance]
    public IEnumerator Storm_Bindery_1000()
    {
        var vms = CreateBinderyItems(1000);
        yield return MeasureStormFrames(f =>
        {
            for (int i = 0; i < vms.Length; i++) vms[i].Value.Value = f + i;
        });
    }

    // --------------------------------------------- single change: latency + GC

    [UnityTest, Performance]
    public IEnumerator SingleChange_Manual()
    {
        var rps = CreateManualItems(1);
        yield return Settle(5);

        float v = 0f;
        Measure.Method(() => { v += 1f; rps[0].Value = v; })
            .WarmupCount(5).IterationsPerMeasurement(1000).MeasurementCount(15).Run();
        MeasureAllocPerChange(() => { v += 1f; rps[0].Value = v; });
    }

    [UnityTest, Performance]
    public IEnumerator SingleChange_Bindery()
    {
        var vms = CreateBinderyItems(1);
        yield return Settle(5);

        float v = 0f;
        Measure.Method(() => { v += 1f; vms[0].Value.Value = v; })
            .WarmupCount(5).IterationsPerMeasurement(1000).MeasurementCount(15).Run();
        MeasureAllocPerChange(() => { v += 1f; vms[0].Value.Value = v; });
    }

    // ------------------------------ fan-out: one property, many bound elements

    private IEnumerator FanOut(int bindingCount)
    {
        var vm = new PerfItemViewModel();
        _container.dataSource = vm;
        for (int i = 0; i < bindingCount; i++)
        {
            var label = new Label("0");
            label.SetBinding(new BindingId("text"), new FormatBinding { source = "Value", format = "{0:F1}" });
            _container.Add(label);
        }
        yield return Settle(10);

        float v = 0f;
        Measure.Method(() => { v += 1f; vm.Value.Value = v; })
            .WarmupCount(3).IterationsPerMeasurement(50).MeasurementCount(10).Run();
    }

    [UnityTest, Performance]
    public IEnumerator FanOut_Bindery_100() => FanOut(100);

    [UnityTest, Performance]
    public IEnumerator FanOut_Bindery_1000() => FanOut(1000);

    // ----------------------- setup spike: first frame after attaching N bindings

    private IEnumerator SetupSpike(Func<int, VisualElement, PerfItemViewModel[]> create)
    {
        yield return Settle(5);
        var sw = new System.Diagnostics.Stopwatch();
        var group = new SampleGroup("FirstFrameAfterAttach", SampleUnit.Millisecond);

        for (int rep = 0; rep < 5; rep++)
        {
            var sub = new VisualElement();
            sub.style.display = DisplayStyle.None;
            _container.Add(sub);
            create(1000, sub);

            sw.Restart();
            yield return null; // the frame in which the binding system runs Setup
            sw.Stop();
            Measure.Custom(group, sw.Elapsed.TotalMilliseconds);

            sub.RemoveFromHierarchy();
            yield return null;
        }
    }

    [UnityTest, Performance]
    public IEnumerator Setup_Bindery_1000() =>
        SetupSpike((n, parent) => CreateBinderyItems(n, parent));

    [UnityTest, Performance]
    public IEnumerator Setup_BuiltinDataBinding_1000() =>
        SetupSpike((n, parent) => CreateBuiltinItems(n, parent));
}

} // namespace Bindery.PerformanceTests
