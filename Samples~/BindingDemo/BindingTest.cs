using System;
using UnityEngine;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class BindingTest : MonoBehaviour
{
    private readonly BindingTestData _data = new();
    private BindingTestPresenter _presenter;

    private void OnEnable()
    {
        GetComponent<UIDocument>().rootVisualElement.dataSource = _data;
        _presenter = new BindingTestPresenter(_data);
    }

    private void OnDisable()
    {
        _presenter?.Dispose();
        _presenter = null;
    }

    private void OnDestroy()
    {
        _data.Dispose();
    }
}
