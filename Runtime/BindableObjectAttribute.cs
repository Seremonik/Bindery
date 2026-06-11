using System;

/// <summary>
/// Marks a partial class for source generation.
/// The generator will implement InitBindings() and create [CreateProperty] wrappers
/// for all [BindableProperty] ReactiveProperty fields.
/// </summary>
namespace Bindery
{

[AttributeUsage(AttributeTargets.Class)]
public sealed class BindableObjectAttribute : Attribute { }

} // namespace Bindery
