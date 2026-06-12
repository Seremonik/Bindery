using System;
using UniRx;

namespace Bindery.Samples.UniRxDemo
{

/// <summary>
/// Model — the player's state as a plain C# class, shared by gameplay and UI.
/// GameSimulation mutates it (the monster attacks on a timer), the ViewModel
/// observes it for display. ReactiveProperties make every change observable by
/// both sides; there is no Unity or UI dependency here.
/// </summary>
public class PlayerData : IDisposable
{
    public const int   PotionPrice   = 25;
    public const float MaxHealth     = 100f;
    public const float HealPerPotion = 30f;
    public const float DamagePerHit  = 20f;

    public ReactiveProperty<float> Health  { get; } = new(40f);
    public ReactiveProperty<int>   Gold    { get; } = new(120);
    public ReactiveProperty<int>   Potions { get; } = new(0);

    public bool IsAlive => Health.Value > 0f;

    public static int  TotalPrice(int quantity)             => quantity * PotionPrice;
    public static bool CanBuy(int totalPrice, int gold)     => totalPrice > 0 && totalPrice <= gold;
    public static bool CanDrink(int potions, float health)  => potions > 0 && health < MaxHealth;

    public void TakeDamage() => Health.Value = Math.Max(0f, Health.Value - DamagePerHit);

    public void BuyPotions(int quantity)
    {
        var total = TotalPrice(quantity);
        if (!CanBuy(total, Gold.Value)) return;
        Gold.Value    -= total;
        Potions.Value += quantity;
    }

    public void DrinkPotion()
    {
        if (!CanDrink(Potions.Value, Health.Value)) return;
        Potions.Value -= 1;
        Health.Value   = Math.Min(MaxHealth, Health.Value + HealPerPotion);
    }

    public void Dispose()
    {
        Health.Dispose();
        Gold.Dispose();
        Potions.Dispose();
    }
}

} // namespace Bindery.Samples.UniRxDemo
