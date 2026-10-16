using System.Buffers.Binary;
using System.Text;
using GrueGPT.Core.SaveFiles;
using GrueGPT.Core.Stories;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.SaveFiles;

public sealed class QuetzalInteroperabilityTests
{
    [Fact]
    public void RestoresAStandardFramedUmemSaveWithAnUnknownOddLengthChunk()
    {
        SyntheticStoryBuilder builder = new SyntheticStoryBuilder()
            .WriteBytes(0x2200, [0xC4]) // SAVE resumes by taking a one-byte branch with offset 4.
            .WriteWord(0x0200, 0x1234);
        Z3ExecutionState source = CreateState(builder);
        source.SetProgramCounter(0x2200);
        source.PushStack(0xBEEF);
        Z3Memory memory = source.Memory;
        byte[] ifhdPayload = new byte[13];
        BinaryPrimitives.WriteUInt16BigEndian(ifhdPayload.AsSpan(0, 2), source.Header.ReleaseNumber);
        memory.GetOriginalSerialNumber().CopyTo(ifhdPayload, 2);
        BinaryPrimitives.WriteUInt16BigEndian(ifhdPayload.AsSpan(8, 2), source.Header.Checksum);
        ifhdPayload[10] = 0;
        ifhdPayload[11] = 0x22;
        ifhdPayload[12] = 0x00;
        QuetzalChunk[] chunks =
        [
            new QuetzalChunk("IFhd", ifhdPayload),
            new QuetzalChunk("UMem", memory.GetCurrentDynamicMemory()),
            new QuetzalChunk("Stks", [0, 0, 0, 0, 0, 0, 0, 1, 0xBE, 0xEF]),
            new QuetzalChunk("AUTH", [0x47]),
        ];
        byte[] externallyFramedSave = WriteIndependentIfzs(chunks);
        Z3ExecutionState target = CreateState(builder);
        target.Memory.WriteWord(0x0200, 0xCAFE);

        Z3RestoreResult result = Z3SaveRestoreService.TryRestore(target, externallyFramedSave);

        Assert.Equal(Z3RestoreStatus.ResumedSaveOpcode, result.Status);
        Assert.Equal(0x2203, target.ProgramCounter);
        Assert.Equal((ushort)0x1234, target.Memory.ReadWord(0x0200));
        Assert.Equal((ushort)0xBEEF, target.PopStack());
    }

    private static byte[] WriteIndependentIfzs(IEnumerable<QuetzalChunk> chunks)
    {
        using MemoryStream stream = new();
        stream.Write("FORM"u8);
        stream.Write(new byte[sizeof(uint)]);
        stream.Write("IFZS"u8);
        Span<byte> length = stackalloc byte[sizeof(uint)];

        foreach (QuetzalChunk chunk in chunks)
        {
            stream.Write(Encoding.ASCII.GetBytes(chunk.Id));
            BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)chunk.Data.Length));
            stream.Write(length);
            stream.Write(chunk.Data.Span);
            if ((chunk.Data.Length & 1) != 0)
            {
                stream.WriteByte(0);
            }
        }

        byte[] encoded = stream.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(encoded.AsSpan(sizeof(uint)), checked((uint)(encoded.Length - 2 * sizeof(uint))));
        return encoded;
    }

    private static Z3ExecutionState CreateState(SyntheticStoryBuilder builder)
    {
        using MemoryStream stream = builder.BuildStream();
        return new Z3ExecutionState(new Z3StoryLoader().Load(stream));
    }
}
