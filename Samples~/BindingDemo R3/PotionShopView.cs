using UnityEngine;
using UnityEngine.UIElements;

namespace Bindery.Samples.R3Demo
{

/// <summary>
/// View — the MonoBehaviour glue. Takes the PlayerData model from
/// GameSimulation, wraps it in a ViewModel and exposes it to the UXML bindings
/// via dataSource. All presentation logic lives in PotionShopView.uxml.
/// </summary>
[RequireComponent(typeof(UIDocument))]
public class PotionShopView : MonoBehaviour
{
    [SerializeField] private GameSimulation game;

    private PotionShopViewModel _viewModel;

    private void OnEnable()
    {
        _viewModel = new PotionShopViewModel(game.Player);
        GetComponent<UIDocument>().rootVisualElement.dataSource = _viewModel;
    }

    private void OnDisable()
    {
        _viewModel?.Dispose();
        _viewModel = null;
    }
}

} // namespace Bindery.Samples.R3Demo
