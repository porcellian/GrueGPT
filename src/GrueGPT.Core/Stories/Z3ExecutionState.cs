namespace GrueGPT.Core.Stories;

/// <summary>Version 3 program counter, routine frames, variables, and evaluation stacks.</summary>
public sealed class Z3ExecutionState
{
    private readonly List<Z3RoutineFrame> _frames = [];
    private readonly int _globalVariablesAddress;

    public Z3ExecutionState(LoadedStory story)
    {
        ArgumentNullException.ThrowIfNull(story);
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

    public int ProgramCounter { get; private set; }

    public int CallDepth => _frames.Count - 1;

    public int LocalCount => CurrentFrame.Locals.Length;

    public int CurrentArgumentCount => CurrentFrame.ArgumentCount;

    public int? CurrentRoutineAddress => CurrentFrame.RoutineAddress;

    public int EvaluationStackDepth => CurrentFrame.Stack.Count;

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
            arguments.Count));
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
        int ArgumentCount)
    {
        public Stack<ushort> Stack { get; } = new();
    }
}
