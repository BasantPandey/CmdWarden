using Spectre.Console;

namespace CmdWarden.Cli;

/// <summary>
/// Spectre.Console helpers for cw output. Every dynamic string goes through <see cref="E"/>.
/// Colors turn off on their own when stdout is redirected or NO_COLOR is set.
/// </summary>
public static class Ui
{
    public static string E(string? text) => Markup.Escape(text ?? "");

    public static string Ok(string text = "OK") => $"[green]{E(text)}[/]";
    public static string Warn(string text = "WARN") => $"[yellow]{E(text)}[/]";
    public static string Fail(string text = "FAIL") => $"[red]{E(text)}[/]";
    public static string Dim(string? text) => $"[grey]{E(text)}[/]";

    public static void Title(string text) =>
        AnsiConsole.Write(new Rule($"[bold]{E(text)}[/]").LeftJustified());

    public static void Line(string markup) => AnsiConsole.MarkupLine(markup);

    public static void Kv(string key, string? value) =>
        AnsiConsole.MarkupLine($"  {Dim(key + ":")} {E(value)}");

    public static Table Table(params string[] columns)
    {
        var table = new Table().Border(TableBorder.Rounded);
        foreach (var c in columns)
            table.AddColumn($"[bold]{E(c)}[/]");
        return table;
    }

    private static bool Interactive => AnsiConsole.Profile.Capabilities.Interactive;

    public static T Status<T>(string text, Func<T> work) =>
        Interactive ? AnsiConsole.Status().Spinner(Spinner.Known.Dots).Start(E(text), _ => work()) : work();

    public static Task<T> StatusAsync<T>(string text, Func<Task<T>> work) =>
        Interactive ? AnsiConsole.Status().Spinner(Spinner.Known.Dots).StartAsync(E(text), _ => work()) : work();
}
