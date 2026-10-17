namespace GrueGPT.Core.Stories;

/// <summary>Maps Version 3 instruction encodings to their standard mnemonic.</summary>
public static class Z3OpcodeNames
{
    public static string GetMnemonic(Z3DecodedInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        return GetMnemonic(instruction.OperandCount, instruction.OpcodeNumber);
    }

    public static string GetMnemonic(Z3OperandCount operandCount, byte opcodeNumber)
    {
        string? mnemonic = operandCount switch
        {
            Z3OperandCount.Zero => GetName(opcodeNumber, "rtrue", "rfalse", "print", "print_ret", "nop", "save", "restore", "restart", "ret_popped", "pop", "quit", "new_line", "show_status", "verify"),
            Z3OperandCount.One => GetName(opcodeNumber, "jz", "get_sibling", "get_child", "get_parent", "get_prop_len", "inc", "dec", "print_addr", "call_1s", "remove_obj", "print_obj", "ret", "jump", "print_paddr", "load", "not"),
            Z3OperandCount.Two => opcodeNumber == 0 ? null : GetName((byte)(opcodeNumber - 1), "je", "jl", "jg", "dec_chk", "inc_chk", "jin", "test", "or", "and", "test_attr", "set_attr", "clear_attr", "store", "insert_obj", "loadw", "loadb", "get_prop", "get_prop_addr", "get_next_prop", "add", "sub", "mul", "div", "mod"),
            Z3OperandCount.Variable => GetVariableName(opcodeNumber),
            _ => null
        };

        return mnemonic ?? $"op_{operandCount.ToString().ToLowerInvariant()}_{opcodeNumber}";
    }

    private static string? GetVariableName(byte opcodeNumber) => GetName(
        opcodeNumber,
        "call_vs",
        "storew",
        "storeb",
        "put_prop",
        "sread",
        "print_char",
        "print_num",
        "random",
        "push",
        "pull",
        "split_window",
        "set_window",
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        "output_stream",
        "input_stream",
        "sound_effect");

    private static string? GetName(byte opcodeNumber, params string?[] names) =>
        opcodeNumber < names.Length ? names[opcodeNumber] : null;
}
