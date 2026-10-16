namespace GrueGPT.Cli;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: GrueGPT.Cli <story-file>");
            return 2;
        }

        Console.Error.WriteLine("Story execution is not implemented yet.");
        return 2;
    }
}
