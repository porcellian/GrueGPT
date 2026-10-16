using GrueGPT.Core.SaveFiles;
using Xunit;

namespace GrueGPT.Tests.SaveFiles;

public sealed class QuetzalStackCodecTests
{
    [Fact]
    public void WritesAndReadsTheRequiredEmptyDummyFrame()
    {
        QuetzalChunk chunk = QuetzalStackCodec.CreateStksChunk(new QuetzalStackState([], []));

        Assert.Equal("Stks", chunk.Id);
        Assert.Equal(new byte[8], chunk.Data.ToArray());
        QuetzalStackState restored = QuetzalStackCodec.ReadStksChunk(chunk);
        Assert.Empty(restored.TopLevelEvaluationStack);
        Assert.Empty(restored.Frames);
    }

    [Fact]
    public void PreservesDummyAndRoutineStackValuesAndFrameMetadata()
    {
        QuetzalStackState state = new(
            [0x1111, 0x2222],
            [new QuetzalStackFrame(0x123456, false, 0, 0b0000_0101, [0xAAAA, 0xBBBB], [0x3333, 0x4444])]);

        QuetzalChunk chunk = QuetzalStackCodec.CreateStksChunk(state);
        QuetzalStackState restored = QuetzalStackCodec.ReadStksChunk(chunk);

        Assert.Equal(
            new byte[]
            {
                0, 0, 0, 0, 0, 0, 0, 2, 0x11, 0x11, 0x22, 0x22,
                0x12, 0x34, 0x56, 2, 0, 5, 0, 2,
                0xAA, 0xAA, 0xBB, 0xBB, 0x33, 0x33, 0x44, 0x44,
            },
            chunk.Data.ToArray());
        Assert.Equal(new ushort[] { 0x1111, 0x2222 }, restored.TopLevelEvaluationStack);
        QuetzalStackFrame frame = Assert.Single(restored.Frames);
        Assert.Equal(0x123456, frame.ReturnProgramCounter);
        Assert.False(frame.DiscardResult);
        Assert.Equal((byte)0, frame.ResultVariable);
        Assert.Equal((byte)0b0000_0101, frame.ArgumentMask);
        Assert.Equal(new ushort[] { 0xAAAA, 0xBBBB }, frame.Locals);
        Assert.Equal(new ushort[] { 0x3333, 0x4444 }, frame.EvaluationStack);
    }

    [Fact]
    public void WritesDiscardResultFlagWithZeroResultVariable()
    {
        QuetzalStackFrame frame = new(0x10203, true, 0, 0b0000_0011, [], []);

        QuetzalStackFrame restored = Assert.Single(
            QuetzalStackCodec.ReadStksChunk(QuetzalStackCodec.CreateStksChunk(new QuetzalStackState([], [frame]))).Frames);

        Assert.True(restored.DiscardResult);
        Assert.Equal((byte)0, restored.ResultVariable);
    }

    [Fact]
    public void RequiresStksIdentifierAndRequiredDummyFrame()
    {
        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("CMem", [])));
        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", [])));
        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", [0, 0, 1, 0, 0, 0, 0, 0])));
    }

    [Theory]
    [InlineData(new byte[] { 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0x20, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0, 0, 0, 0, 0, 0x80, 0, 0 })]
    public void RejectsMalformedFrameHeaders(byte[] data)
    {
        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", data)));
    }

    [Fact]
    public void RejectsTruncatedLocalsAndEvaluationStack()
    {
        byte[] truncatedLocal = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0];
        byte[] truncatedStack = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1];

        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", truncatedLocal)));
        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", truncatedStack)));
    }

    [Fact]
    public void RejectsNonzeroResultVariableWhenDiscardFlagIsSet()
    {
        byte[] data = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0x10, 1, 0, 0, 0];

        Assert.Throws<InvalidDataException>(() => QuetzalStackCodec.ReadStksChunk(new QuetzalChunk("Stks", data)));
    }

    [Fact]
    public void RejectsUnrepresentableStackFramesWhenWriting()
    {
        QuetzalStackFrame invalid = new(0, false, 0, 0, Enumerable.Repeat((ushort)0, 16), []);

        Assert.Throws<InvalidDataException>(() =>
            QuetzalStackCodec.CreateStksChunk(new QuetzalStackState([], [invalid])));
    }
}
