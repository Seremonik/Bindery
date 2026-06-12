using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine.UIElements;

/// <summary>
/// Shared helpers for building compiled delegates used by FormatBinding, MultiBinding and ClickBinding.
/// Delegates are built once at setup time via Expression trees and replace MemberInfo.GetValue
/// calls on the hot path — eliminating reflection overhead while retaining type safety.
/// </summary>
namespace Bindery
{

internal static class BindingReflection
{
    const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    // Expression.Compile costs ~0.1-0.3 ms per delegate — without a cache,
    // 1000 list items binding the same member compile 1000 identical delegates
    // and freeze the first frame for hundreds of ms. Keyed by (type, member);
    // null results are cached too. Bindings run on the main thread only, so a
    // plain Dictionary is safe.
    static readonly Dictionary<(Type, string), Func<object, object>> GetterCache = new();
    static readonly Dictionary<(Type, string), (Action<object, object> Setter, Type PropertyType)> SetterCache = new();

    /// <summary>
    /// Builds a compiled getter for a field or property on <paramref name="dsType"/>.
    /// Automatically unwraps ReactiveProperty&lt;T&gt; to its inner Value.
    /// Returns null if the member is not found. Results are cached per (type, member).
    /// </summary>
    public static Func<object, object> BuildGetter(Type dsType, string memberName)
    {
        if (string.IsNullOrEmpty(memberName)) return null;

        var key = (dsType, memberName);
        if (GetterCache.TryGetValue(key, out var cached)) return cached;

        var getter = BuildGetterUncached(dsType, memberName);
        GetterCache[key] = getter;
        return getter;
    }

    static Func<object, object> BuildGetterUncached(Type dsType, string memberName)
    {
        MemberInfo member = dsType.GetProperty(memberName, PublicInstance)
                         ?? (MemberInfo)dsType.GetField(memberName, PublicInstance);
        if (member == null) return null;

        var memberType = member is PropertyInfo mpi
            ? mpi.PropertyType
            : ((FieldInfo)member).FieldType;

        var param  = Expression.Parameter(typeof(object));
        var cast   = Expression.Convert(param, dsType);

        Expression access = member is PropertyInfo pi
            ? Expression.Property(cast, pi)
            : Expression.Field(cast, (FieldInfo)member);

        // Unwrap ReactiveProperty<T> → .Value
        if (memberType.IsGenericType && memberType.Name == "ReactiveProperty`1")
        {
            var valueProp = memberType.GetProperty("Value");
            if (valueProp != null) access = Expression.Property(access, valueProp);
        }

        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(access, typeof(object)), param).Compile();
    }

    /// <summary>
    /// Builds a compiled setter for a property on <paramref name="elementType"/>.
    /// Returns null if the property is not found or has no setter;
    /// <paramref name="propertyType"/> is set whenever the property exists,
    /// so callers can produce precise error messages and validate value types.
    /// Results are cached per (type, property).
    /// </summary>
    public static Action<object, object> BuildSetter(Type elementType, string propertyName, out Type propertyType)
    {
        propertyType = null;
        if (string.IsNullOrEmpty(propertyName)) return null;

        var key = (elementType, propertyName);
        if (SetterCache.TryGetValue(key, out var cached))
        {
            propertyType = cached.PropertyType;
            return cached.Setter;
        }

        var result = BuildSetterUncached(elementType, propertyName, out propertyType);
        SetterCache[key] = (result, propertyType);
        return result;
    }

    static Action<object, object> BuildSetterUncached(Type elementType, string propertyName, out Type propertyType)
    {
        var prop = elementType.GetProperty(propertyName, PublicInstance);
        propertyType = prop?.PropertyType;
        var setter = prop?.GetSetMethod();
        if (setter == null) return null;

        var element = Expression.Parameter(typeof(object));
        var value   = Expression.Parameter(typeof(object));

        var call = Expression.Call(
            Expression.Convert(element, elementType),
            setter,
            Expression.Convert(value, prop.PropertyType));

        return Expression.Lambda<Action<object, object>>(call, element, value).Compile();
    }

    /// Walks the visual element hierarchy to find the nearest data source.
    public static object FindDataSource(VisualElement element)
    {
        var el = element;
        while (el != null)
        {
            if (el.dataSource != null) return el.dataSource;
            el = el.parent;
        }
        return null;
    }
}

} // namespace Bindery
