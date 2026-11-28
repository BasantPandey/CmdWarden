namespace CmdWarden.Contracts;

/// <summary><c>--project</c> on <c>cw save</c> and <c>cw delete</c>.</summary>
public static class ProjectPathOption
{
    public const string Flag = "--project";

    /// <summary>
    /// True when <paramref name="args"/> at <paramref name="index"/> is the flag.
    /// <paramref name="path"/> is the next word, or null when that word is missing or starts with <c>-</c>.
    /// </summary>
    public static bool TryTake(IReadOnlyList<string> args, ref int index, out string? path)
    {
        path = null;
        if ((uint)index >= (uint)args.Count)
            return false;
        if (!string.Equals(args[index], Flag, StringComparison.OrdinalIgnoreCase))
            return false;
        if (index + 1 < args.Count && args[index + 1].Length > 0 && args[index + 1][0] != '-')
        {
            path = args[index + 1];
            index++;
        }

        return true;
    }
}
