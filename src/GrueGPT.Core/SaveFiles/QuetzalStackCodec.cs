namespace GrueGPT.Core.SaveFiles;

/// <summary>Creates and restores the Version 3 Quetzal Stks chunk.</summary>
public static class QuetzalStackCodec
{
    private const int FrameHeaderLength = 8;

    /// <summary>Serializes a required dummy frame followed by active frames, oldest first.</summary>
    public static QuetzalChunk CreateStksChunk(QuetzalStackState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        using MemoryStream stream = new();
        WriteFrame(stream, 0, false, 0, 0, [], state.TopLevelEvaluationStack);
        foreach (QuetzalStackFrame frame in state.Frames)
        {
            ValidateFrame(frame);
            WriteFrame(
                stream,
                frame.ReturnProgramCounter,
                frame.DiscardResult,
                frame.ResultVariable,
                frame.ArgumentMask,
                frame.Locals,
                frame.EvaluationStack);
        }

        return new QuetzalChunk("Stks", stream.ToArray());
    }

    /// <summary>Parses a Stks chunk, requiring the Version 3 dummy frame.</summary>
    public static QuetzalStackState ReadStksChunk(QuetzalChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (!string.Equals(chunk.Id, "Stks", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected a Stks chunk, found '{chunk.Id}'.");
        }

        ReadOnlySpan<byte> data = chunk.Data.Span;
        int offset = 0;
        List<QuetzalStackFrame> frames = [];
        ushort[]? topLevelStack = null;
        while (offset < data.Length)
        {
            if (data.Length - offset < FrameHeaderLength)
            {
                throw new InvalidDataException("The Stks chunk ends with an incomplete frame header.");
            }

            int returnProgramCounter = (data[offset] << 16) | (data[offset + 1] << 8) | data[offset + 2];
            byte flags = data[offset + 3];
            if ((flags & 0xE0) != 0)
            {
                throw new InvalidDataException("The Stks frame uses reserved flag bits.");
            }

            bool discardResult = (flags & 0x10) != 0;
            int localCount = flags & 0x0F;
            byte resultVariable = data[offset + 4];
            byte argumentMask = data[offset + 5];
            if ((argumentMask & 0x80) != 0)
            {
                throw new InvalidDataException("The Stks frame argument mask uses its reserved top bit.");
            }

            if (discardResult && resultVariable != 0)
            {
                throw new InvalidDataException("A discard-result Stks frame must store result variable zero.");
            }

            int evaluationCount = ReadWord(data, offset + 6);
            int frameLength = checked(FrameHeaderLength + (localCount + evaluationCount) * sizeof(ushort));
            if (frameLength > data.Length - offset)
            {
                throw new InvalidDataException("The Stks chunk ends before all frame locals and stack values are present.");
            }

            int valueOffset = offset + FrameHeaderLength;
            ushort[] locals = ReadWords(data, ref valueOffset, localCount);
            ushort[] evaluationStack = ReadWords(data, ref valueOffset, evaluationCount);
            if (topLevelStack is null)
            {
                if (returnProgramCounter != 0 || flags != 0 || resultVariable != 0 || argumentMask != 0 || localCount != 0)
                {
                    throw new InvalidDataException("The first Stks frame must be the all-zero Version 3 dummy frame.");
                }

                topLevelStack = evaluationStack;
            }
            else
            {
                frames.Add(new QuetzalStackFrame(
                    returnProgramCounter,
                    discardResult,
                    resultVariable,
                    argumentMask,
                    locals,
                    evaluationStack));
            }

            offset += frameLength;
        }

        if (topLevelStack is null)
        {
            throw new InvalidDataException("The Stks chunk is missing the required Version 3 dummy frame.");
        }

        return new QuetzalStackState(topLevelStack, frames);
    }

    private static void WriteFrame(
        Stream stream,
        int returnProgramCounter,
        bool discardResult,
        byte resultVariable,
        byte argumentMask,
        IReadOnlyList<ushort> locals,
        IReadOnlyList<ushort> evaluationStack)
    {
        if (returnProgramCounter is < 0 or > 0xFFFFFF || locals.Count > 15
            || evaluationStack.Count > ushort.MaxValue || argumentMask > 0x7F
            || (discardResult && resultVariable != 0))
        {
            throw new InvalidDataException("The stack frame cannot be represented in a Version 3 Stks chunk.");
        }

        stream.WriteByte((byte)(returnProgramCounter >> 16));
        stream.WriteByte((byte)(returnProgramCounter >> 8));
        stream.WriteByte((byte)returnProgramCounter);
        stream.WriteByte((byte)((discardResult ? 0x10 : 0) | locals.Count));
        stream.WriteByte(resultVariable);
        stream.WriteByte(argumentMask);
        WriteWord(stream, (ushort)evaluationStack.Count);
        foreach (ushort local in locals)
        {
            WriteWord(stream, local);
        }

        foreach (ushort value in evaluationStack)
        {
            WriteWord(stream, value);
        }
    }

    private static void ValidateFrame(QuetzalStackFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Locals.Count > 15 || frame.ArgumentMask > 0x7F)
        {
            throw new InvalidDataException("The stack frame contains invalid Version 3 locals or argument mask.");
        }
    }

    private static ushort[] ReadWords(ReadOnlySpan<byte> data, ref int offset, int count)
    {
        ushort[] words = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            words[index] = ReadWord(data, offset);
            offset += sizeof(ushort);
        }

        return words;
    }

    private static ushort ReadWord(ReadOnlySpan<byte> data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static void WriteWord(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }
}
