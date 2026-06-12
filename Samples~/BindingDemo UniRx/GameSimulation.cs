using UnityEngine;

namespace Bindery.Samples.UniRxDemo
{

/// <summary>
/// Gameplay — stands in for "the rest of your game". Owns the PlayerData model
/// and mutates it from game logic: a monster attacks the player on a timer.
/// Knows nothing about the UI — the ViewModel observes the same model, so the
/// screen tracks every change without any UI code here.
/// </summary>
public class GameSimulation : MonoBehaviour
{
    [SerializeField] private float attackInterval = 4f;

    public PlayerData Player { get; private set; }

    private float _nextAttackTime;

    private void Awake()
    {
        Player = new PlayerData();
        _nextAttackTime = Time.time + attackInterval;
    }

    private void Update()
    {
        if (Time.time < _nextAttackTime) return;
        _nextAttackTime = Time.time + attackInterval;

        if (Player.IsAlive)
            Player.TakeDamage();
    }

    private void OnDestroy()
    {
        Player?.Dispose();
        Player = null;
    }
}

} // namespace Bindery.Samples.UniRxDemo
