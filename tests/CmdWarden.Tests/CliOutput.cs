using Spectre.Console;

namespace CmdWarden.Tests;

static class CliOutput
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<(int Code, string Out, string Err)> Capture(Func<Task<int>> run, int? width = 400)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var oldOut = Console.Out;
            var oldErr = Console.Error;
            var previous = AnsiConsole.Console;
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(stdout),
                Ansi = AnsiSupport.No,
                Interactive = InteractionSupport.No,
            });
            if (width is int columns)
                console.Profile.Width = columns;
            AnsiConsole.Console = console;
            Console.SetOut(stdout);
            Console.SetError(stderr);
            try
            {
                return (await run().ConfigureAwait(false), stdout.ToString(), stderr.ToString());
            }
            finally
            {
                Console.SetOut(oldOut);
                Console.SetError(oldErr);
                AnsiConsole.Console = previous;
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
