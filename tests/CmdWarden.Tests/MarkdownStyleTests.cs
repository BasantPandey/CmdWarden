using System.Text.RegularExpressions;

namespace CmdWarden.Tests;

public class MarkdownStyleTests
{
    private static readonly Regex Heading = new(
        @"^[ ]{0,3}(#{1,6})\s+(.+?)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex NumberedHeading = new(
        @"^(?:UC\d+\b|\d+[\.)]\s+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex InlineCode = new(@"`[^`\n]*`", RegexOptions.Compiled);

    private static readonly Regex Image = new(@"!\[[^\]]*\]\([^)]*\)", RegexOptions.Compiled);

    private static readonly Regex Link = new(@"(?<!!)\[([^\]]*)\]\([^)]*\)", RegexOptions.Compiled);

    private static readonly Regex AutoLink = new(@"<https?://[^>]+>", RegexOptions.Compiled);

    private static readonly Regex BareUrl = new(@"https?://", RegexOptions.Compiled);

    [Fact]
    public void Style_guide_names_the_google_rules()
    {
        var text = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "style-guide.md"));

        Assert.Contains("you", text, StringComparison.Ordinal);
        Assert.Contains("active voice", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sentence case", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one H1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("numbered list", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bold only for a UI label", text, StringComparison.Ordinal);
        Assert.Contains("code font", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("link text that names the target", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Spell the month", text, StringComparison.Ordinal);
        Assert.Contains(
            "[Google developer documentation style guide](https://developers.google.com/style)",
            text,
            StringComparison.Ordinal);
        Assert.Contains("fenced command", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code span", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quoted social post", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Markdown_files_follow_the_docs_style_guide()
    {
        var failures = new List<string>();
        foreach (var path in MarkdownFiles())
            CheckFile(path, failures);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Social_posts_keep_the_plain_docs_address()
    {
        const string plain = "> https://basantpandey.github.io/CmdWarden/";
        var linkedin = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "plan", "linkedin-post.md"));
        var thread = File.ReadAllText(Path.Combine(TestPaths.RepoRoot, "docs", "plan", "x-thread.md"));

        Assert.Contains(plain, linkedin, StringComparison.Ordinal);
        Assert.Contains(plain, thread, StringComparison.Ordinal);
    }

    private static IEnumerable<string> MarkdownFiles()
    {
        var root = TestPaths.RepoRoot;
        foreach (var path in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Any(part => part is "bin" or "obj" or ".git"))
                continue;

            yield return path;
        }
    }

    private static void CheckFile(string path, List<string> failures)
    {
        var relative = Path.GetRelativePath(TestPaths.RepoRoot, path);
        var lines = File.ReadAllText(path).Replace("\r\n", "\n").Split('\n');
        var inFence = false;
        var headings = new List<(int Number, int Level, string Title)>();

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            var number = index + 1;
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }

            if (inFence)
                continue;

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                var title = heading.Groups[2].Value.Trim();
                headings.Add((number, level, title));
                if (NumberedHeading.IsMatch(title))
                    failures.Add($"{relative}:{number}: numbered heading {title}");
                if (title.Contains("](", StringComparison.Ordinal))
                    failures.Add($"{relative}:{number}: link in heading {title}");
            }

            foreach (Match match in Link.Matches(line))
            {
                var label = match.Groups[1].Value.Trim();
                if (label.Equals("here", StringComparison.OrdinalIgnoreCase)
                    || label.Equals("click here", StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{relative}:{number}: bad link text {label}");
                }
            }

            if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                continue;

            var plain = AutoLink.Replace(Link.Replace(Image.Replace(InlineCode.Replace(line, ""), ""), ""), "");
            if (BareUrl.IsMatch(plain))
                failures.Add($"{relative}:{number}: bare url {plain.Trim()}");
        }

        var h1Count = headings.Count(item => item.Level == 1);
        if (h1Count != 1)
            failures.Add($"{relative}: H1 count {h1Count}");

        var previous = 0;
        foreach (var heading in headings)
        {
            if (previous > 0 && heading.Level > previous + 1)
                failures.Add($"{relative}:{heading.Number}: skip {previous}->{heading.Level} {heading.Title}");
            previous = heading.Level;
        }
    }
}
