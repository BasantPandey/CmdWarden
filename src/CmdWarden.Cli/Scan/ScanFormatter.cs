using System.Text;
using CmdWarden.Contracts;
using CmdWarden.Contracts.Scan;

namespace CmdWarden.Cli.Scan;

/// <summary>
/// Human-readable scan output without secret values.
/// </summary>
public static class ScanFormatter
{
    public static string Format(IReadOnlyList<ScanFinding> findings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{ProductInfo.Name} scan");
        sb.AppendLine($"  findings: {findings.Count}");
        sb.AppendLine();

        if (findings.Count == 0)
        {
            sb.AppendLine("  (no findings)");
            return sb.ToString();
        }

        foreach (var f in findings)
        {
            sb.AppendLine($"[{f.Severity}] {f.Id}");
            sb.AppendLine($"  tool:         {f.Tool}");
            sb.AppendLine($"  title:        {f.Title}");
            sb.AppendLine($"  summary:      {f.Summary}");
            sb.AppendLine($"  evidence:     {f.Evidence}");
            if (!string.IsNullOrWhiteSpace(f.Remediation))
                sb.AppendLine($"  remediation:  {f.Remediation}");
            if (!string.IsNullOrWhiteSpace(f.HardenHint))
                sb.AppendLine($"  harden_hint:  {f.HardenHint}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }
}
