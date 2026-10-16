using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3ExecutionStateTests
{
    [Fact]
    public void ReadsAndWritesLocalAndGlobalVariables()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2200, 2).WriteWord(0x2201, 0x1111).WriteWord(0x2203, 0x2222);
        builder.WriteWord(0x200, 0x1234);
        Z3ExecutionState state = CreateState(builder);
        state.CallRoutine(Z3Memory.PackAddress(0x2200), [], resultVariable: null);

        Assert.Equal(2, state.LocalCount);
        Assert.Equal(0x1111, state.ReadVariableValue(1));
        state.WriteVariable(2, 0xABCD);
        Assert.Equal(0xABCD, state.ReadVariableValue(2));
        Assert.Equal(0x1234, state.ReadVariableValue(16));
        state.WriteVariable(16, 0xBEEF);
        Assert.Equal(0xBEEF, state.Memory.ReadWord(0x200));
    }

    [Fact]
    public void DistinguishesStackVariableValueAndIndirectAccess()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());
        state.PushStack(0x1111);

        Assert.Equal(0x1111, state.ReadVariableIndirect(0));
        Assert.Equal(1, state.EvaluationStackDepth);
        state.WriteVariableIndirect(0, 0x2222);
        Assert.Equal(0x2222, state.PeekStack());
        Assert.Equal(0x2222, state.ReadVariableValue(0));
        Assert.Equal(0, state.EvaluationStackDepth);
        Assert.Throws<InvalidOperationException>(() => state.ReadVariableValue(0));
    }

    [Fact]
    public void RejectsReferencesToLocalsThatDoNotExist()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());

        Assert.Throws<InvalidOperationException>(() => state.ReadVariableValue(1));
        Assert.Throws<InvalidOperationException>(() => state.WriteVariable(15, 0));
    }

    [Fact]
    public void CallsRoutineWithInitializedLocalsAndArgumentsThenRestoresCaller()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2200, 2).WriteWord(0x2201, 0x1111).WriteWord(0x2203, 0x2222);
        Z3ExecutionState state = CreateState(builder);
        state.SetProgramCounter(0x2010);
        state.PushStack(0x7777);
        state.CallRoutine(Z3Memory.PackAddress(0x2200), [0xAAAA, 0xBBBB, 0xCCCC], resultVariable: 0);

        Assert.Equal(1, state.CallDepth);
        Assert.Equal(0x2200, state.CurrentRoutineAddress);
        Assert.Equal(3, state.CurrentArgumentCount);
        Assert.Equal(0xAAAA, state.ReadVariableValue(1));
        Assert.Equal(0xBBBB, state.ReadVariableValue(2));
        Assert.Equal(0, state.EvaluationStackDepth);

        state.PushStack(0xDEAD);
        state.ReturnFromRoutine(0x1234);

        Assert.Equal(0, state.CallDepth);
        Assert.Equal(0x2010, state.ProgramCounter);
        Assert.Equal(0x1234, state.PopStack());
        Assert.Equal(0x7777, state.PopStack());
    }

    [Fact]
    public void InitializesLocalsFromRoutineHeaderAndDropsArgumentsBeyondLocalCount()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2200, 1).WriteWord(0x2201, 0x3456);
        Z3ExecutionState state = CreateState(builder);

        state.CallRoutine(Z3Memory.PackAddress(0x2200), [0xABCD, 0xEF01], resultVariable: null);

        Assert.Equal(0xABCD, state.ReadVariableValue(1));
        Assert.Equal(2, state.CurrentArgumentCount);
    }

    [Fact]
    public void PackedAddressZeroReturnsFalseWithoutPushingACallFrame()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());

        state.CallRoutine(0, [], resultVariable: 16);

        Assert.Equal(0, state.CallDepth);
        Assert.Equal(0, state.ReadVariableValue(16));
    }

    [Fact]
    public void RejectsInvalidRoutineHeadersAndMoreThanThreeArguments()
    {
        SyntheticStoryBuilder builder = new();
        builder.WriteByte(0x2200, 16);
        Z3ExecutionState state = CreateState(builder);

        Assert.Throws<InvalidDataException>(() => state.CallRoutine(Z3Memory.PackAddress(0x2200), [], null));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.CallRoutine(0, [1, 2, 3, 4], null));
    }

    [Fact]
    public void RootFrameCannotReturnAndJumpUsesSignedRelativeOffset()
    {
        Z3ExecutionState state = CreateState(new SyntheticStoryBuilder());

        Assert.Throws<InvalidOperationException>(() => state.ReturnFromRoutine(0));
        state.SetProgramCounter(0x2010);
        state.Jump(0xFFFC);

        Assert.Equal(0x200A, state.ProgramCounter);
    }

    private static Z3ExecutionState CreateState(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }
}
