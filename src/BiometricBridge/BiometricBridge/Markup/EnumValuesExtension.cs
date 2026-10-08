using System;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;

namespace BiometricBridge.Markup;

/// <summary>
/// 枚举取值标记扩展：把枚举的全部取值交给控件（如 ComboBox 的 ItemsSource），供以后各类枚举绑定复用。
/// </summary>
/// <example>
/// <code>
/// &lt;ComboBox ItemsSource="{markup:EnumValues {x:Type enums:BiometricDeviceStatus}}" /&gt;
/// </code>
/// </example>
/// <remarks>
/// <c>x:Type</c> 是 Avalonia 的编译器 intrinsic，赋的是 <see cref="System.Type"/> 字面量，
/// 可作位置实参传给本扩展。这里返回的是裸数组而非绑定，故与绑定模式（compiled/reflection）无关。
/// </remarks>
public sealed class EnumValuesExtension : MarkupExtension
{
    /// <summary>
    /// 无参构造：供 XAML 以具名属性形式使用（<c>{markup:EnumValues EnumType={x:Type ...}}</c>）。
    /// </summary>
    public EnumValuesExtension()
    {
    }

    /// <summary>
    /// 构造标记扩展。
    /// </summary>
    /// <param name="enumType">
    /// 目标枚举类型。
    /// </param>
    public EnumValuesExtension(Type enumType) => EnumType = enumType;

    /// <summary>
    /// 要取值的枚举类型。
    /// </summary>
    /// <remarks>
    /// <see cref="ConstructorArgumentAttribute"/> 的参数须与构造函数参数名 <c>enumType</c> 一致，
    /// 否则位置实参绑不上。
    /// </remarks>
    [ConstructorArgument("enumType")]
    public Type? EnumType { get; set; }

    /// <summary>
    /// 提供枚举的全部取值。
    /// </summary>
    /// <param name="serviceProvider">
    /// XAML 服务提供程序。
    /// </param>
    /// <returns>
    /// 枚举取值的数组；未指定 <see cref="EnumType"/> 时返回空数组。
    /// </returns>
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return EnumType is null ? Array.Empty<object>() : Enum.GetValues(EnumType);
    }
}
