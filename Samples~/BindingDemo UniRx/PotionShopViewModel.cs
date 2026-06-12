using System;
using UniRx;

namespace Bindery.Samples.UniRxDemo
{

/// <summary>
/// ViewModel — adapts the PlayerData model to UI Toolkit bindings. Model state
/// is mirrored into [BindableProperty] fields (one-way: model -> UI), pure view
/// state (Quantity, Total) lives only here, and commands route user intent back
/// into model methods. The model never knows the UI exists.
///
/// UniRx's ReactiveCommand does not implement ICommand, so ClickBinding cannot
/// bind it directly and cannot auto-disable buttons from CanExecute. The
/// ViewModel exposes Action properties that route to the commands instead —
/// Execute() is a no-op while CanExecute is false, so the guard still applies
/// even though the buttons stay visually enabled.
/// </summary>
[BindableObject]
public partial class PotionShopViewModel : BindableObject
{
    // Mirrored from the model (display only — the UI never writes these)
    [BindableProperty] public ReactiveProperty<float> Health    = new(0f);
    [BindableProperty] public ReactiveProperty<float> MaxHealth = new(PlayerData.MaxHealth);
    [BindableProperty] public ReactiveProperty<int>   Gold      = new(0);
    [BindableProperty] public ReactiveProperty<int>   Owned     = new(0);

    // Pure view state — the shop form; the model does not care about it
    [BindableProperty] public ReactiveProperty<int>   Quantity  = new(1);
    [BindableProperty] public ReactiveProperty<int>   Price     = new(PlayerData.PotionPrice);
    [BindableProperty] public ReactiveProperty<int>   Total     = new(PlayerData.PotionPrice);

    public ReactiveCommand BuyCommand   { get; }
    public ReactiveCommand DrinkCommand { get; }

    // Bound by ClickBinding's Action shape: command="Buy" / command="Drink" in UXML.
    public Action Buy   { get; }
    public Action Drink { get; }

    private readonly PlayerData _player;

    public PotionShopViewModel(PlayerData player)
    {
        _player = player;

        // Model -> ViewModel mirror. Subscribe pushes the current value
        // immediately, so the initial state is synced too.
        Track(player.Health.Subscribe(v => Health.Value = v));
        Track(player.Gold.Subscribe(v => Gold.Value = v));
        Track(player.Potions.Subscribe(v => Owned.Value = v));

        BuyCommand   = Total.CombineLatest(Gold, PlayerData.CanBuy).ToReactiveCommand();
        DrinkCommand = Owned.CombineLatest(Health, PlayerData.CanDrink).ToReactiveCommand();
        Buy   = () => BuyCommand.Execute();
        Drink = () => DrinkCommand.Execute();

        Track(BuyCommand);
        Track(DrinkCommand);
        Track(BuyCommand.Subscribe(_ => _player.BuyPotions(Quantity.Value)));
        Track(DrinkCommand.Subscribe(_ => _player.DrinkPotion()));
    }

    // Generated partial callback — invoked whenever Quantity changes.
    partial void OnQuantityChanged(int quantity) => Total.Value = PlayerData.TotalPrice(quantity);

    // Bound by ClickBinding's method shape: command="TakeDamage" in UXML.
    public void TakeDamage() => _player.TakeDamage();
}

} // namespace Bindery.Samples.UniRxDemo
