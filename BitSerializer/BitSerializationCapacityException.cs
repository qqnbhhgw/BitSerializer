namespace BitSerializer;

public sealed class BitSerializationCapacityException : Exception
{
    public BitSerializationCapacityException(string memberName, int requiredCapacity)
        : base($"Preallocated member '{memberName}' requires capacity {requiredCapacity}.")
    {
    }
}
