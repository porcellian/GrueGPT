namespace GrueGPT.Core.SaveFiles;

/// <summary>A Version 3 evaluation stack and its active call frames, ordered oldest first.</summary>
public sealed class QuetzalStackState
{
    private readonly ushort[] _topLevelEvaluationStack;
    private readonly IReadOnlyList<QuetzalStackFrame> _frames;

    public QuetzalStackState(
        IEnumerable<ushort> topLevelEvaluationStack,
        IEnumerable<QuetzalStackFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(topLevelEvaluationStack);
        ArgumentNullException.ThrowIfNull(frames);
        _topLevelEvaluationStack = topLevelEvaluationStack.ToArray();
        QuetzalStackFrame[] frameCopy = frames.ToArray();
        if (frameCopy.Any(static frame => frame is null))
        {
            throw new ArgumentException("A stack state cannot contain a null frame.", nameof(frames));
        }

        _frames = Array.AsReadOnly(frameCopy);
    }

    public IReadOnlyList<ushort> TopLevelEvaluationStack => Array.AsReadOnly(_topLevelEvaluationStack);

    public IReadOnlyList<QuetzalStackFrame> Frames => _frames;
}

/// <summary>A saved Version 3 call frame; stack values are ordered least recent first.</summary>
public sealed class QuetzalStackFrame
{
    private readonly ushort[] _locals;
    private readonly ushort[] _evaluationStack;

    public QuetzalStackFrame(
        int returnProgramCounter,
        bool discardResult,
        byte resultVariable,
        byte argumentMask,
        IEnumerable<ushort> locals,
        IEnumerable<ushort> evaluationStack)
    {
        ArgumentNullException.ThrowIfNull(locals);
        ArgumentNullException.ThrowIfNull(evaluationStack);
        ReturnProgramCounter = returnProgramCounter;
        DiscardResult = discardResult;
        ResultVariable = resultVariable;
        ArgumentMask = argumentMask;
        _locals = locals.ToArray();
        _evaluationStack = evaluationStack.ToArray();
    }

    public int ReturnProgramCounter { get; }

    public bool DiscardResult { get; }

    public byte ResultVariable { get; }

    public byte ArgumentMask { get; }

    public IReadOnlyList<ushort> Locals => Array.AsReadOnly(_locals);

    public IReadOnlyList<ushort> EvaluationStack => Array.AsReadOnly(_evaluationStack);
}
