# Add and remove secrets in CmdWarden Vault

**When:** You want to store a named secret, or remove one, without the terminal.

1. Open **CmdWarden Vault** from the Start Menu or the Desktop icon. If both are missing, run `cw shortcut install --desktop` once.
2. The app opens on the **Secrets** page. The badge next to the title shows the Session Agent state.
3. Click **+ Add secret**. Type the **Name** and the **Value**. Click **Save**.
4. The new name appears as a card. The value is never shown again.
5. To delete: click the card to select it, then click **Delete** on that card. Confirm with **Delete** in the dialog.

![Secrets page with DEMO_TOKEN and other secret names](../images/vault-secrets.png)

*Expect:* the same Credential Manager target as `cw save` (`CmdWarden/secret/<NAME>`). The list refreshes on its own every few seconds. A yellow banner **Session Agent not running - actions disabled** means the agent is down; run `cw doctor` in a terminal. A banner that says **denied access** means the agent runs elevated. Stop it from an admin shell, then start it from a normal shell.

Saving in the Vault does **not** show the Approval Gate. The gate appears only when a launcher tries to **release** the secret ([UC2 Part 3](approve-secret-read-mid-session.md#part-3-middle-of-the-ai-session-agent-tries-to-read-that-secret-popup)).
