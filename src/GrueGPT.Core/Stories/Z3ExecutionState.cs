using GrueGPT.Core.SaveFiles;

namespace GrueGPT.Core.Stories;

/// <summary>Version 3 program counter, routine frames, variables, and evaluation stacks.</summary>
public sealed class Z3ExecutionState
{
    private readonly List<Z3RoutineFrame> _frames = [];
    private readonly int _globalVariablesAddress;

    public Z3ExecutionState(LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);
        Header = story.Header;
        Memory = new Z3Memory(story);
        _globalVariablesAddress = story.Header.GlobalVariablesAddress;
        ProgramCounter = story.Header.InitialProgramCounter;
        _frames.Add(new Z3RoutineFrame(
            null,
            null,
            null,
            [],
            0));
    }

    public Z3Memory Memory { get; }

    public StoryHeader Header { get; }

    public int ProgramCounter { get; private set; }

    public int CallDepth => _frames.Count - 1;

    public int LocalCount => CurrentFrame.Locals.Length;

    public int CurrentArgumentCount => CurrentFrame.ArgumentCount;

    public int? CurrentRoutineAddress => CurrentFrame.RoutineAddress;

    public int EvaluationStackDepth => CurrentFrame.Stack.Count;

    /// <summary>Creates a Quetzal-compatible snapshot of all active Version 3 frames and stacks.</summary>
    public QuetzalStackState CreateStackSnapshot()
    {
        QuetzalStackFrame[] frames = _frames.Skip(1)
            .Select(static frame => new QuetzalStackFrame(
                frame.ReturnAddress!.Value,
                frame.ResultVariable is null,
                frame.ResultVariable ?? 0,
                frame.ArgumentMask,
                frame.Locals,
                frame.Stack.Reverse()))
            .ToArray();

        return new QuetzalStackState(_frames[0].Stack.Reverse(), frames);
    }

    /// <summary>Restores all stack frames and sets the program counter from a validated save.</summary>
    public void RestoreStackSnapshot(QuetzalStackState snapshot, int programCounter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ValidateStackSnapshot(snapshot, programCounter);

        List<Z3RoutineFrame> restoredFrames =
        [new Z3RoutineFrame(null, null, null, [], 0)];
        restoredFrames[0].StackLoad(snapshot.TopLevelEvaluationStack);

        foreach (QuetzalStackFrame frame in snapshot.Frames)
        {
            Z3RoutineFrame restored = new(
                null,
                frame.ReturnProgramCounter,
                frame.DiscardResult ? null : frame.ResultVariable,
                frame.Locals.ToArray(),
                CountArguments(frame.ArgumentMask),
                frame.ArgumentMask);
            restored.StackLoad(frame.EvaluationStack);
            restoredFrames.Add(restored);
        }

        _frames.Clear();
        _frames.AddRange(restoredFrames);
        ProgramCounter = programCounter;
    }

    /// <summary>Checks that a decoded stack can replace this state's frames without mutation.</summary>
    public void ValidateStackSnapshot(QuetzalStackState snapshot, int programCounter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (programCounter < 0 || programCounter >= Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(programCounter), "The program counter must point inside the story image.");
        }

        if (snapshot.TopLevelEvaluationStack.Count > ushort.MaxValue)
        {
            throw new InvalidDataException("The top-level evaluation stack exceeds the Stks format limit.");
        }

        for (int index = 0; index < snapshot.Frames.Count; index++)
        {
            QuetzalStackFrame frame = snapshot.Frames[index];
            if (frame.ReturnProgramCounter < 0 || frame.ReturnProgramCounter >= Memory.Length)
            {
                throw new InvalidDataException("A saved frame return address is outside the story image.");
            }

            if (frame.Locals.Count > 15 || frame.EvaluationStack.Count > ushort.MaxValue || frame.ArgumentMask > 0x7F
                || (frame.DiscardResult && frame.ResultVariable != 0))
            {
                throw new InvalidDataException("A saved frame contains invalid Version 3 frame metadata.");
            }

            if (!frame.DiscardResult && frame.ResultVariable is > 0 and <= 15)
            {
                int callerLocalCount = index == 0 ? 0 : snapshot.Frames[index - 1].Locals.Count;
                if (frame.ResultVariable > callerLocalCount)
                {
                    throw new InvalidDataException("A saved frame result variable is not present in its caller.");
                }
            }
        }
    }

    private static int CountArguments(byte argumentMask)
    {
        int count = 0;
        while (argumentMask != 0)
        {
            count += argumentMask & 1;
            argumentMask >>= 1;
        }

        return count;
    }

    private Z3RoutineFrame CurrentFrame => _frames[^1];

    /// <summary>Reads a variable by value; variable zero pops the current routine stack.</summary>
    public ushort ReadVariableValue(byte variableNumber)
    {
        if (variableNumber == 0)
        {
            return PopStack();
        }

        return ReadVariableWithoutStackEffect(variableNumber);
    }

    /// <summary>Writes a result variable; variable zero pushes onto the current routine stack.</summary>
    public void WriteVariable(byte variableNumber, ushort value)
    {
        if (variableNumber == 0)
        {
            PushStack(value);
            return;
        }

        WriteVariableWithoutStackEffect(variableNumber, value);
    }

    /// <summary>Reads an indirect variable reference; stack variable zero is read without popping.</summary>
    public ushort ReadVariableIndirect(byte variableNumber)
    {
        if (variableNumber == 0)
        {
            return PeekStack();
        }

        return ReadVariableWithoutStackEffect(variableNumber);
    }

    /// <summary>Writes an indirect variable reference; stack variable zero replaces the top entry.</summary>
    public void WriteVariableIndirect(byte variableNumber, ushort value)
    {
        if (variableNumber == 0)
        {
            _ = PopStack();
            PushStack(value);
            return;
        }

        WriteVariableWithoutStackEffect(variableNumber, value);
    }

    public void PushStack(ushort value) => CurrentFrame.Stack.Push(value);

    public ushort PopStack()
    {
        if (CurrentFrame.Stack.Count == 0)
        {
            throw new InvalidOperationException("The current routine's evaluation stack is empty.");
        }

        return CurrentFrame.Stack.Pop();
    }

    public ushort PeekStack()
    {
        if (CurrentFrame.Stack.Count == 0)
        {
            throw new InvalidOperationException("The current routine's evaluation stack is empty.");
        }

        return CurrentFrame.Stack.Peek();
    }

    /// <summary>
    /// Calls a packed routine. A packed address of zero returns false immediately;
    /// otherwise the routine's v3 locals are initialized from its header and arguments.
    /// </summary>
    public void CallRoutine(ushort packedAddress, IReadOnlyList<ushort> arguments, byte? resultVariable)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(arguments), "A Version 3 routine accepts at most three arguments.");
        }

        if (packedAddress == 0)
        {
            if (resultVariable.HasValue)
            {
                WriteVariable(resultVariable.Value, 0);
            }

            return;
        }

        int routineAddress = Z3Memory.UnpackAddress(packedAddress);
        byte localCount = Memory.ReadByte(routineAddress);
        if (localCount > 15)
        {
            throw new InvalidDataException($"Routine at 0x{routineAddress:X} declares {localCount} locals; Version 3 permits at most 15.");
        }

        ushort[] locals = new ushort[localCount];
        int nextAddress = routineAddress + 1;
        for (int localIndex = 0; localIndex < localCount; localIndex++)
        {
            locals[localIndex] = Memory.ReadWord(nextAddress);
            nextAddress += sizeof(ushort);
        }

        for (int argumentIndex = 0; argumentIndex < Math.Min(arguments.Count, locals.Length); argumentIndex++)
        {
            locals[argumentIndex] = arguments[argumentIndex];
        }

        _frames.Add(new Z3RoutineFrame(
            routineAddress,
            ProgramCounter,
            resultVariable,
            locals,
            arguments.Count,
            (byte)((1 << arguments.Count) - 1)));
        SetProgramCounter(nextAddress);
    }

    /// <summary>Returns from the current routine, discarding its evaluation stack.</summary>
    public void ReturnFromRoutine(ushort value)
    {
        if (_frames.Count == 1)
        {
            throw new InvalidOperationException("The initial Version 3 execution frame cannot return.");
        }

        Z3RoutineFrame returningFrame = CurrentFrame;
        _frames.RemoveAt(_frames.Count - 1);
        ProgramCounter = returningFrame.ReturnAddress!.Value;
        if (returningFrame.ResultVariable.HasValue)
        {
            WriteVariable(returningFrame.ResultVariable.Value, value);
        }
    }

    /// <summary>Applies an encoded signed 16-bit jump offset relative to the current PC.</summary>
    public void Jump(ushort encodedOffset) => SetProgramCounter(ProgramCounter + unchecked((short)encodedOffset) - 2);

    public void SetProgramCounter(int address)
    {
        if (address < 0 || address >= Memory.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "The program counter must point inside the story image.");
        }

        ProgramCounter = address;
    }

    /// <summary>Restores initial memory and execution state for the Version 3 restart opcode.</summary>
    public void Restart()
    {
        Memory.ResetForRestart();
        _frames.Clear();
        _frames.Add(new Z3RoutineFrame(null, null, null, [], 0));
        ProgramCounter = Header.InitialProgramCounter;
    }

    private ushort ReadVariableWithoutStackEffect(byte variableNumber)
    {
        if (variableNumber <= 15)
        {
            int localIndex = variableNumber - 1;
            if (localIndex >= CurrentFrame.Locals.Length)
            {
                throw new InvalidOperationException($"Local variable {variableNumber} does not exist in the current routine.");
            }

            return CurrentFrame.Locals[localIndex];
        }

        int globalIndex = variableNumber - 16;
        return Memory.ReadWord(_globalVariablesAddress + globalIndex * sizeof(ushort));
    }

    private void WriteVariableWithoutStackEffect(byte variableNumber, ushort value)
    {
        if (variableNumber <= 15)
        {
            int localIndex = variableNumber - 1;
            if (localIndex >= CurrentFrame.Locals.Length)
            {
                throw new InvalidOperationException($"Local variable {variableNumber} does not exist in the current routine.");
            }

            CurrentFrame.Locals[localIndex] = value;
            return;
        }

        int globalIndex = variableNumber - 16;
        Memory.WriteWord(_globalVariablesAddress + globalIndex * sizeof(ushort), value);
    }

    private sealed record Z3RoutineFrame(
        int? RoutineAddress,
        int? ReturnAddress,
        byte? ResultVariable,
        ushort[] Locals,
        int ArgumentCount,
        byte ArgumentMask = 0)
    {
        public Stack<ushort> Stack { get; } = new();

        public void StackLoad(IEnumerable<ushort> values)
        {
            foreach (ushort value in values)
            {
                Stack.Push(value);
            }
        }
    }
}
