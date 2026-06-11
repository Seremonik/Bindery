using System;
using R3;

public class BindingTestPresenter : IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public BindingTestPresenter(BindingTestData data)
    {
        data.ResetCommand
            .Subscribe(_ => data.SliderValue.Value = 0f)
            .AddTo(_disposables);
    }

    public void Dispose() => _disposables.Dispose();
}
