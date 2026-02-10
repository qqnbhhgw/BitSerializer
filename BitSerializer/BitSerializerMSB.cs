#nullable enable
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace BitSerializer;

public static class BitSerializerMSB
{
    private static readonly ConcurrentDictionary<Type, Delegate>     DeserializerCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate>     SerializerCache = new();
    private static readonly ConcurrentDictionary<Type, TypeMetadata> TypeMetadataCache = new();

    // Cache for nested type deserializers: Func<byte[], int, T> where int is bitStartIndex
    private static readonly ConcurrentDictionary<Type, Delegate> NestedDeserializerCache = new();

    // Cache for nested type serializers: Action<byte[], int, T> where int is bitStartIndex
    private static readonly ConcurrentDictionary<Type, Delegate> NestedSerializerCache = new();

    // Cache for list element deserializers (for nested types in lists)
    private static readonly ConcurrentDictionary<Type, Delegate> ListElementDeserializerCache = new();

    // Cache for list element serializers
    private static readonly ConcurrentDictionary<Type, Delegate> ListElementSerializerCache = new();

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : new()
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));
        var deserializer = GetOrCreateDeserializer<T>(metadata);
        return deserializer(bytes.ToArray());
    }

    public static T Deserialize<T>(byte[] bytes) where T : new()
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));
        var deserializer = GetOrCreateDeserializer<T>(metadata);
        return deserializer(bytes);
    }

    private static Func<byte[], T> GetOrCreateDeserializer<T>(TypeMetadata metadata) where T : new()
    {
        return (Func<byte[], T>)DeserializerCache.GetOrAdd(typeof(T), _ => CreateDeserializer<T>(metadata));
    }

    private static TypeMetadata GetOrCreateTypeMetadata(Type type)
    {
        return TypeMetadataCache.GetOrAdd(type, CreateTypeMetadata);
    }

    private static TypeMetadata CreateTypeMetadata(Type type)
    {
        var members = GetMembersInOrder(type);
        ValidateAllMembersHaveAttributes(type, members);

        var fieldInfos = new List<BitFieldInfo>();
        var currentBitIndex = 0;

        foreach (var member in members)
        {
            var bitFieldAttr = member.GetCustomAttribute<BitFieldAttribute>();
            var bitIgnoreAttr = member.GetCustomAttribute<BitIgnoreAttribute>();
            var bitRelatedAttr = member.GetCustomAttribute<BitFieldRelatedAttribute>();
            var bitCountAttr = member.GetCustomAttribute<BitFiledCountAttribute>();
            var bitPolyAttrs = member.GetCustomAttributes<BitPolyAttribute>().ToList();

            if (bitIgnoreAttr != null)
                continue;

            if (bitFieldAttr == null)
                throw new InvalidOperationException(
                    $"Member '{member.Name}' in type '{type.Name}' must have BitFieldAttribute or BitIgnoreAttribute.");

            var memberType = GetMemberType(member);
            var isListType = IsListType(memberType);
            var isPolymorphic = bitPolyAttrs.Count > 0;
            var isSupportedType = IsNumericOrEnumType(memberType) || isListType ||
                                  IsNestedBitSerializableType(memberType) || isPolymorphic;

            if (!isSupportedType)
                throw new InvalidOperationException(
                    $"Member '{member.Name}' in type '{type.Name}' has unsupported type '{memberType.Name}'. " +
                    "Only numeric types, enums, List<T>, and nested bit-serializable types are supported. " +
                    "Use BitIgnoreAttribute to exclude this member.");

            var bitStartIndex = currentBitIndex;

            int bitLength;
            if (bitFieldAttr.BitLength == null)
            {
                if (IsNumericOrEnumType(memberType))
                {
                    bitLength = GetBitLengthForType(memberType);
                }
                else if (isListType || IsNestedBitSerializableType(memberType) || isPolymorphic)
                {
                    // For List, nested types, and polymorphic types, bitLength will be calculated dynamically
                    bitLength = 0;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"BitLength must be specified for member '{member.Name}' in type '{type.Name}'.");
                }
            }
            else
            {
                bitLength = bitFieldAttr.BitLength!.Value;
            }

            string? relatedMemberName = null;
            int? fixedCount = null;
            if (isListType || isPolymorphic)
            {
                // BitFiledCountAttribute takes priority over BitFieldRelatedAttribute for lists
                if (isListType && bitCountAttr != null)
                {
                    fixedCount = bitCountAttr.Count;
                }
                else if (bitRelatedAttr == null || string.IsNullOrEmpty(bitRelatedAttr.RelatedMemberName))
                {
                    if (isListType)
                        throw new InvalidOperationException(
                            $"List member '{member.Name}' in type '{type.Name}' must have BitFieldRelatedAttribute " +
                            "with RelatedMemberName specifying the count field, or BitFiledCountAttribute specifying a fixed count.");
                    if (isPolymorphic)
                        throw new InvalidOperationException(
                            $"Polymorphic member '{member.Name}' in type '{type.Name}' must have BitFieldRelatedAttribute " +
                            "with RelatedMemberName specifying the type discriminator field.");
                }

                if (fixedCount == null)
                    relatedMemberName = bitRelatedAttr!.RelatedMemberName;
            }

            // Extract and validate ValueConverterType
            Type? valueConverterType = null;
            if (bitRelatedAttr?.ValueConverterType != null)
            {
                if (!typeof(IBitFieldValueConverter).IsAssignableFrom(bitRelatedAttr.ValueConverterType))
                    throw new InvalidOperationException(
                        $"ValueConverterType '{bitRelatedAttr.ValueConverterType.Name}' for member '{member.Name}' " +
                        $"in type '{type.Name}' must implement IBitFieldValueConverter.");
                valueConverterType = bitRelatedAttr.ValueConverterType;
            }

            var fieldInfo = new BitFieldInfo
            {
                Member = member,
                MemberType = memberType,
                BitStartIndex = bitStartIndex,
                BitLength = bitLength,
                IsListType = isListType,
                FixedCount = fixedCount,
                RelatedMemberName = relatedMemberName,
                ValueConverterType = valueConverterType,
                IsNestedType = !IsNumericOrEnumType(memberType) && !isListType,
                IsPolymorphic = isPolymorphic
            };

            // Handle polymorphic types
            if (isPolymorphic)
            {
                fieldInfo.PolyTypeMappings = new Dictionary<int, Type>();
                fieldInfo.PolyTypeMetadatas = new Dictionary<int, TypeMetadata>();

                foreach (var polyAttr in bitPolyAttrs)
                {
                    fieldInfo.PolyTypeMappings[polyAttr.TypId] = polyAttr.Type;
                    // Build TypeMetadata for all listed polymorphic types
                    fieldInfo.PolyTypeMetadatas[polyAttr.TypId] = GetOrCreateTypeMetadata(polyAttr.Type);
                }

                // Calculate bitLength from the maximum of all polymorphic types if not specified
                if (bitLength == 0)
                {
                    bitLength = fieldInfo.PolyTypeMetadatas.Values.Max(m => m.TotalBitLength);
                    fieldInfo.BitLength = bitLength;
                }
            }
            else if (fieldInfo.IsNestedType)
            {
                fieldInfo.NestedMetadata = GetOrCreateTypeMetadata(memberType);
                if (bitLength == 0)
                {
                    bitLength = fieldInfo.NestedMetadata.TotalBitLength;
                    fieldInfo.BitLength = bitLength;
                }
            }

            if (isListType)
            {
                var elementType = memberType.GetGenericArguments()[0];
                if (IsNestedBitSerializableType(elementType))
                {
                    fieldInfo.ListElementMetadata = GetOrCreateTypeMetadata(elementType);
                }
            }

            fieldInfos.Add(fieldInfo);
            currentBitIndex = bitStartIndex + bitLength;
        }

        return new TypeMetadata
        {
            Type = type,
            Fields = fieldInfos,
            TotalBitLength = currentBitIndex
        };
    }

    private static void ValidateAllMembersHaveAttributes(Type type, List<MemberInfo> members)
    {
        foreach (var member in members)
        {
            var hasBitField = member.GetCustomAttribute<BitFieldAttribute>() != null;
            var hasBitIgnore = member.GetCustomAttribute<BitIgnoreAttribute>() != null;

            if (!hasBitField && !hasBitIgnore)
                throw new InvalidOperationException(
                    $"Member '{member.Name}' in type '{type.Name}' must have either BitFieldAttribute or BitIgnoreAttribute.");
        }
    }

    private static List<MemberInfo> GetMembersInOrder(Type type)
    {
        var members = new List<MemberInfo>();

        // Get properties and fields in declaration order
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Cast<MemberInfo>();

        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Cast<MemberInfo>();

        // Combine and sort by MetadataToken to maintain declaration order
        members.AddRange(props.Concat(fields).OrderBy(m => m.MetadataToken));

        return members;
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            PropertyInfo prop => prop.PropertyType,
            FieldInfo field   => field.FieldType,
            _                 => throw new InvalidOperationException($"Unsupported member type: {member.GetType()}")
        };
    }

    private static bool IsNumericOrEnumType(Type type)
    {
        if (type.IsEnum) return true;

        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong);
    }

    private static bool IsListType(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
    }

    private static bool IsNestedBitSerializableType(Type type)
    {
        if (IsNumericOrEnumType(type) || IsListType(type))
            return false;

        if (!type.IsClass || type == typeof(string))
            return false;

        // Check if all members have BitFieldAttribute or BitIgnoreAttribute
        var members = GetMembersInOrder(type);
        return members.All(m =>
            m.GetCustomAttribute<BitFieldAttribute>() != null ||
            m.GetCustomAttribute<BitIgnoreAttribute>() != null);
    }

    private static int GetBitLengthForType(Type type)
    {
        var actualType = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

        if (actualType == typeof(byte) || actualType == typeof(sbyte)) return 8;
        if (actualType == typeof(short) || actualType == typeof(ushort)) return 16;
        if (actualType == typeof(int) || actualType == typeof(uint)) return 32;
        if (actualType == typeof(long) || actualType == typeof(ulong)) return 64;

        throw new InvalidOperationException($"Cannot determine bit length for type: {type.Name}");
    }

    private static Func<byte[], T> CreateDeserializer<T>(TypeMetadata metadata) where T : new()
    {
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var resultVar = Expression.Variable(typeof(T), "result");
        var bitIndexVar = Expression.Variable(typeof(int), "bitIndex");

        var expressions = new List<Expression>
        {
            Expression.Assign(resultVar, Expression.New(typeof(T))),
            Expression.Assign(bitIndexVar, Expression.Constant(0))
        };

        foreach (var field in metadata.Fields)
        {
            var memberAccess = field.Member switch
            {
                PropertyInfo prop => Expression.Property(resultVar, prop),
                FieldInfo fi      => Expression.Field(resultVar, fi),
                _                 => throw new InvalidOperationException()
            };

            if (field.IsListType)
            {
                expressions.Add(CreateListDeserializationExpression(
                    bytesParam, resultVar, bitIndexVar, field, memberAccess, metadata));
            }
            else if (field.IsPolymorphic)
            {
                expressions.Add(CreatePolymorphicDeserializationExpression(
                    bytesParam, resultVar, bitIndexVar, field, memberAccess, metadata));
            }
            else if (field.IsNestedType)
            {
                expressions.Add(CreateNestedDeserializationExpression(
                    bytesParam, bitIndexVar, field, memberAccess));
            }
            else
            {
                expressions.Add(CreatePrimitiveDeserializationExpression(
                    bytesParam, bitIndexVar, field, memberAccess));
            }
        }

        expressions.Add(resultVar);

        var body = Expression.Block([resultVar, bitIndexVar], expressions);

        var lambda = Expression.Lambda<Func<byte[], T>>(body, bytesParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Wraps an expression with OnDeserializeConvert if the field has a ValueConverterType.
    /// </summary>
    private static Expression WrapWithDeserializeConverter(Expression value, BitFieldInfo field)
    {
        if (field.ValueConverterType == null)
            return value;

        var method = field.ValueConverterType.GetMethod(
            nameof(IBitFieldValueConverter.OnDeserializeConvert),
            BindingFlags.Public | BindingFlags.Static)!;
        return Expression.Convert(
            Expression.Call(method, Expression.Convert(value, typeof(object))),
            field.MemberType);
    }

    /// <summary>
    /// Wraps an expression with OnSerializeConvert if the field has a ValueConverterType.
    /// </summary>
    private static Expression WrapWithSerializeConverter(Expression value, BitFieldInfo field)
    {
        if (field.ValueConverterType == null)
            return value;

        var method = field.ValueConverterType.GetMethod(
            nameof(IBitFieldValueConverter.OnSerializeConvert),
            BindingFlags.Public | BindingFlags.Static)!;
        return Expression.Convert(
            Expression.Call(method, Expression.Convert(value, typeof(object))),
            field.MemberType);
    }

    private static Expression CreatePrimitiveDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess)
    {
        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call BitHelperMSB.ValueLength<T>(bytes, bitIndex, bitLength)
        var valueLengthMethod = typeof(BitHelperMSB)
            .GetMethod(nameof(BitHelperMSB.ValueLength))!
            .MakeGenericMethod(field.MemberType);

        var spanFromArray = Expression.Call(
            typeof(BitSerializerMSB).GetMethod(nameof(CreateReadOnlySpan), BindingFlags.NonPublic | BindingFlags.Static)!,
            bytesParam);

        var callValueLength = Expression.Call(
            valueLengthMethod,
            spanFromArray,
            bitIndexVar,
            Expression.Constant(field.BitLength));

        var valueToAssign = WrapWithDeserializeConverter(callValueLength, field);
        var assignValue = Expression.Assign(memberAccess, valueToAssign);

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, assignValue, updateBitIndex);
    }

    private static Expression CreateNestedDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess)
    {
        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call DeserializeNested<T>(bytes, bitIndex, bitLength)
        var deserializeNestedMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(DeserializeNested), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(field.MemberType);

        var callDeserialize = Expression.Call(
            deserializeNestedMethod,
            bytesParam,
            bitIndexVar,
            Expression.Constant(field.BitLength));

        var valueToAssign = WrapWithDeserializeConverter(callDeserialize, field);
        var assignValue = Expression.Assign(memberAccess, valueToAssign);

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, assignValue, updateBitIndex);
    }

    private static Expression CreatePolymorphicDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression resultVar,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        // Find the related discriminator field
        var discriminatorField = parentMetadata.Fields.FirstOrDefault(f =>
            f.Member.Name == field.RelatedMemberName);

        if (discriminatorField == null)
            throw new InvalidOperationException(
                $"Related member '{field.RelatedMemberName}' not found for polymorphic member '{field.Member.Name}'.");

        var discriminatorMemberAccess = discriminatorField.Member switch
        {
            PropertyInfo prop => (Expression)Expression.Property(resultVar, prop),
            FieldInfo fi      => Expression.Field(resultVar, fi),
            _                 => throw new InvalidOperationException()
        };

        // Convert discriminator to int
        var discriminatorAsInt = Expression.Convert(discriminatorMemberAccess, typeof(int));

        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call DeserializePolymorphic(bytes, bitIndex, bitLength, discriminatorValue, polyTypeMappings)
        var deserializePolyMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(DeserializePolymorphic), BindingFlags.NonPublic | BindingFlags.Static)!;

        var callDeserialize = Expression.Call(
            deserializePolyMethod,
            bytesParam,
            bitIndexVar,
            Expression.Constant(field.BitLength),
            discriminatorAsInt,
            Expression.Constant(field.PolyTypeMappings),
            Expression.Constant(field.MemberType));

        // Cast the result to the member type (base class type)
        var castResult = Expression.Convert(callDeserialize, field.MemberType);
        var assignValue = Expression.Assign(memberAccess, castResult);

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, assignValue, updateBitIndex);
    }

    private static Expression CreateListDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression resultVar,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        Expression countAsInt;
        if (field.FixedCount.HasValue)
        {
            countAsInt = Expression.Constant(field.FixedCount.Value);
        }
        else
        {
            // Find the related count field
            var countField = parentMetadata.Fields.FirstOrDefault(f =>
                f.Member.Name == field.RelatedMemberName);

            if (countField == null)
                throw new InvalidOperationException(
                    $"Related member '{field.RelatedMemberName}' not found for List member '{field.Member.Name}'.");

            var countMemberAccess = countField.Member switch
            {
                PropertyInfo prop => (Expression)Expression.Property(resultVar, prop),
                FieldInfo fi      => Expression.Field(resultVar, fi),
                _                 => throw new InvalidOperationException()
            };

            countAsInt = Expression.Convert(countMemberAccess, typeof(int));
        }

        var elementType = field.MemberType.GetGenericArguments()[0];
        var listType = typeof(List<>).MakeGenericType(elementType);

        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call DeserializeList<T>(bytes, bitIndex, count, elementBitLength)
        var deserializeListMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(DeserializeList), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);

        int elementBitLength;
        if (field.ListElementMetadata != null)
        {
            elementBitLength = field.ListElementMetadata.TotalBitLength;
        }
        else if (IsNumericOrEnumType(elementType))
        {
            elementBitLength = GetBitLengthForType(elementType);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot determine element bit length for List<{elementType.Name}>");
        }

        var callDeserialize = Expression.Call(
            deserializeListMethod,
            bytesParam,
            bitIndexVar,
            countAsInt,
            Expression.Constant(elementBitLength));

        var assignValue = Expression.Assign(memberAccess, callDeserialize);

        // Update bitIndex: bitIndex += count * elementBitLength
        var totalBits = Expression.Multiply(countAsInt, Expression.Constant(elementBitLength));
        var updateBitIndex = Expression.AddAssign(bitIndexVar, totalBits);

        return Expression.Block(setBitIndex, assignValue, updateBitIndex);
    }

    private static ReadOnlySpan<byte> CreateReadOnlySpan(byte[] bytes)
    {
        return new ReadOnlySpan<byte>(bytes);
    }

    /// <summary>
    /// Gets or creates a compiled nested deserializer for the specified type.
    /// Returns Func&lt;byte[], int, T&gt; where int is bitStartIndex.
    /// </summary>
    private static Func<byte[], int, T> GetOrCreateNestedDeserializer<T>() where T : new()
    {
        return (Func<byte[], int, T>)NestedDeserializerCache.GetOrAdd(
            typeof(T),
            _ => CreateNestedDeserializerDelegate<T>());
    }

    /// <summary>
    /// Creates a compiled expression tree deserializer for nested types.
    /// </summary>
    private static Func<byte[], int, T> CreateNestedDeserializerDelegate<T>() where T : new()
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));

        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitStartIndexParam = Expression.Parameter(typeof(int), "bitStartIndex");
        var resultVar = Expression.Variable(typeof(T), "result");

        var expressions = new List<Expression>
        {
            Expression.Assign(resultVar, Expression.New(typeof(T)))
        };

        foreach (var field in metadata.Fields)
        {
            var memberAccess = field.Member switch
            {
                PropertyInfo prop => Expression.Property(resultVar, prop),
                FieldInfo fi      => Expression.Field(resultVar, fi),
                _                 => throw new InvalidOperationException()
            };

            // Calculate actual bit start: bitStartIndex + field.BitStartIndex
            var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

            if (field.IsListType)
            {
                // Handle List types
                expressions.Add(CreateNestedListDeserializationExpression(
                    bytesParam, resultVar, bitStartIndexParam, field, memberAccess, metadata));
            }
            else if (field.IsPolymorphic)
            {
                // Handle polymorphic types
                expressions.Add(CreateNestedPolymorphicDeserializationExpression(
                    bytesParam, resultVar, bitStartIndexParam, field, memberAccess, metadata));
            }
            else if (field.IsNestedType)
            {
                // Call the nested deserializer recursively
                var nestedDeserializerMethod = typeof(BitSerializerMSB)
                    .GetMethod(nameof(GetOrCreateNestedDeserializer), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(field.MemberType);

                // Get the deserializer delegate
                var getDeserializer = Expression.Call(nestedDeserializerMethod);

                // Invoke the delegate: deserializer(bytes, actualBitStart)
                var invokeDeserializer = Expression.Invoke(getDeserializer, bytesParam, actualBitStart);
                var nestedValueToAssign = WrapWithDeserializeConverter(invokeDeserializer, field);
                expressions.Add(Expression.Assign(memberAccess, nestedValueToAssign));
            }
            else
            {
                // Primitive type - use BitHelperMSB.ValueLength<T>
                var valueLengthMethod = typeof(BitHelperMSB)
                    .GetMethod(nameof(BitHelperMSB.ValueLength))!
                    .MakeGenericMethod(field.MemberType);

                var spanFromArray = Expression.Call(
                    typeof(BitSerializerMSB).GetMethod(nameof(CreateReadOnlySpan), BindingFlags.NonPublic | BindingFlags.Static)!,
                    bytesParam);

                var callValueLength = Expression.Call(
                    valueLengthMethod,
                    spanFromArray,
                    actualBitStart,
                    Expression.Constant(field.BitLength));

                var valueToAssign = WrapWithDeserializeConverter(callValueLength, field);
                expressions.Add(Expression.Assign(memberAccess, valueToAssign));
            }
        }

        expressions.Add(resultVar);

        var body = Expression.Block([resultVar], expressions);
        var lambda = Expression.Lambda<Func<byte[], int, T>>(body, bytesParam, bitStartIndexParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Deserializes a nested type using pre-compiled expression tree.
    /// </summary>
    private static T DeserializeNested<T>(byte[] bytes, int bitStartIndex, int bitLength) where T : new()
    {
        var deserializer = GetOrCreateNestedDeserializer<T>();
        return deserializer(bytes, bitStartIndex);
    }

    /// <summary>
    /// Gets or creates a compiled polymorphic deserializer.
    /// The returned delegate takes (bytes, bitStartIndex, discriminatorValue) and returns the deserialized object.
    /// </summary>
    private static Func<byte[], int, int, object> GetOrCreatePolymorphicDeserializer(
        Dictionary<int, Type> polyTypeMappings,
        Type baseType)
    {
        // Create a unique key for this polymorphic configuration
        var key = baseType;

        return (Func<byte[], int, int, object>)NestedDeserializerCache.GetOrAdd(
            key,
            _ => CreatePolymorphicDeserializerDelegate(polyTypeMappings, baseType));
    }

    /// <summary>
    /// Creates a compiled expression tree deserializer for polymorphic types.
    /// Generates a switch expression based on discriminator value.
    /// </summary>
    private static Func<byte[], int, int, object> CreatePolymorphicDeserializerDelegate(
        Dictionary<int, Type> polyTypeMappings,
        Type baseType)
    {
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitStartIndexParam = Expression.Parameter(typeof(int), "bitStartIndex");
        var discriminatorParam = Expression.Parameter(typeof(int), "discriminator");

        // Build switch cases for each type mapping
        var switchCases = new List<SwitchCase>();

        foreach (var (typId, concreteType) in polyTypeMappings)
        {
            // Get or create the nested deserializer for this concrete type
            var getDeserializerMethod = typeof(BitSerializerMSB)
                .GetMethod(nameof(GetOrCreateNestedDeserializer), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(concreteType);

            // Call the deserializer and convert to object
            var getDeserializer = Expression.Call(getDeserializerMethod);
            var invokeDeserializer = Expression.Invoke(getDeserializer, bytesParam, bitStartIndexParam);
            var convertToObject = Expression.Convert(invokeDeserializer, typeof(object));

            switchCases.Add(Expression.SwitchCase(convertToObject, Expression.Constant(typId)));
        }

        // Default case: throw exception
        var throwExpr = Expression.Throw(
            Expression.New(
                typeof(InvalidOperationException).GetConstructor([typeof(string)])!,
                Expression.Call(
                    typeof(string).GetMethod(nameof(string.Format), [typeof(string), typeof(object), typeof(object)])!,
                    Expression.Constant("No polymorphic type mapping found for discriminator value '{0}' on base type '{1}'."),
                    Expression.Convert(discriminatorParam, typeof(object)),
                    Expression.Constant(baseType.Name))),
            typeof(object));

        var switchExpr = Expression.Switch(
            discriminatorParam,
            throwExpr,
            switchCases.ToArray());

        var lambda = Expression.Lambda<Func<byte[], int, int, object>>(
            switchExpr,
            bytesParam,
            bitStartIndexParam,
            discriminatorParam);

        return lambda.Compile();
    }

    /// <summary>
    /// Deserializes a polymorphic type using pre-compiled expression tree with switch.
    /// </summary>
    private static object DeserializePolymorphic(
        byte[] bytes,
        int bitStartIndex,
        int bitLength,
        int discriminatorValue,
        Dictionary<int, Type> polyTypeMappings,
        Type baseType)
    {
        var deserializer = GetOrCreatePolymorphicDeserializer(polyTypeMappings, baseType);
        return deserializer(bytes, bitStartIndex, discriminatorValue);
    }

    /// <summary>
    /// Gets or creates a compiled list deserializer for the specified element type.
    /// </summary>
    private static Func<byte[], int, int, int, List<T>> GetOrCreateListDeserializer<T>()
    {
        return (Func<byte[], int, int, int, List<T>>)ListElementDeserializerCache.GetOrAdd(
            typeof(T),
            _ => CreateListDeserializerDelegate<T>());
    }

    /// <summary>
    /// Creates a compiled expression tree deserializer for List types.
    /// </summary>
    private static Func<byte[], int, int, int, List<T>> CreateListDeserializerDelegate<T>()
    {
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitStartIndexParam = Expression.Parameter(typeof(int), "bitStartIndex");
        var countParam = Expression.Parameter(typeof(int), "count");
        var elementBitLengthParam = Expression.Parameter(typeof(int), "elementBitLength");

        var listVar = Expression.Variable(typeof(List<T>), "list");
        var indexVar = Expression.Variable(typeof(int), "i");
        var currentBitIndexVar = Expression.Variable(typeof(int), "currentBitIndex");
        var breakLabel = Expression.Label("breakLoop");

        var expressions = new List<Expression>
        {
            // list = new List<T>(count)
            Expression.Assign(listVar, Expression.New(
                typeof(List<T>).GetConstructor([typeof(int)])!,
                countParam)),
            // currentBitIndex = bitStartIndex
            Expression.Assign(currentBitIndexVar, bitStartIndexParam),
            // i = 0
            Expression.Assign(indexVar, Expression.Constant(0))
        };

        // Loop body
        Expression elementExpr;
        if (IsNumericOrEnumType(typeof(T)))
        {
            // For primitive types: BitHelperMSB.ValueLength<T>(bytes, currentBitIndex, elementBitLength)
            var valueLengthMethod = typeof(BitHelperMSB)
                .GetMethod(nameof(BitHelperMSB.ValueLength))!
                .MakeGenericMethod(typeof(T));

            var spanFromArray = Expression.Call(
                typeof(BitSerializerMSB).GetMethod(nameof(CreateReadOnlySpan), BindingFlags.NonPublic | BindingFlags.Static)!,
                bytesParam);

            elementExpr = Expression.Call(
                valueLengthMethod,
                spanFromArray,
                currentBitIndexVar,
                elementBitLengthParam);
        }
        else
        {
            // For nested types: use pre-compiled deserializer
            var getDeserializerMethod = typeof(BitSerializerMSB)
                .GetMethod(nameof(GetOrCreateNestedDeserializer), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(T));

            var getDeserializer = Expression.Call(getDeserializerMethod);
            elementExpr = Expression.Invoke(getDeserializer, bytesParam, currentBitIndexVar);
        }

        // list.Add(element)
        var addMethod = typeof(List<T>).GetMethod(nameof(List<T>.Add))!;
        var addElement = Expression.Call(listVar, addMethod, elementExpr);

        // currentBitIndex += elementBitLength
        var incrementBitIndex = Expression.AddAssign(currentBitIndexVar, elementBitLengthParam);

        // i++
        var incrementIndex = Expression.PostIncrementAssign(indexVar);

        // Loop: while (i < count) { ... }
        var loopBody = Expression.Block(
            addElement,
            incrementBitIndex,
            incrementIndex);

        var loop = Expression.Loop(
            Expression.IfThenElse(
                Expression.LessThan(indexVar, countParam),
                loopBody,
                Expression.Break(breakLabel)),
            breakLabel);

        expressions.Add(loop);
        expressions.Add(listVar);

        var body = Expression.Block([listVar, indexVar, currentBitIndexVar], expressions);
        var lambda = Expression.Lambda<Func<byte[], int, int, int, List<T>>>(
            body,
            bytesParam,
            bitStartIndexParam,
            countParam,
            elementBitLengthParam);

        return lambda.Compile();
    }

    /// <summary>
    /// Deserializes a list using pre-compiled expression tree.
    /// </summary>
    private static List<T> DeserializeList<T>(byte[] bytes, int bitStartIndex, int count, int elementBitLength)
    {
        var deserializer = GetOrCreateListDeserializer<T>();
        return deserializer(bytes, bitStartIndex, count, elementBitLength);
    }

    /// <summary>
    /// Creates a polymorphic deserialization expression for nested types.
    /// Similar to CreatePolymorphicDeserializationExpression but uses bitStartIndexParam.
    /// </summary>
    private static Expression CreateNestedPolymorphicDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression resultVar,
        ParameterExpression bitStartIndexParam,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        // Find the related discriminator field
        var discriminatorField = parentMetadata.Fields.FirstOrDefault(f =>
            f.Member.Name == field.RelatedMemberName);

        if (discriminatorField == null)
            throw new InvalidOperationException(
                $"Related member '{field.RelatedMemberName}' not found for polymorphic member '{field.Member.Name}'.");

        var discriminatorMemberAccess = discriminatorField.Member switch
        {
            PropertyInfo prop => (Expression)Expression.Property(resultVar, prop),
            FieldInfo fi      => Expression.Field(resultVar, fi),
            _                 => throw new InvalidOperationException()
        };

        // Convert discriminator to int
        var discriminatorAsInt = Expression.Convert(discriminatorMemberAccess, typeof(int));

        // Calculate actual bit start: bitStartIndexParam + field.BitStartIndex
        var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

        // Call DeserializePolymorphic(bytes, actualBitStart, bitLength, discriminatorValue, polyTypeMappings)
        var deserializePolyMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(DeserializePolymorphic), BindingFlags.NonPublic | BindingFlags.Static)!;

        var callDeserialize = Expression.Call(
            deserializePolyMethod,
            bytesParam,
            actualBitStart,
            Expression.Constant(field.BitLength),
            discriminatorAsInt,
            Expression.Constant(field.PolyTypeMappings),
            Expression.Constant(field.MemberType));

        // Cast the result to the member type (base class type)
        var castResult = Expression.Convert(callDeserialize, field.MemberType);
        var assignValue = Expression.Assign(memberAccess, castResult);

        return assignValue;
    }

    /// <summary>
    /// Creates a list deserialization expression for nested types.
    /// Similar to CreateListDeserializationExpression but uses bitStartIndexParam.
    /// </summary>
    private static Expression CreateNestedListDeserializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression resultVar,
        ParameterExpression bitStartIndexParam,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        Expression countAsInt;
        if (field.FixedCount.HasValue)
        {
            countAsInt = Expression.Constant(field.FixedCount.Value);
        }
        else
        {
            // Find the related count field
            var countField = parentMetadata.Fields.FirstOrDefault(f =>
                f.Member.Name == field.RelatedMemberName);

            if (countField == null)
                throw new InvalidOperationException(
                    $"Related member '{field.RelatedMemberName}' not found for List member '{field.Member.Name}'.");

            var countMemberAccess = countField.Member switch
            {
                PropertyInfo prop => (Expression)Expression.Property(resultVar, prop),
                FieldInfo fi      => Expression.Field(resultVar, fi),
                _                 => throw new InvalidOperationException()
            };

            countAsInt = Expression.Convert(countMemberAccess, typeof(int));
        }

        var elementType = field.MemberType.GetGenericArguments()[0];

        // Calculate actual bit start: bitStartIndexParam + field.BitStartIndex
        var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

        // Call DeserializeList<T>(bytes, actualBitStart, count, elementBitLength)
        var deserializeListMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(DeserializeList), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);

        int elementBitLength;
        if (field.ListElementMetadata != null)
        {
            elementBitLength = field.ListElementMetadata.TotalBitLength;
        }
        else if (IsNumericOrEnumType(elementType))
        {
            elementBitLength = GetBitLengthForType(elementType);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot determine element bit length for List<{elementType.Name}>");
        }

        var callDeserialize = Expression.Call(
            deserializeListMethod,
            bytesParam,
            actualBitStart,
            countAsInt,
            Expression.Constant(elementBitLength));

        var assignValue = Expression.Assign(memberAccess, callDeserialize);

        return assignValue;
    }

    private class TypeMetadata
    {
        public Type Type { get; set; } = null!;
        public List<BitFieldInfo> Fields { get; set; } = new();
        public int TotalBitLength { get; set; }
    }

    private class BitFieldInfo
    {
        public MemberInfo Member { get; set; } = null!;
        public Type MemberType { get; set; } = null!;
        public int BitStartIndex { get; set; }
        public int BitLength { get; set; }
        public bool IsListType { get; set; }
        public int? FixedCount { get; set; }
        public string? RelatedMemberName { get; set; }
        public bool IsNestedType { get; set; }
        public TypeMetadata? NestedMetadata { get; set; }
        public TypeMetadata? ListElementMetadata { get; set; }

        // Value converter support
        public Type? ValueConverterType { get; set; }

        // Polymorphic type support
        public bool IsPolymorphic { get; set; }
        public Dictionary<int, Type>? PolyTypeMappings { get; set; }
        public Dictionary<int, TypeMetadata>? PolyTypeMetadatas { get; set; }
    }

    #region Serialization Methods

    public static byte[] Serialize<T>(T obj)
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));
        var serializer = GetOrCreateSerializer<T>(metadata);

        // Calculate required byte array size (including dynamic fields like Lists)
        var totalBits = CalculateTotalBits(obj, metadata);
        var byteCount = (totalBits + 7) / 8;
        var bytes = new byte[byteCount];

        serializer(obj, bytes);
        return bytes;
    }

    private static int CalculateTotalBits<T>(T obj, TypeMetadata metadata)
    {
        var totalBits = 0;

        foreach (var field in metadata.Fields)
        {
            if (field.IsListType)
            {
                int count;
                if (field.FixedCount.HasValue)
                {
                    count = field.FixedCount.Value;
                }
                else
                {
                    var countField = metadata.Fields.FirstOrDefault(f => f.Member.Name == field.RelatedMemberName);
                    if (countField == null) continue;
                    var countValue = countField.Member switch
                    {
                        PropertyInfo prop => prop.GetValue(obj),
                        FieldInfo fi => fi.GetValue(obj),
                        _ => null
                    };
                    count = Convert.ToInt32(countValue);
                }
                {
                    var elementType = field.MemberType.GetGenericArguments()[0];
                    int elementBitLength;

                    if (field.ListElementMetadata != null)
                    {
                        elementBitLength = field.ListElementMetadata.TotalBitLength;
                    }
                    else if (IsNumericOrEnumType(elementType))
                    {
                        elementBitLength = GetBitLengthForType(elementType);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"Cannot determine element bit length for List<{elementType.Name}>");
                    }

                    totalBits = field.BitStartIndex + count * elementBitLength;
                }
            }
            else if (field.IsPolymorphic)
            {
                // For polymorphic types, use the maximum bit length
                totalBits = Math.Max(totalBits, field.BitStartIndex + field.BitLength);
            }
            else
            {
                totalBits = Math.Max(totalBits, field.BitStartIndex + field.BitLength);
            }
        }

        return totalBits;
    }

    public static void Serialize<T>(T obj, Span<byte> bytes)
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));

        // Calculate required byte array size
        var byteCount = (metadata.TotalBitLength + 7) / 8;
        if (bytes.Length < byteCount)
            throw new ArgumentException($"Span is too small. Required: {byteCount}, Provided: {bytes.Length}");

        // Create a temporary array and copy it to the span
        var tempBytes = new byte[byteCount];
        var serializer = GetOrCreateSerializer<T>(metadata);
        serializer(obj, tempBytes);

        // Copy the result to the provided span
        tempBytes.AsSpan().CopyTo(bytes);
    }

    private static Action<T, byte[]> GetOrCreateSerializer<T>(TypeMetadata metadata)
    {
        return (Action<T, byte[]>)SerializerCache.GetOrAdd(typeof(T), _ => CreateSerializer<T>(metadata));
    }

    private static Action<T, byte[]> CreateSerializer<T>(TypeMetadata metadata)
    {
        var objParam = Expression.Parameter(typeof(T), "obj");
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitIndexVar = Expression.Variable(typeof(int), "bitIndex");

        var expressions = new List<Expression>
        {
            Expression.Assign(bitIndexVar, Expression.Constant(0))
        };

        foreach (var field in metadata.Fields)
        {
            var memberAccess = field.Member switch
            {
                PropertyInfo prop => Expression.Property(objParam, prop),
                FieldInfo fi      => Expression.Field(objParam, fi),
                _                 => throw new InvalidOperationException()
            };

            if (field.IsListType)
            {
                expressions.Add(CreateListSerializationExpression(
                    bytesParam, objParam, bitIndexVar, field, memberAccess, metadata));
            }
            else if (field.IsPolymorphic)
            {
                expressions.Add(CreatePolymorphicSerializationExpression(
                    bytesParam, objParam, bitIndexVar, field, memberAccess, metadata));
            }
            else if (field.IsNestedType)
            {
                expressions.Add(CreateNestedSerializationExpression(
                    bytesParam, bitIndexVar, field, memberAccess));
            }
            else
            {
                expressions.Add(CreatePrimitiveSerializationExpression(
                    bytesParam, bitIndexVar, field, memberAccess));
            }
        }

        var body = Expression.Block([bitIndexVar], expressions);
        var lambda = Expression.Lambda<Action<T, byte[]>>(body, objParam, bytesParam);
        return lambda.Compile();
    }

    private static Expression CreatePrimitiveSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess)
    {
        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call BitHelperMSB.SetValueLength<T>(bytes, bitIndex, bitLength, value)
        var setValueLengthMethod = typeof(BitHelperMSB)
            .GetMethod(nameof(BitHelperMSB.SetValueLength))!
            .MakeGenericMethod(field.MemberType);

        var spanFromArray = Expression.Call(
            typeof(BitSerializerMSB).GetMethod(nameof(CreateSpan), BindingFlags.NonPublic | BindingFlags.Static)!,
            bytesParam);

        var valueToSerialize = WrapWithSerializeConverter(memberAccess, field);
        var callSetValueLength = Expression.Call(
            setValueLengthMethod,
            spanFromArray,
            bitIndexVar,
            Expression.Constant(field.BitLength),
            valueToSerialize);

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, callSetValueLength, updateBitIndex);
    }

    private static Expression CreateNestedSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess)
    {
        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call SerializeNested<T>(obj, bytes, bitIndex, bitLength)
        var serializeNestedMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(SerializeNested), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(field.MemberType);

        var valueToSerialize = WrapWithSerializeConverter(memberAccess, field);
        var callSerialize = Expression.Call(
            serializeNestedMethod,
            valueToSerialize,
            bytesParam,
            bitIndexVar,
            Expression.Constant(field.BitLength));

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, callSerialize, updateBitIndex);
    }

    private static Expression CreateListSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression objParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        Expression countAsInt;
        if (field.FixedCount.HasValue)
        {
            countAsInt = Expression.Constant(field.FixedCount.Value);
        }
        else
        {
            // Find the related count field
            var countField = parentMetadata.Fields.FirstOrDefault(f =>
                f.Member.Name == field.RelatedMemberName);

            if (countField == null)
                throw new InvalidOperationException(
                    $"Related member '{field.RelatedMemberName}' not found for List member '{field.Member.Name}'.");

            var countMemberAccess = countField.Member switch
            {
                PropertyInfo prop => (Expression)Expression.Property(objParam, prop),
                FieldInfo fi      => Expression.Field(objParam, fi),
                _                 => throw new InvalidOperationException()
            };

            countAsInt = Expression.Convert(countMemberAccess, typeof(int));
        }

        var elementType = field.MemberType.GetGenericArguments()[0];

        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call SerializeList<T>(list, bytes, bitIndex, count, elementBitLength)
        var serializeListMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(SerializeList), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);

        int elementBitLength;
        if (field.ListElementMetadata != null)
        {
            elementBitLength = field.ListElementMetadata.TotalBitLength;
        }
        else if (IsNumericOrEnumType(elementType))
        {
            elementBitLength = GetBitLengthForType(elementType);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot determine element bit length for List<{elementType.Name}>");
        }

        var callSerialize = Expression.Call(
            serializeListMethod,
            memberAccess,
            bytesParam,
            bitIndexVar,
            countAsInt,
            Expression.Constant(elementBitLength));

        // Update bitIndex: bitIndex += count * elementBitLength
        var totalBits = Expression.Multiply(countAsInt, Expression.Constant(elementBitLength));
        var updateBitIndex = Expression.AddAssign(bitIndexVar, totalBits);

        return Expression.Block(setBitIndex, callSerialize, updateBitIndex);
    }

    private static Expression CreatePolymorphicSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression objParam,
        ParameterExpression bitIndexVar,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        // Set bitIndex to field's start index
        var setBitIndex = Expression.Assign(bitIndexVar, Expression.Constant(field.BitStartIndex));

        // Call SerializePolymorphic(obj, bytes, bitIndex, bitLength, polyTypeMappings)
        var serializePolyMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(SerializePolymorphic), BindingFlags.NonPublic | BindingFlags.Static)!;

        var callSerialize = Expression.Call(
            serializePolyMethod,
            memberAccess,
            bytesParam,
            bitIndexVar,
            Expression.Constant(field.BitLength),
            Expression.Constant(field.PolyTypeMappings),
            Expression.Constant(field.MemberType));

        // Update bitIndex
        var updateBitIndex = Expression.AddAssign(bitIndexVar, Expression.Constant(field.BitLength));

        return Expression.Block(setBitIndex, callSerialize, updateBitIndex);
    }

    private static Span<byte> CreateSpan(byte[] bytes)
    {
        return new Span<byte>(bytes);
    }

    /// <summary>
    /// Gets or creates a compiled nested serializer for the specified type.
    /// Returns Action&lt;T, byte[], int&gt; where int is bitStartIndex.
    /// </summary>
    private static Action<T, byte[], int> GetOrCreateNestedSerializer<T>()
    {
        return (Action<T, byte[], int>)NestedSerializerCache.GetOrAdd(
            typeof(T),
            _ => CreateNestedSerializerDelegate<T>());
    }

    /// <summary>
    /// Creates a compiled expression tree serializer for nested types.
    /// </summary>
    private static Action<T, byte[], int> CreateNestedSerializerDelegate<T>()
    {
        var metadata = GetOrCreateTypeMetadata(typeof(T));

        var objParam = Expression.Parameter(typeof(T), "obj");
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitStartIndexParam = Expression.Parameter(typeof(int), "bitStartIndex");

        var expressions = new List<Expression>();

        foreach (var field in metadata.Fields)
        {
            var memberAccess = field.Member switch
            {
                PropertyInfo prop => Expression.Property(objParam, prop),
                FieldInfo fi      => Expression.Field(objParam, fi),
                _                 => throw new InvalidOperationException()
            };

            // Calculate actual bit start: bitStartIndex + field.BitStartIndex
            var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

            if (field.IsListType)
            {
                // Handle List types
                expressions.Add(CreateNestedListSerializationExpression(
                    bytesParam, objParam, bitStartIndexParam, field, memberAccess, metadata));
            }
            else if (field.IsPolymorphic)
            {
                // Handle polymorphic types
                expressions.Add(CreateNestedPolymorphicSerializationExpression(
                    bytesParam, objParam, bitStartIndexParam, field, memberAccess, metadata));
            }
            else if (field.IsNestedType)
            {
                // Call the nested serializer recursively
                var nestedSerializerMethod = typeof(BitSerializerMSB)
                    .GetMethod(nameof(GetOrCreateNestedSerializer), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(field.MemberType);

                // Get the serializer delegate
                var getSerializer = Expression.Call(nestedSerializerMethod);

                // Invoke the delegate: serializer(convertedObj, bytes, actualBitStart)
                var serNestedValue = WrapWithSerializeConverter(memberAccess, field);
                var invokeSerializer = Expression.Invoke(getSerializer, serNestedValue, bytesParam, actualBitStart);
                expressions.Add(invokeSerializer);
            }
            else
            {
                // Primitive type - use BitHelperMSB.SetValueLength<T>
                var setValueLengthMethod = typeof(BitHelperMSB)
                    .GetMethod(nameof(BitHelperMSB.SetValueLength))!
                    .MakeGenericMethod(field.MemberType);

                var spanFromArray = Expression.Call(
                    typeof(BitSerializerMSB).GetMethod(nameof(CreateSpan), BindingFlags.NonPublic | BindingFlags.Static)!,
                    bytesParam);

                var serValueToWrite = WrapWithSerializeConverter(memberAccess, field);
                var callSetValueLength = Expression.Call(
                    setValueLengthMethod,
                    spanFromArray,
                    actualBitStart,
                    Expression.Constant(field.BitLength),
                    serValueToWrite);

                expressions.Add(callSetValueLength);
            }
        }

        var body = Expression.Block(expressions);
        var lambda = Expression.Lambda<Action<T, byte[], int>>(body, objParam, bytesParam, bitStartIndexParam);
        return lambda.Compile();
    }

    /// <summary>
    /// Serializes a nested type using pre-compiled expression tree.
    /// </summary>
    private static void SerializeNested<T>(T obj, byte[] bytes, int bitStartIndex, int bitLength)
    {
        var serializer = GetOrCreateNestedSerializer<T>();
        serializer(obj, bytes, bitStartIndex);
    }

    /// <summary>
    /// Gets or creates a compiled list serializer for the specified element type.
    /// </summary>
    private static Action<List<T>, byte[], int, int, int> GetOrCreateListSerializer<T>()
    {
        return (Action<List<T>, byte[], int, int, int>)ListElementSerializerCache.GetOrAdd(
            typeof(T),
            _ => CreateListSerializerDelegate<T>());
    }

    /// <summary>
    /// Creates a compiled expression tree serializer for List types.
    /// </summary>
    private static Action<List<T>, byte[], int, int, int> CreateListSerializerDelegate<T>()
    {
        var listParam = Expression.Parameter(typeof(List<T>), "list");
        var bytesParam = Expression.Parameter(typeof(byte[]), "bytes");
        var bitStartIndexParam = Expression.Parameter(typeof(int), "bitStartIndex");
        var countParam = Expression.Parameter(typeof(int), "count");
        var elementBitLengthParam = Expression.Parameter(typeof(int), "elementBitLength");

        var indexVar = Expression.Variable(typeof(int), "i");
        var currentBitIndexVar = Expression.Variable(typeof(int), "currentBitIndex");
        var elementVar = Expression.Variable(typeof(T), "element");
        var breakLabel = Expression.Label("breakLoop");

        var expressions = new List<Expression>
        {
            // currentBitIndex = bitStartIndex
            Expression.Assign(currentBitIndexVar, bitStartIndexParam),
            // i = 0
            Expression.Assign(indexVar, Expression.Constant(0))
        };

        // Get element: element = list[i]
        var getItem = Expression.Property(listParam, "Item", indexVar);
        var assignElement = Expression.Assign(elementVar, getItem);

        // Serialize element
        Expression serializeExpr;
        if (IsNumericOrEnumType(typeof(T)))
        {
            // For primitive types: BitHelperMSB.SetValueLength<T>(bytes, currentBitIndex, elementBitLength, element)
            var setValueLengthMethod = typeof(BitHelperMSB)
                .GetMethod(nameof(BitHelperMSB.SetValueLength))!
                .MakeGenericMethod(typeof(T));

            var spanFromArray = Expression.Call(
                typeof(BitSerializerMSB).GetMethod(nameof(CreateSpan), BindingFlags.NonPublic | BindingFlags.Static)!,
                bytesParam);

            serializeExpr = Expression.Call(
                setValueLengthMethod,
                spanFromArray,
                currentBitIndexVar,
                elementBitLengthParam,
                elementVar);
        }
        else
        {
            // For nested types: use pre-compiled serializer
            var getSerializerMethod = typeof(BitSerializerMSB)
                .GetMethod(nameof(GetOrCreateNestedSerializer), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(typeof(T));

            var getSerializer = Expression.Call(getSerializerMethod);
            serializeExpr = Expression.Invoke(getSerializer, elementVar, bytesParam, currentBitIndexVar);
        }

        // currentBitIndex += elementBitLength
        var incrementBitIndex = Expression.AddAssign(currentBitIndexVar, elementBitLengthParam);

        // i++
        var incrementIndex = Expression.PostIncrementAssign(indexVar);

        // Loop: while (i < count) { ... }
        var loopBody = Expression.Block(
            assignElement,
            serializeExpr,
            incrementBitIndex,
            incrementIndex);

        var loop = Expression.Loop(
            Expression.IfThenElse(
                Expression.LessThan(indexVar, countParam),
                loopBody,
                Expression.Break(breakLabel)),
            breakLabel);

        expressions.Add(loop);

        var body = Expression.Block([indexVar, currentBitIndexVar, elementVar], expressions);
        var lambda = Expression.Lambda<Action<List<T>, byte[], int, int, int>>(
            body,
            listParam,
            bytesParam,
            bitStartIndexParam,
            countParam,
            elementBitLengthParam);

        return lambda.Compile();
    }

    /// <summary>
    /// Serializes a list using pre-compiled expression tree.
    /// </summary>
    private static void SerializeList<T>(List<T> list, byte[] bytes, int bitStartIndex, int count, int elementBitLength)
    {
        var serializer = GetOrCreateListSerializer<T>();
        serializer(list, bytes, bitStartIndex, count, elementBitLength);
    }

    /// <summary>
    /// Serializes a polymorphic type.
    /// </summary>
    private static void SerializePolymorphic(
        object obj,
        byte[] bytes,
        int bitStartIndex,
        int bitLength,
        Dictionary<int, Type> polyTypeMappings,
        Type baseType)
    {
        if (obj == null)
            throw new ArgumentNullException(nameof(obj));

        // Find the type mapping for this object's actual type
        var actualType = obj.GetType();
        var typMapping = polyTypeMappings.FirstOrDefault(kvp => kvp.Value == actualType);

        if (typMapping.Value == null)
            throw new InvalidOperationException(
                $"No polymorphic type mapping found for type '{actualType.Name}' on base type '{baseType.Name}'.");

        // Get or create the nested serializer for this type
        var serializerMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(GetOrCreateNestedSerializer), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(actualType);

        var serializer = serializerMethod.Invoke(null, null);

        // Invoke the serializer
        var invokeMethod = serializer!.GetType().GetMethod("Invoke")!;
        invokeMethod.Invoke(serializer, new[] { obj, bytes, bitStartIndex });
    }

    /// <summary>
    /// Creates a polymorphic serialization expression for nested types.
    /// </summary>
    private static Expression CreateNestedPolymorphicSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression objParam,
        ParameterExpression bitStartIndexParam,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        // Calculate actual bit start: bitStartIndexParam + field.BitStartIndex
        var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

        // Call SerializePolymorphic(obj, bytes, actualBitStart, bitLength, polyTypeMappings)
        var serializePolyMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(SerializePolymorphic), BindingFlags.NonPublic | BindingFlags.Static)!;

        var convertToObject = Expression.Convert(memberAccess, typeof(object));
        var callSerialize = Expression.Call(
            serializePolyMethod,
            convertToObject,
            bytesParam,
            actualBitStart,
            Expression.Constant(field.BitLength),
            Expression.Constant(field.PolyTypeMappings),
            Expression.Constant(field.MemberType));

        return callSerialize;
    }

    /// <summary>
    /// Creates a list serialization expression for nested types.
    /// </summary>
    private static Expression CreateNestedListSerializationExpression(
        ParameterExpression bytesParam,
        ParameterExpression objParam,
        ParameterExpression bitStartIndexParam,
        BitFieldInfo field,
        MemberExpression memberAccess,
        TypeMetadata parentMetadata)
    {
        Expression countAsInt;
        if (field.FixedCount.HasValue)
        {
            countAsInt = Expression.Constant(field.FixedCount.Value);
        }
        else
        {
            // Find the related count field
            var countField = parentMetadata.Fields.FirstOrDefault(f =>
                f.Member.Name == field.RelatedMemberName);

            if (countField == null)
                throw new InvalidOperationException(
                    $"Related member '{field.RelatedMemberName}' not found for List member '{field.Member.Name}'.");

            var countMemberAccess = countField.Member switch
            {
                PropertyInfo prop => (Expression)Expression.Property(objParam, prop),
                FieldInfo fi      => Expression.Field(objParam, fi),
                _                 => throw new InvalidOperationException()
            };

            countAsInt = Expression.Convert(countMemberAccess, typeof(int));
        }

        var elementType = field.MemberType.GetGenericArguments()[0];

        // Calculate actual bit start: bitStartIndexParam + field.BitStartIndex
        var actualBitStart = Expression.Add(bitStartIndexParam, Expression.Constant(field.BitStartIndex));

        // Call SerializeList<T>(list, bytes, actualBitStart, count, elementBitLength)
        var serializeListMethod = typeof(BitSerializerMSB)
            .GetMethod(nameof(SerializeList), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(elementType);

        int elementBitLength;
        if (field.ListElementMetadata != null)
        {
            elementBitLength = field.ListElementMetadata.TotalBitLength;
        }
        else if (IsNumericOrEnumType(elementType))
        {
            elementBitLength = GetBitLengthForType(elementType);
        }
        else
        {
            throw new InvalidOperationException(
                $"Cannot determine element bit length for List<{elementType.Name}>");
        }

        var callSerialize = Expression.Call(
            serializeListMethod,
            memberAccess,
            bytesParam,
            actualBitStart,
            countAsInt,
            Expression.Constant(elementBitLength));

        return callSerialize;
    }

    #endregion
}
