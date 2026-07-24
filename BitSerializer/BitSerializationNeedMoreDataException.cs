namespace BitSerializer;

public sealed class BitSerializationNeedMoreDataException : Exception
{
    public BitSerializationNeedMoreDataException(string message)
        : base(message)
    {
    }
}
