#nullable enable

namespace BitSerializer;

/// <summary>
/// 值转换器接口。
/// 若需在转换时访问序列化/反序列化上下文，请重写带 context 参数的重载。
/// </summary>
public interface IBitFieldValueConverter
{
    /// <summary>
    /// 在反序列化时，将原始数据转换为所需值
    /// </summary>
    static virtual object OnDeserializeConvert(object formDataValue) => formDataValue;

    /// <summary>
    /// 在序列化时，将字段值转换为需要写入的数据
    /// </summary>
    static virtual object OnSerializeConvert(object propertyValue) => propertyValue;

    /// <summary>
    /// 在反序列化时，将原始数据转换为所需值（可访问上下文）。
    /// 重写此方法以在转换时访问上下文对象。
    /// </summary>
    static virtual object OnDeserializeConvert(object formDataValue, object? context) => formDataValue;

    /// <summary>
    /// 在序列化时，将字段值转换为需要写入的数据（可访问上下文）。
    /// 重写此方法以在转换时访问上下文对象。
    /// </summary>
    static virtual object OnSerializeConvert(object propertyValue, object? context) => propertyValue;
}