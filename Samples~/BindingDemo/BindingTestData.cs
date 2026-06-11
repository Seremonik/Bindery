using Bindery;
using R3;

[BindableObject]
public partial class BindingTestData : BindableObject
{
    [BindableProperty] public ReactiveProperty<float> SliderValue = new(42f);
    [BindableProperty] public ReactiveProperty<float> MaxValue    = new(60f);

    public ReactiveCommand ResetCommand { get; }

    public BindingTestData()
    {
        ResetCommand = SliderValue.Select(v => v > 50f).ToReactiveCommand(initialCanExecute: false);
    }
}
