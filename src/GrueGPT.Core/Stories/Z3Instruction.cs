namespace GrueGPT.Core.Stories;

public enum Z3InstructionForm
{
    Long,
    Short,
    Variable
}

public enum Z3OperandType
{
    LargeConstant,
    SmallConstant,
    Variable
}

public enum Z3OperandCount
{
    Zero,
    One,
    Two,
    Variable
}

/// <summary>An instruction operand after its encoded value has been read.</summary>
public readonly record struct Z3Operand(Z3OperandType Type, ushort Value, byte? VariableNumber = null);

/// <summary>Conditional branch data, positioned immediately after its encoded bytes.</summary>
public readonly record struct Z3Branch(bool BranchOnTrue, int Offset, int AddressAfterBranchData)
{
    public bool IsTaken(bool condition) => condition == BranchOnTrue;

    public void Apply(Z3ExecutionState state, bool condition)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!IsTaken(condition))
        {
            return;
        }

        if (Offset is 0 or 1)
        {
            state.ReturnFromRoutine((ushort)Offset);
            return;
        }

        state.SetProgramCounter(AddressAfterBranchData + Offset - 2);
    }
}

/// <summary>A decoded Version 3 instruction and the address of its next instruction.</summary>
public sealed record Z3DecodedInstruction(
    int Address,
    Z3InstructionForm Form,
    Z3OperandCount OperandCount,
    byte OpcodeNumber,
    IReadOnlyList<Z3Operand> Operands,
    byte? StoreVariable,
    Z3Branch? Branch,
    int? InlineTextAddress,
    int? InlineTextLength,
    int NextAddress);
