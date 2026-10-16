using GrueGPT.Core.SaveFiles;
using Xunit;

namespace GrueGPT.Tests.SaveFiles;

public sealed class QuetzalIfhdTests
{
    [Fact]
    public void WritesAndReadsTheThirteenByteIfhdPayload()
    {
        QuetzalIfhd source = new(0x1234, [0x31, 0x32, 0x33, 0x34, 0x35, 0x36], 0xABCD, 0x123456);

        QuetzalChunk chunk = source.ToChunk();
        QuetzalIfhd parsed = QuetzalIfhd.Read(chunk);

        Assert.Equal("IFhd", chunk.Id);
        Assert.Equal(13, chunk.Data.Length);
        Assert.Equal(new byte[] { 0x12, 0x34, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0xAB, 0xCD, 0x12, 0x34, 0x56 }, chunk.Data.ToArray());
        Assert.Equal(source.ReleaseNumber, parsed.ReleaseNumber);
        Assert.Equal(source.SerialNumber.ToArray(), parsed.SerialNumber.ToArray());
        Assert.Equal(source.Checksum, parsed.Checksum);
        Assert.Equal(source.ProgramCounter, parsed.ProgramCounter);
    }

    [Fact]
    public void ReadsAndPreservesFutureIfhdExtensionBytes()
    {
        QuetzalIfhd source = new(1, [1, 2, 3, 4, 5, 6], 2, 0xFFFFFF);
        QuetzalChunk chunk = source.ToChunk();
        byte[] extendedData = [.. chunk.Data.ToArray(), 0xAA, 0xBB];

        QuetzalIfhd parsed = QuetzalIfhd.Read(new QuetzalChunk("IFhd", extendedData));

        Assert.Equal(source.ProgramCounter, parsed.ProgramCounter);
        Assert.Equal(source.SerialNumber.ToArray(), parsed.SerialNumber.ToArray());
        Assert.Equal(new byte[] { 0xAA, 0xBB }, parsed.ExtensionData.ToArray());
        Assert.Equal(extendedData, parsed.ToChunk().Data.ToArray());
    }

    [Fact]
    public void RejectsIfhdChunksWithTheWrongIdentifierOrTooFewBytes()
    {
        Assert.Throws<InvalidDataException>(() => QuetzalIfhd.Read(new QuetzalChunk("AUTH", new byte[13])));
        Assert.Throws<InvalidDataException>(() => QuetzalIfhd.Read(new QuetzalChunk("IFhd", new byte[12])));
    }

    [Fact]
    public void RequiresSixSerialBytesAndAThreeByteProgramCounter()
    {
        Assert.Throws<ArgumentException>(() => new QuetzalIfhd(1, [1, 2, 3, 4, 5], 2, 0));
        Assert.Throws<ArgumentException>(() => new QuetzalIfhd(1, [1, 2, 3, 4, 5, 6, 7], 2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuetzalIfhd(1, [1, 2, 3, 4, 5, 6], 2, 0x1000000));
    }

    [Fact]
    public void CopiesTheSerialNumberProvidedByTheCaller()
    {
        byte[] serial = [1, 2, 3, 4, 5, 6];
        QuetzalIfhd ifhd = new(1, serial, 2, 3);
        serial[0] = 9;

        Assert.Equal((byte)1, ifhd.SerialNumber.Span[0]);
    }
}
