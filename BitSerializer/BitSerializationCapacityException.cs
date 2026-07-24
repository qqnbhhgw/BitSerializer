namespace BitSerializer;

public sealed class BitSerializationCapacityException : Exception
{
    public BitSerializationCapacityException(string memberName, int requiredCapacity)
        : this(memberName, requiredCapacity, "has insufficient reusable capacity")
    {
    }

    public BitSerializationCapacityException(string memberName, int requiredCapacity, string reason)
        : base($"Preallocated member '{memberName}' {reason}; required capacity is {requiredCapacity}.")
    {
    }
}
