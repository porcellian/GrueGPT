namespace GrueGPT.Core.Stories;

/// <summary>Receives each decoded Version 3 instruction immediately before execution.</summary>
public interface IZ3InstructionTraceSink
{
    void OnInstruction(Z3OpcodeTraceEntry instruction);
}

/// <summary>Identifies one dynamic instruction execution in a Version 3 story.</summary>
public sealed record Z3OpcodeTraceEntry(
    long Sequence,
    int ProgramCounter,
    Z3InstructionForm Form,
    byte OpcodeNumber,
    string Mnemonic);
