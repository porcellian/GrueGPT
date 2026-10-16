namespace GrueGPT.Cli;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            return await CliApplication.RunAsync(
                args,
                Console.In,
                Console.Out,
                Console.Error,
                cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }
}
