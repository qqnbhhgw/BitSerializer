namespace BitSerializer;

/// <summary>
/// 值转换器接口
/// </summary>
public interface IBitFieldValueConverter
{
    /// <summary>
    /// 在反序列化时，将原始数据转换为所需值
    /// </summary>
    /// <param name="formDataValue"></param>
    /// <returns></returns>
   static abstract object OnDeserializeConvert(object formDataValue);

    /// <summary>
    /// 在序列化时，将字段值转换为需要写入的数据
    /// </summary>
    /// <param name="propertyValue"></param>
    /// <returns></returns>
    static abstract object OnSerializeConvert(object propertyValue);
}
