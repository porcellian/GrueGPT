using GrueGPT.Cli;
using GrueGPT.Tests.TestUtilities;
using Xunit;

namespace GrueGPT.Tests.Cli;

public sealed class CliApplicationTests
{
    [Fact]
    public async Task RunAsyncRequiresAStoryPath()
    {
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await CliApplication.RunAsync([], input, output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("A story file path is required.", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public async Task RunAsyncLoadsStoryAndWritesStoryText()
    {
        using TemporaryFiles files = new();
        files.WriteStory(new SyntheticStoryBuilder()
            .WriteByte(0x2000, 0xE5) // print_char
            .WriteByte(0x2001, 0x7F) // one small-constant operand, then omitted operands
            .WriteByte(0x2002, (byte)'H')
            .WriteByte(0x2003, 0xBA)); // quit
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await CliApplication.RunAsync([files.StoryPath], input, output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("H", output.ToString());
    }

    [Fact]
    public async Task RunAsyncReadsAndEchoesACommandLine()
    {
        using TemporaryFiles files = new();
        files.WriteStory(new SyntheticStoryBuilder()
            .WriteByte(0x101, 4) // valid empty dictionary entry length
            .WriteByte(0x80, 8)
            .WriteByte(0x90, 4)
            .WriteByte(0x2000, 0xE4) // read
            .WriteByte(0x2001, 0x5F) // two small-constant operands, then omitted operands
            .WriteByte(0x2002, 0x80)
            .WriteByte(0x2003, 0x90)
            .WriteByte(0x2004, 0xBA)); // quit
        using StringReader input = new("look\n");
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await CliApplication.RunAsync([files.StoryPath], input, output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("> look\n", output.ToString());
        Assert.Contains("Score: 0  Moves: 0", error.ToString());
        Assert.DoesNotContain("look", error.ToString());
    }

    [Fact]
    public async Task StorySaveCanBeRestoredOnStartup()
    {
        using TemporaryFiles files = new();
        files.WriteStory(new SyntheticStoryBuilder()
            .WriteByte(0x2000, 0xB5) // save
            .WriteByte(0x2001, 0xC2) // branch on success, offset 2
            .WriteByte(0x2002, 0xBA)); // quit after save or restore
        using StringReader saveInput = new($"{files.SavePath}\n");
        using StringWriter saveOutput = new();
        using StringWriter saveError = new();

        int saveExitCode = await CliApplication.RunAsync([files.StoryPath], saveInput, saveOutput, saveError);

        Assert.Equal(0, saveExitCode);
        Assert.True(File.Exists(files.SavePath));
        Assert.True(new FileInfo(files.SavePath).Length > 0);

        using StringReader restoreInput = new(string.Empty);
        using StringWriter restoreOutput = new();
        using StringWriter restoreError = new();

        int restoreExitCode = await CliApplication.RunAsync(
            [files.StoryPath, "--restore", files.SavePath],
            restoreInput,
            restoreOutput,
            restoreError);

        Assert.Equal(0, restoreExitCode);
        Assert.DoesNotContain("Unable to restore", restoreError.ToString());
    }

    [Fact]
    public async Task RunAsyncReportsInvalidStoryAndReturnsFailure()
    {
        using TemporaryFiles files = new();
        File.WriteAllBytes(files.StoryPath, [1, 2, 3]);
        using StringReader input = new(string.Empty);
        using StringWriter output = new();
        using StringWriter error = new();

        int exitCode = await CliApplication.RunAsync([files.StoryPath], input, output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Unable to load story", error.ToString());
    }

    private sealed class TemporaryFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"GrueGPT.Cli.Tests.{Guid.NewGuid():N}");

        public string StoryPath => Path.Combine(_directory, "synthetic.z3");

        public string SavePath => Path.Combine(_directory, "synthetic.sav");

        public TemporaryFiles() => Directory.CreateDirectory(_directory);

        public void WriteStory(SyntheticStoryBuilder builder) => File.WriteAllBytes(StoryPath, builder.Build());

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
