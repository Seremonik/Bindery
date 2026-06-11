using System;

/// <summary>
/// Mark a public ReactiveProperty&lt;T&gt; field so the source generator
/// produces a ContainerPropertyBag entry and wires change notifications.
/// Must be assigned with a field initializer — see BindableObject docs.
///
/// Example:
///   [BindableProperty] public ReactiveProperty&lt;float&gt; Speed = new(10f);
/// </summary>
namespace Bindery
{

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class BindablePropertyAttribute : Attribute { }

} // namespace Bindery
