namespace GrueGPT.Core.Stories;

/// <summary>Reads and updates the Version 3 object tree and property tables.</summary>
public sealed class Z3ObjectTable
{
    public const int MaximumObjectNumber = byte.MaxValue;
    public const int MaximumPropertyNumber = 31;
    public const int AttributeCount = 32;

    private const int PropertyDefaultsCount = MaximumPropertyNumber;
    private const int PropertyDefaultsSize = PropertyDefaultsCount * sizeof(ushort);
    private const int ObjectEntrySize = 9;
    private const int AttributeBytes = 4;

    private readonly Z3Memory _memory;
    private readonly int _objectEntriesAddress;
    private readonly int _propertyDefaultsAddress;

    public Z3ObjectTable(Z3Memory memory, ushort objectTableAddress)
    {
        ArgumentNullException.ThrowIfNull(memory);
        _memory = memory;
        _propertyDefaultsAddress = objectTableAddress;
        _objectEntriesAddress = objectTableAddress + PropertyDefaultsSize;

        if (_objectEntriesAddress > memory.StaticMemoryBase)
        {
            throw new ArgumentOutOfRangeException(
                nameof(objectTableAddress),
                "The property defaults table must fit in dynamic memory.");
        }
    }

    /// <summary>Returns whether an object's numbered attribute is set.</summary>
    public bool TestAttribute(byte objectNumber, byte attributeNumber)
    {
        ValidateAttributeNumber(attributeNumber);
        (int attributeByte, byte mask) = GetAttributeLocation(objectNumber, attributeNumber);
        return (_memory.ReadByte(attributeByte) & mask) != 0;
    }

    /// <summary>Sets an object's numbered attribute.</summary>
    public void SetAttribute(byte objectNumber, byte attributeNumber) =>
        ChangeAttribute(objectNumber, attributeNumber, set: true);

    /// <summary>Clears an object's numbered attribute.</summary>
    public void ClearAttribute(byte objectNumber, byte attributeNumber) =>
        ChangeAttribute(objectNumber, attributeNumber, set: false);

    public byte GetParent(byte objectNumber) => _memory.ReadByte(GetObjectEntryAddress(objectNumber) + 4);

    public byte GetSibling(byte objectNumber) => _memory.ReadByte(GetObjectEntryAddress(objectNumber) + 5);

    public byte GetChild(byte objectNumber) => _memory.ReadByte(GetObjectEntryAddress(objectNumber) + 6);

    /// <summary>Decodes an object's short name from the start of its property table.</summary>
    public string GetShortName(byte objectNumber, Z3TextDecoder textDecoder)
    {
        ArgumentNullException.ThrowIfNull(textDecoder);
        int entryAddress = GetObjectEntryAddress(objectNumber);
        int propertyTableAddress = _memory.ReadWord(entryAddress + 7);
        ValidateDynamicAddress(propertyTableAddress, "object property table");
        byte nameWordCount = _memory.ReadByte(propertyTableAddress);
        if (nameWordCount == 0)
        {
            return string.Empty;
        }

        int nameEndAddress = propertyTableAddress + 1 + (nameWordCount * sizeof(ushort));
        if (nameEndAddress > _memory.StaticMemoryBase)
        {
            throw new InvalidOperationException($"Object {objectNumber}'s short name extends beyond dynamic memory.");
        }

        return textDecoder.DecodeAt(propertyTableAddress + 1);
    }

    /// <summary>Reads a property's default value from the story's 31-word defaults table.</summary>
    public ushort GetPropertyDefault(byte propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        return _memory.ReadWord(_propertyDefaultsAddress + ((propertyNumber - 1) * sizeof(ushort)));
    }

    /// <summary>Gets the first byte of a property's data, or zero when the object lacks it.</summary>
    public ushort GetPropertyAddress(byte objectNumber, byte propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        PropertyDescriptor? descriptor = FindProperty(objectNumber, propertyNumber);
        return descriptor is null ? (ushort)0 : checked((ushort)descriptor.DataAddress);
    }

    /// <summary>Gets a property value, using the defaults table when the property is absent.</summary>
    public ushort GetProperty(byte objectNumber, byte propertyNumber)
    {
        ValidatePropertyNumber(propertyNumber);
        PropertyDescriptor? descriptor = FindProperty(objectNumber, propertyNumber);
        if (descriptor is null)
        {
            return GetPropertyDefault(propertyNumber);
        }

        return descriptor.Length switch
        {
            1 => _memory.ReadByte(descriptor.DataAddress),
            2 => _memory.ReadWord(descriptor.DataAddress),
            _ => throw new InvalidOperationException(
                $"Property {propertyNumber} on object {objectNumber} has length {descriptor.Length}; get_prop supports only lengths 1 and 2.")
        };
    }

    /// <summary>Updates a declared one- or two-byte property.</summary>
    public void PutProperty(byte objectNumber, byte propertyNumber, ushort value)
    {
        ValidatePropertyNumber(propertyNumber);
        PropertyDescriptor descriptor = FindProperty(objectNumber, propertyNumber)
            ?? throw new InvalidOperationException($"Object {objectNumber} does not provide property {propertyNumber}.");

        switch (descriptor.Length)
        {
            case 1:
                _memory.WriteByte(descriptor.DataAddress, (byte)value);
                break;
            case 2:
                _memory.WriteWord(descriptor.DataAddress, value);
                break;
            default:
                throw new InvalidOperationException(
                    $"Property {propertyNumber} on object {objectNumber} has length {descriptor.Length}; put_prop supports only lengths 1 and 2.");
        }
    }

    /// <summary>Gets the next property number in the object's descending property list.</summary>
    public byte GetNextProperty(byte objectNumber, byte propertyNumber)
    {
        if (propertyNumber > MaximumPropertyNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyNumber), "Version 3 property numbers range from 0 through 31.");
        }

        IReadOnlyList<PropertyDescriptor> properties = ReadProperties(objectNumber);
        if (propertyNumber == 0)
        {
            return properties.Count == 0 ? (byte)0 : properties[0].Number;
        }

        for (int index = 0; index < properties.Count; index++)
        {
            if (properties[index].Number == propertyNumber)
            {
                return index + 1 < properties.Count ? properties[index + 1].Number : (byte)0;
            }
        }

        throw new InvalidOperationException($"Object {objectNumber} does not provide property {propertyNumber}.");
    }

    /// <summary>Returns the byte length for a property-data address; address zero has length zero.</summary>
    public byte GetPropertyLength(ushort propertyDataAddress)
    {
        if (propertyDataAddress == 0)
        {
            return 0;
        }

        int headerAddress = propertyDataAddress - 1;
        ValidateDynamicAddress(headerAddress, "property header");
        byte header = _memory.ReadByte(headerAddress);
        if (header == 0)
        {
            throw new InvalidOperationException("Address does not point to the data of a Version 3 property.");
        }

        return (byte)((header >> 5) + 1);
    }

    /// <summary>Detaches an object from its parent while leaving its children in place.</summary>
    public void RemoveObject(byte objectNumber)
    {
        int objectAddress = GetObjectEntryAddress(objectNumber);
        byte parent = _memory.ReadByte(objectAddress + 4);
        if (parent == 0)
        {
            _memory.WriteByte(objectAddress + 4, 0);
            _memory.WriteByte(objectAddress + 5, 0);
            return;
        }

        int parentAddress = GetObjectEntryAddress(parent);
        byte firstChild = _memory.ReadByte(parentAddress + 6);
        if (firstChild == objectNumber)
        {
            _memory.WriteByte(parentAddress + 6, _memory.ReadByte(objectAddress + 5));
        }
        else
        {
            byte previous = firstChild;
            int traversed = 0;
            while (previous != 0 && traversed++ < MaximumObjectNumber)
            {
                int previousAddress = GetObjectEntryAddress(previous);
                byte next = _memory.ReadByte(previousAddress + 5);
                if (next == objectNumber)
                {
                    _memory.WriteByte(previousAddress + 5, _memory.ReadByte(objectAddress + 5));
                    break;
                }

                previous = next;
            }

            if (previous == 0 || traversed > MaximumObjectNumber)
            {
                throw new InvalidOperationException($"Object {objectNumber} is not present in its parent {parent}'s child list.");
            }
        }

        _memory.WriteByte(objectAddress + 4, 0);
        _memory.WriteByte(objectAddress + 5, 0);
    }

    /// <summary>Moves an object to become the first child of the destination object.</summary>
    public void InsertObject(byte objectNumber, byte destinationObjectNumber)
    {
        _ = GetObjectEntryAddress(destinationObjectNumber);
        if (objectNumber == destinationObjectNumber)
        {
            throw new InvalidOperationException("An object cannot be inserted into itself.");
        }

        RemoveObject(objectNumber);
        int objectAddress = GetObjectEntryAddress(objectNumber);
        int destinationAddress = GetObjectEntryAddress(destinationObjectNumber);
        _memory.WriteByte(objectAddress + 4, destinationObjectNumber);
        _memory.WriteByte(objectAddress + 5, _memory.ReadByte(destinationAddress + 6));
        _memory.WriteByte(destinationAddress + 6, objectNumber);
    }

    private void ChangeAttribute(byte objectNumber, byte attributeNumber, bool set)
    {
        (int attributeByte, byte mask) = GetAttributeLocation(objectNumber, attributeNumber);
        byte current = _memory.ReadByte(attributeByte);
        _memory.WriteByte(attributeByte, set ? (byte)(current | mask) : (byte)(current & ~mask));
    }

    private (int AttributeAddress, byte Mask) GetAttributeLocation(byte objectNumber, byte attributeNumber)
    {
        ValidateAttributeNumber(attributeNumber);
        int entryAddress = GetObjectEntryAddress(objectNumber);
        int attributeAddress = entryAddress + (attributeNumber / 8);
        byte mask = (byte)(0x80 >> (attributeNumber % 8));
        return (attributeAddress, mask);
    }

    private int GetObjectEntryAddress(byte objectNumber)
    {
        if (objectNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(objectNumber), "Object number zero means nothing and is not an object.");
        }

        int address = _objectEntriesAddress + ((objectNumber - 1) * ObjectEntrySize);
        if (address < _objectEntriesAddress || address + ObjectEntrySize > _memory.StaticMemoryBase)
        {
            throw new ArgumentOutOfRangeException(nameof(objectNumber), $"Object {objectNumber} does not have a complete entry in dynamic memory.");
        }

        return address;
    }

    private PropertyDescriptor? FindProperty(byte objectNumber, byte propertyNumber)
    {
        foreach (PropertyDescriptor property in ReadProperties(objectNumber))
        {
            if (property.Number == propertyNumber)
            {
                return property;
            }

            if (property.Number < propertyNumber)
            {
                break;
            }
        }

        return null;
    }

    private IReadOnlyList<PropertyDescriptor> ReadProperties(byte objectNumber)
    {
        int entryAddress = GetObjectEntryAddress(objectNumber);
        int propertyTableAddress = _memory.ReadWord(entryAddress + 7);
        ValidateDynamicAddress(propertyTableAddress, "object property table");
        byte shortNameWordCount = _memory.ReadByte(propertyTableAddress);
        int cursor = propertyTableAddress + 1 + (shortNameWordCount * sizeof(ushort));
        if (cursor > _memory.StaticMemoryBase)
        {
            throw new InvalidOperationException($"Object {objectNumber}'s short name extends beyond dynamic memory.");
        }

        List<PropertyDescriptor> properties = [];
        int previousNumber = MaximumPropertyNumber + 1;
        while (true)
        {
            ValidateDynamicAddress(cursor, "property header");
            byte header = _memory.ReadByte(cursor++);
            if (header == 0)
            {
                return properties;
            }

            byte number = (byte)(header & 0x1F);
            byte length = (byte)((header >> 5) + 1);
            if (number == 0)
            {
                throw new InvalidOperationException($"Object {objectNumber}'s property table contains an invalid property number zero.");
            }

            if (number >= previousNumber)
            {
                throw new InvalidOperationException($"Object {objectNumber}'s property numbers are not in descending order.");
            }

            if (cursor + length > _memory.StaticMemoryBase)
            {
                throw new InvalidOperationException($"Object {objectNumber}'s property data extends beyond dynamic memory.");
            }

            properties.Add(new PropertyDescriptor(number, cursor, length));
            cursor += length;
            previousNumber = number;
        }
    }

    private void ValidateDynamicAddress(int address, string name)
    {
        if (address < 0 || address >= _memory.StaticMemoryBase)
        {
            throw new InvalidOperationException($"The {name} address 0x{address:X4} is outside dynamic memory.");
        }
    }

    private static void ValidateAttributeNumber(byte attributeNumber)
    {
        if (attributeNumber >= AttributeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(attributeNumber), "Version 3 attribute numbers range from 0 through 31.");
        }
    }

    private static void ValidatePropertyNumber(byte propertyNumber)
    {
        if (propertyNumber == 0 || propertyNumber > MaximumPropertyNumber)
        {
            throw new ArgumentOutOfRangeException(nameof(propertyNumber), "Version 3 property numbers range from 1 through 31.");
        }
    }

    private sealed record PropertyDescriptor(byte Number, int DataAddress, byte Length);
}
