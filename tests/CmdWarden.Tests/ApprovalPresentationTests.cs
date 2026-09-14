using System.Text.Json;
using CmdWarden.Contracts;

namespace CmdWarden.Tests;

/// <summary>
/// Seam: IApprovalGate presentation helpers (ticket #78) — pure functions, no UI.
/// </summary>
public class ApprovalPresentationTests
{
    [Theory]
    [InlineData("Cursor", "desc", "x.exe", @"C:\app\x.exe", "Cursor")]
    [InlineData(null, "Cursor App", "x.exe", @"C:\app\x.exe", "Cursor App")]
    [InlineData("  ", "  ", "Cursor.exe", @"C:\app\Cursor.exe", "Cursor.exe")]
    [InlineData(null, null, null, @"C:\app\Cursor.exe", "Cursor.exe")]
    [InlineData(null, null, null, null, "Unknown app")]
    [InlineData("", "", "", "", "Unknown app")]
    public void Launcher_display_name_priority(
        string? product,
        string? description,
        string? fileName,
        string? path,
        string expected)
    {
        var name = ApprovalPresentation.ResolveLauncherDisplayName(product, description, fileName, path);
        Assert.Equal(expected, name);
    }

    [Fact]
    public void Launcher_display_name_never_uses_full_path_as_hero()
    {
        var path = @"C:\Users\me\AppData\Local\Programs\cursor\Cursor.exe";
        var name = ApprovalPresentation.ResolveLauncherDisplayName(null, null, null, path);
        Assert.Equal("Cursor.exe", name);
        Assert.DoesNotContain(@"C:\Users", name);
    }

    [Fact]
    public void Reason_line_with_purpose()
    {
        var line = ApprovalPresentation.BuildReasonLine("gh", "GH_TOKEN", "github.com account me");
        Assert.Equal("gh needs GH_TOKEN for github.com account me", line);
    }

    [Fact]
    public void Reason_line_without_purpose_omits_for_clause()
    {
        Assert.Equal("gh needs GH_TOKEN", ApprovalPresentation.BuildReasonLine("gh", "GH_TOKEN", null));
        Assert.Equal("gh needs GH_TOKEN", ApprovalPresentation.BuildReasonLine("gh", "GH_TOKEN", "  "));
    }

    [Fact]
    public void Reason_heading_uses_secret_name()
    {
        Assert.Equal("GH_TOKEN requested", ApprovalPresentation.BuildReasonHeading("GH_TOKEN"));
    }

    [Fact]
    public void Helper_payload_includes_card_fields_and_never_secret_values()
    {
        var request = new ApprovalRequest(
            Tool: "gh",
            CommandClass: "secret-reveal",
            PolicyLevel: "Read",
            LauncherPolicyKey: "auth:sha1:deadbeef",
            LauncherKind: "authenticode",
            LauncherPath: @"C:\Apps\Cursor.exe",
            SecretName: "GH_TOKEN",
            Purpose: "github.com account me",
            EnrollmentKind: "ai-harness",
            PolicyNote: null,
            CommandLine: "gh auth token",
            ToolPath: @"C:\Program Files\GitHub CLI\gh.exe",
            WorkingDirectory: @"C:\work\proj",
            LauncherProductName: "Cursor",
            LauncherFileDescription: "Cursor",
            LauncherFileName: "Cursor.exe",
            LauncherPublisher: "Anysphere, Inc.",
            RequestedAt: new DateTimeOffset(2026, 8, 3, 10, 15, 42, TimeSpan.Zero));

        var payload = ApprovalPresentation.ToHelperPayload(request);
        Assert.Equal(ProductInfo.Name, payload.WindowTitle);
        Assert.Equal("Cursor", payload.LauncherDisplayName);
        Assert.Equal(ApprovalPresentation.SubtitleWantsToRun, payload.Subtitle);
        Assert.Equal("gh auth token", payload.CommandLine);
        Assert.Equal(@"C:\Program Files\GitHub CLI\gh.exe", payload.ToolPath);
        Assert.Equal(@"C:\work\proj", payload.WorkingDirectory);
        Assert.Equal(new[] { "GH_TOKEN" }, payload.SecretNames);
        Assert.Equal("GH_TOKEN requested", payload.ReasonHeading);
        Assert.Equal("gh needs GH_TOKEN for github.com account me", payload.ReasonLine);
        Assert.Equal("ai-harness", payload.EnrollmentKind);
        Assert.Equal("authenticode", payload.IdentityKind);
        Assert.Equal("Anysphere, Inc.", payload.Publisher);
        Assert.Equal(@"C:\Apps\Cursor.exe", payload.LauncherPath);
        Assert.Equal("Read", payload.PolicyLevel);
        Assert.Equal("secret-reveal", payload.CommandClass);
        Assert.Equal("auth:sha1:deadbeef", payload.PolicyKey);
        Assert.Equal(request.RequestedAt, payload.RequestedAt);

        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("GH_TOKEN", json);
        Assert.DoesNotContain("gho_", json);
        Assert.DoesNotContain("\"Value\"", json);
        Assert.DoesNotContain("\"SecretValue\"", json);
        Assert.DoesNotContain("secret_value", json);
    }

    [Fact]
    public void Helper_payload_unknown_launcher_display_name()
    {
        var request = new ApprovalRequest(
            Tool: "gh",
            CommandClass: "read",
            PolicyLevel: "Deny",
            LauncherPolicyKey: "unknown",
            LauncherKind: "unknown",
            LauncherPath: null,
            SecretName: "GH_TOKEN",
            Purpose: null,
            EnrollmentKind: "unknown",
            PolicyNote: null);

        var payload = ApprovalPresentation.ToHelperPayload(request);
        Assert.Equal("Unknown app", payload.LauncherDisplayName);
        Assert.Equal("gh needs GH_TOKEN", payload.ReasonLine);
    }
}
