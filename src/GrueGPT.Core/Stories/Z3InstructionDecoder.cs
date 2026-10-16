namespace GrueGPT.Core.Stories;

/// <summary>Reads complete Version 3 instructions and advances the execution program counter.</summary>
public sealed class Z3InstructionDecoder
{
    private readonly Z3ExecutionState _state;
    private readonly Z3Memory _memory;

    public Z3InstructionDecoder(Z3ExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _memory = state.Memory;
    }

    public Z3DecodedInstruction DecodeNext()
    {
        int instructionAddress = _state.ProgramCounter;
        int cursor = instructionAddress;
        byte opcode = ReadByte(ref cursor);
        Z3InstructionForm form;
        Z3OperandCount operandCount;
        byte opcodeNumber;
        List<Z3Operand> operands;

        if ((opcode & 0x80) == 0)
        {
            form = Z3InstructionForm.Long;
            operandCount = Z3OperandCount.Two;
            opcodeNumber = (byte)(opcode & 0x1F);
            Z3OperandType firstType = (opcode & 0x40) == 0 ? Z3OperandType.SmallConstant : Z3OperandType.Variable;
            Z3OperandType secondType = (opcode & 0x20) == 0 ? Z3OperandType.SmallConstant : Z3OperandType.Variable;
            operands =
            [
                ReadOperand(firstType, ref cursor),
                ReadOperand(secondType, ref cursor)
            ];
        }
        else if ((opcode & 0xC0) == 0x80)
        {
            form = Z3InstructionForm.Short;
            opcodeNumber = (byte)(opcode & 0x0F);
            int operandType = (opcode >> 4) & 0x03;
            if (operandType == 0x03)
            {
                operandCount = Z3OperandCount.Zero;
                operands = [];
            }
            else
            {
                operandCount = Z3OperandCount.One;
                operands = [ReadOperand((Z3OperandType)operandType, ref cursor)];
            }
        }
        else
        {
            form = Z3InstructionForm.Variable;
            opcodeNumber = (byte)(opcode & 0x1F);
            operandCount = (opcode & 0x20) == 0 ? Z3OperandCount.Two : Z3OperandCount.Variable;
            byte types = ReadByte(ref cursor);
            operands = ReadVariableOperands(types, operandCount, ref cursor);
        }

        byte? storeVariable = HasStore(operandCount, opcodeNumber) ? ReadByte(ref cursor) : null;
        Z3Branch? branch = HasBranch(operandCount, opcodeNumber) ? ReadBranch(ref cursor) : null;
        int? inlineTextAddress = null;
        int? inlineTextLength = null;
        if (operandCount == Z3OperandCount.Zero && opcodeNumber is 2 or 3)
        {
            inlineTextAddress = cursor;
            cursor = SkipInlineText(cursor);
            inlineTextLength = cursor - inlineTextAddress.Value;
        }

        _state.SetProgramCounter(cursor);
        return new Z3DecodedInstruction(
            instructionAddress,
            form,
            operandCount,
            opcodeNumber,
            operands,
            storeVariable,
            branch,
            inlineTextAddress,
            inlineTextLength,
            cursor);
    }

    private List<Z3Operand> ReadVariableOperands(byte typeByte, Z3OperandCount count, ref int cursor)
    {
        int fieldsToRead = count == Z3OperandCount.Two ? 2 : 4;
        List<Z3Operand> operands = new(fieldsToRead);
        bool omitted = false;

        for (int fieldIndex = 0; fieldIndex < 4; fieldIndex++)
        {
            int encodedType = (typeByte >> (6 - fieldIndex * 2)) & 0x03;
            if (encodedType == 0x03)
            {
                omitted = true;
                continue;
            }

            if (omitted || fieldIndex >= fieldsToRead)
            {
                throw new InvalidDataException("An operand type follows an omitted operand or exceeds the instruction's operand count.");
            }

            operands.Add(ReadOperand((Z3OperandType)encodedType, ref cursor));
        }

        if (count == Z3OperandCount.Two && operands.Count != 2)
        {
            throw new InvalidDataException("A variable-form 2OP instruction must encode exactly two operands.");
        }

        return operands;
    }

    private Z3Operand ReadOperand(Z3OperandType type, ref int cursor)
    {
        if (type == Z3OperandType.LargeConstant)
        {
            ushort value = _memory.ReadWord(cursor);
            cursor += sizeof(ushort);
            return new Z3Operand(type, value);
        }

        byte encodedValue = ReadByte(ref cursor);
        if (type == Z3OperandType.SmallConstant)
        {
            return new Z3Operand(type, encodedValue);
        }

        ushort variableValue = _state.ReadVariableValue(encodedValue);
        return new Z3Operand(type, variableValue, encodedValue);
    }

    private Z3Branch ReadBranch(ref int cursor)
    {
        byte firstByte = ReadByte(ref cursor);
        bool branchOnTrue = (firstByte & 0x80) != 0;
        int offset;
        if ((firstByte & 0x40) != 0)
        {
            offset = firstByte & 0x3F;
        }
        else
        {
            int encodedOffset = ((firstByte & 0x3F) << 8) | ReadByte(ref cursor);
            offset = (encodedOffset & 0x2000) == 0 ? encodedOffset : encodedOffset - 0x4000;
        }

        return new Z3Branch(branchOnTrue, offset, cursor);
    }

    private int SkipInlineText(int cursor)
    {
        while (true)
        {
            ushort word = _memory.ReadWord(cursor);
            cursor += sizeof(ushort);
            if ((word & 0x8000) != 0)
            {
                return cursor;
            }
        }
    }

    private byte ReadByte(ref int cursor)
    {
        byte value = _memory.ReadByte(cursor);
        cursor++;
        return value;
    }

    private static bool HasStore(Z3OperandCount operandCount, byte opcodeNumber) => operandCount switch
    {
        Z3OperandCount.One => opcodeNumber is 1 or 2 or 3 or 4 or 14 or 15,
        Z3OperandCount.Two => opcodeNumber is 8 or 9 or >= 15 and <= 24,
        Z3OperandCount.Variable => opcodeNumber is 0 or 7,
        _ => false
    };

    private static bool HasBranch(Z3OperandCount operandCount, byte opcodeNumber) => operandCount switch
    {
        Z3OperandCount.Zero => opcodeNumber is 5 or 6 or 13,
        Z3OperandCount.One => opcodeNumber is 0 or 1 or 2,
        Z3OperandCount.Two => opcodeNumber is >= 1 and <= 7 or 10,
        _ => false
    };
}
