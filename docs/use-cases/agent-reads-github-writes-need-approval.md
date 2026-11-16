# Let the agent read GitHub, gate writes and token export

When: AI harness is enrolled as `ai-harness` (default **Read**). Safe read `gh` may auto-allow; secret-reveal / write should show the same Approval Gate popup as [Read the secret mid-session](approve-secret-read-mid-session.md#middle-of-the-ai-session-agent-tries-to-read-that-secret).

```powershell
cw policy list
cw whoami
cw policy set <harnessPolicyKey> gh Read
```

Then in the AI product, ask the agent to run e.g. `gh auth token` or `gh pr create …`. Answer **Deny** or **Approve Once** on the desktop card.

*Expect:* read-class may auto-allow under Read; write / secret-reveal → Approval Gate or block.

If everything always prompts: check enrollment (`cw whoami`, `cw policy list`) - unenrolled launchers never auto-allow.
