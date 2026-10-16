using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3InstructionDecoderTests
{
    [Fact]
    public void DecodesLongFormOperandsAndStoreVariable()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0x14, 7, 8, 16, 0xB0]));

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(Z3InstructionForm.Long, instruction.Form);
        Assert.Equal(Z3OperandCount.Two, instruction.OperandCount);
        Assert.Equal(20, instruction.OpcodeNumber);
        Assert.Equal(new Z3Operand(Z3OperandType.SmallConstant, 7), instruction.Operands[0]);
        Assert.Equal(new Z3Operand(Z3OperandType.SmallConstant, 8), instruction.Operands[1]);
        Assert.Equal((byte)16, instruction.StoreVariable);
        Assert.Equal(0x2004, state.ProgramCounter);
    }

    [Fact]
    public void EvaluatesLongFormVariableOperandsFromLeftToRight()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0x74, 0, 0, 16]));
        state.PushStack(0x1111);
        state.PushStack(0x2222);

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(0x2222, instruction.Operands[0].Value);
        Assert.Equal((byte)0, instruction.Operands[0].VariableNumber);
        Assert.Equal(0x1111, instruction.Operands[1].Value);
        Assert.Equal(0, state.EvaluationStackDepth);
    }

    [Fact]
    public void DecodesShortFormLargeConstant()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0x8E, 0x12, 0x34, 16]));

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(Z3InstructionForm.Short, instruction.Form);
        Assert.Equal(Z3OperandType.LargeConstant, instruction.Operands[0].Type);
        Assert.Equal(0x1234, instruction.Operands[0].Value);
        Assert.Equal((byte)16, instruction.StoreVariable);
    }

    [Fact]
    public void DecodesShortFormVariableAndVariableFormVariableOperands()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteWord(0x200, 0x9876);
        builder.WriteBytes(0x2000, [0xA0, 16, 0xC1, 0xE8, 0xBF, 16]);
        Z3ExecutionState state = CreateState(builder);
        Z3InstructionDecoder decoder = new(state);

        Z3DecodedInstruction branch = decoder.DecodeNext();
        Z3DecodedInstruction push = decoder.DecodeNext();

        Assert.Equal(Z3OperandType.Variable, branch.Operands[0].Type);
        Assert.Equal(0x9876, branch.Operands[0].Value);
        Assert.Equal(0x9876, push.Operands[0].Value);
        Assert.Equal((byte)16, push.Operands[0].VariableNumber);
        Assert.Equal(0x2006, state.ProgramCounter);
    }

    [Fact]
    public void DecodesVariableFormTypesAndLargeConstants()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0xE0, 0x1F, 0x11, 0x00, 7, 16]));

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(Z3OperandCount.Variable, instruction.OperandCount);
        Assert.Equal(new Z3Operand(Z3OperandType.LargeConstant, 0x1100), instruction.Operands[0]);
        Assert.Equal(new Z3Operand(Z3OperandType.SmallConstant, 7), instruction.Operands[1]);
        Assert.Equal((byte)16, instruction.StoreVariable);
        Assert.Equal(0x2006, state.ProgramCounter);
    }

    [Fact]
    public void DecodesVariableFormTwoOperandInstruction()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0xC8, 0x1F, 0x12, 0x34, 5, 16]));

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(Z3OperandCount.Two, instruction.OperandCount);
        Assert.Equal(8, instruction.OpcodeNumber);
        Assert.Equal(0x1234, instruction.Operands[0].Value);
        Assert.Equal(5, instruction.Operands[1].Value);
        Assert.Equal(0x2006, state.ProgramCounter);
    }

    [Fact]
    public void DecodesOneOperandStoreAndBranchInstructions()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0x91, 7, 16, 0xC5]));

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(Z3OperandCount.One, instruction.OperandCount);
        Assert.Equal(1, instruction.OpcodeNumber);
        Assert.Equal((byte)16, instruction.StoreVariable);
        Assert.NotNull(instruction.Branch);
        Assert.True(instruction.Branch.Value.BranchOnTrue);
        Assert.Equal(5, instruction.Branch.Value.Offset);
        instruction.Branch.Value.Apply(state, condition: true);
        Assert.Equal(0x2007, state.ProgramCounter);
    }

    [Fact]
    public void DecodesSignedFourteenBitBranchOffsets()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0xBD, 0x3F, 0xFE]));
        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(-2, instruction.Branch!.Value.Offset);
        instruction.Branch.Value.Apply(state, condition: false);

        Assert.Equal(0x1FFF, state.ProgramCounter);
    }

    [Theory]
    [InlineData(0xC0, 0)]
    [InlineData(0xC1, 1)]
    public void BranchOffsetsZeroAndOneReturnFromTheCurrentRoutine(byte branchByte, ushort expected)
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2200, 0).WriteBytes(0x2201, [0x90, 0, branchByte]);
        Z3ExecutionState state = CreateState(builder);
        state.CallRoutine(Z3Memory.PackAddress(0x2200), [], resultVariable: 16);

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();
        instruction.Branch!.Value.Apply(state, condition: true);

        Assert.Equal(0, state.CallDepth);
        Assert.Equal(expected, state.ReadVariableValue(16));
        Assert.Equal(0x2000, state.ProgramCounter);
    }

    [Fact]
    public void AdvancesPastInlinePrintText()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2000, 0xB2).WriteWord(0x2001, EncodeWord(25, 10, 24, isLast: true));
        Z3ExecutionState state = CreateState(builder);

        Z3DecodedInstruction instruction = new Z3InstructionDecoder(state).DecodeNext();

        Assert.Equal(0x2001, instruction.InlineTextAddress);
        Assert.Equal(2, instruction.InlineTextLength);
        Assert.Equal(0x2003, instruction.NextAddress);
        Assert.Equal("tes", new Z3TextDecoder(state.Memory, 0x0300).DecodeAt(instruction.InlineTextAddress!.Value));
    }

    [Fact]
    public void RejectsNonOmittedOperandTypesAfterOmission()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder()
            .WriteBytes(0x2000, [0xE8, 0x7B, 1, 2]));

        Assert.Throws<InvalidDataException>(() => new Z3InstructionDecoder(state).DecodeNext());
    }

    private static Z3ExecutionState CreateState(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }

    private static ushort EncodeWord(int first, int second, int third, bool isLast)
    {
        ushort word = (ushort)((first << 10) | (second << 5) | third);
        return isLast ? (ushort)(word | 0x8000) : word;
    }
}
