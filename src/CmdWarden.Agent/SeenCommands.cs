namespace CmdWarden.Agent;

/// <summary>
/// The commands each launcher ran through a gate (#35), for the "first use" signal on the Approval
/// Gate. One line per launcher key, tool, and verb in commands-seen.txt; no arguments, no values.
/// ponytail: the file grows by one line per new verb and launcher; trim it when it passes 5000 lines.
/// </summary>
public sealed class SeenCommands
{
    private const int MaxLines = 5000;
    private readonly string _path;
    private readonly object _gate = new();
    private HashSet<string>? _seen;

    public SeenCommands(string productRoot) => _path = Path.Combine(productRoot, "commands-seen.txt");

    private static string Key(string launcherKey, string tool, string verb) =>
        string.Join('\t', launcherKey.ToLowerInvariant(), tool, verb.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '));

    public bool Contains(string launcherKey, string tool, string verb)
    {
        lock (_gate)
            return Load().Contains(Key(launcherKey, tool, verb));
    }

    public void Mark(string launcherKey, string tool, string verb)
    {
        if (verb.Length == 0)
            return;
        var key = Key(launcherKey, tool, verb);
        lock (_gate)
        {
            if (!Load().Add(key))
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                if (_seen!.Count > MaxLines)
                {
                    _seen = [key];
                    File.WriteAllText(_path, key + Environment.NewLine);
                }
                else
                {
                    File.AppendAllText(_path, key + Environment.NewLine);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A lost line only shows "first use" once more.
            }
        }
    }

    private HashSet<string> Load()
    {
        if (_seen is not null)
            return _seen;
        _seen = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(_path))
                _seen.UnionWith(File.ReadAllLines(_path).Where(l => l.Length > 0));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable file only shows "first use" again.
        }
        return _seen;
    }
}
