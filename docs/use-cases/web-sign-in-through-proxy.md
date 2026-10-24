---
title: Let an agent sign in to a web site without seeing the password
description: The agent types cw://NAME in the password field. You approve the card. The CmdWarden proxy puts the real password in the sign-in request, so the agent and the page never get it.
---

# UC17 Let an agent sign in without the password

**When:** An AI agent drives a browser, for example with Playwright MCP or Chrome DevTools MCP. It must sign in to a web site. You do not want to give the agent your password.

**How it works:** The agent types the placeholder `cw://NAME` in the password field. The browser sends the form through the CmdWarden proxy. You get the Approval Gate card. When you approve, the proxy puts the real password in the request, and only in the field that you named. The page, the browser, and the agent hold only the placeholder.

```text
Agent ──types cw://GITHUB_WEB_PASSWORD──> Browser
Browser ──POST /session password=cw://GITHUB_WEB_PASSWORD──> CmdWarden proxy
CmdWarden proxy ──Approval Gate card──> You: Approve
CmdWarden proxy ──POST /session password=<real password>──> github.com
github.com ──session cookie──> Browser
```

## Step 1: Turn on the proxy

Do this one time. The browser uses the Windows certificate store, so it must trust the CmdWarden CA.

```powershell
cw proxy setup --trust
```

*Expect:* Windows asks you to confirm the CA. The CA goes to the root store of your user only. See [UC15](api-keys-through-proxy.md) for more about the proxy.

## Step 2: Find the sign-in path and the password field

The proxy fills one field on one path. Find them one time for each site:

1. Open the sign-in page of the site in your own browser.
2. Press **F12**, and open the **Network** tab.
3. Sign in one time.
4. Find the `POST` request that sends your password. Write down its path, for example `/session`.
5. Open the **Payload** tab of that request. Write down the name of the password field, for example `password`.

Examples:

| Site | Host | Path | Password field |
|------|------|------|----------------|
| GitHub | `github.com` | `/session` | `password` |
| A local test app | `localhost` | `/login` | `password` |

Sites change their sign-in pages. Always check the path and the field in your browser.

## Step 3: Save the password and name the field

Run these commands in your own terminal, not in the agent. The policy hook stops an agent that runs `cw proxy add`.

```powershell
cw save GITHUB_WEB_PASSWORD
cw proxy add GITHUB_WEB_PASSWORD --host github.com `
  --body /session --field password
```

*Expect:*

```text
cw://GITHUB_WEB_PASSWORD: github.com; only in /session field password
```

In Git Bash, set `MSYS_NO_PATHCONV=1` first. Git Bash changes `/session` into a Windows path.

`cw save` asks for the value, and the value goes to the vault. A key that has `--body` is a **sign-in key**. It goes only in that field, on that path, on that host. It never goes in a header or in another field.

## Step 4: Send the agent browser through the proxy

Pick the browser tool of your agent.

**Playwright MCP in Claude Code:**

```powershell
claude mcp add playwright -- `
  npx @playwright/mcp@latest --proxy-server http://127.0.0.1:47831
```

**Chrome DevTools MCP in Claude Code:**

```powershell
claude mcp add chrome-devtools -- `
  npx chrome-devtools-mcp@latest --proxyServer http://127.0.0.1:47831
```

**Cursor or Codex:** add the same `--proxy-server` or `--proxyServer` argument to the MCP server entry in the config file of the harness.

**A Chrome that you start yourself**, for an agent that attaches to it:

```powershell
& "$env:ProgramFiles\Google\Chrome\Application\chrome.exe" `
  --user-data-dir="$env:LOCALAPPDATA\CmdWarden\browser" `
  --proxy-server=http://127.0.0.1:47831 `
  --remote-debugging-port=9222
```

Chrome 136 and later needs its own `--user-data-dir` for `--remote-debugging-port`. Do not use your daily profile.

Use port `47831` unless you set another port with `cw proxy setup --port`. Run `cw proxy list` to see the port.

## Step 5: Tell the agent to use the placeholder

Give the agent a rule like this:

```text
To sign in to github.com, type my user name in the login field.
Type cw://GITHUB_WEB_PASSWORD in the password field. Then click Sign in.
Never ask me for the password.
```

*Expect:* The agent clicks **Sign in**. You get the Approval Gate card. The card shows `POST https://github.com/session` and the text "Fills GITHUB_WEB_PASSWORD in the field password and sends it to github.com". Click **Approve Once** or **Allow for session**. The site gets the real password, and the agent is signed in.

## Step 6: Fill two-factor codes (optional)

A site can ask for a code from an authenticator app after the password. CmdWarden can fill that code too. The vault keeps the TOTP key of the site, and the proxy makes the current code. The agent never sees the key.

1. Find the 2FA path and the code field, as in Step 2. Sign in one time, and find the `POST` that sends the 6-digit code.
2. Get the TOTP key of the site. When you turn on 2FA, the site shows a QR code. Most sites also show a text key, or "setup key", next to it. Some show an `otpauth://totp/...` link.
3. Save the key, and name the field, in your own terminal:

    ```powershell
    cw save GITHUB_TOTP
    cw proxy add GITHUB_TOTP --host github.com `
      --body /sessions/two-factor --field app_otp --totp
    ```

    `cw save` asks for the value. Paste the text key, for example `JBSW Y3DP EHPK 3PXP`, or the whole `otpauth://` link. The path and the field above are an example. Check them in your browser.

4. Tell the agent: "When the site asks for the 2FA code, type `cw://GITHUB_TOTP` in the code field."

*Expect:* You get the card with the text "Fills GITHUB_TOTP in the field app_otp (one-time code)". On approve, the site gets the current 6-digit code.

The proxy reads `digits`, `period`, and `algorithm` from an `otpauth://` link. Without a link, it uses 6 digits, 30 seconds, and SHA1, as most sites do.

**If the code field takes only 6 characters:** many code fields have a length limit or accept only digits, so `cw://GITHUB_TOTP` does not fit. Use a short name, for example `cw save T` and `cw://T`, which has 6 characters. A field that accepts only digits still blocks the placeholder. Then the agent must set the field value with a script and send the form.

## Step 7: Make a new account (optional)

The agent can make an account on a site, and nobody sees the password. CmdWarden makes a random password in the vault.

1. Find the sign-up path and its password fields, as in Step 2. Many sign-up forms have a second "confirm password" field.
2. Make the password, and name the fields, in your own terminal:

    ```powershell
    cw save SHOP_PASSWORD --generate
    cw proxy add SHOP_PASSWORD --host shop.example.com `
      --body /signup --field password --body /signup --field password_confirm `
      --body /login --field password
    ```

    *Expect:* `A random password of 24 characters. CmdWarden does not show it.`

3. Tell the agent: "Type `cw://SHOP_PASSWORD` in each password field."

The password has 24 characters by default. Use `--length 12` to `--length 128` to change it. The password has upper and lower case letters, digits, and symbols such as `!#%+-.:=?@_~`.

`--generate` does not replace an entry that is in the vault, because the old password is then lost. Add `--force` to replace it, for example after you change the password on the site.

To see the password, run `cw read SHOP_PASSWORD` in your own terminal. You get the card and Windows Hello first.

## What the proxy does

| The request | The result |
|-------------|------------|
| `cw://NAME` is the whole value of the named field, on the named path | The card asks. On approve, the site gets the real value. |
| `cw://NAME` in another field, for example the user name field | `403`. A site can show a user name back on the page, so the value never goes there. |
| `cw://NAME` in a header, for example `Authorization` | `403` |
| `cw://NAME` is only part of a field value | `403` |
| `cw://NAME` on a path that the key does not name | The request goes on with no change. The site gets only the placeholder. |
| A host that the key does not list | The site gets only the placeholder, or `403` in strict mode |
| A sign-in body bigger than 1 MiB | `413` |
| A `--totp` key whose vault entry is not a TOTP key | `403`, with the text "is not a TOTP key" |

More rules:

- A sign-in always shows the card, even when the policy level of the app is **Full**. You approve each sign-in.
- **Allow for session** covers the next sign-ins of the same app for the time that you pick.
- Deny, or no card (for example, the desktop is locked), sends nothing to the site.
- The proxy reads form bodies (`application/x-www-form-urlencoded`) and JSON bodies (`application/json`). A placeholder in another body type gets `403`.
- Each sign-in writes one audit row with tool `proxy` and the key name. The audit never holds the value. Run `cw audit` to see the rows.

## Limits

- After the sign-in, the browser holds the session cookie, and the agent can use it. The agent can act as you on that site until the session ends. 1Password for Agents has the same limit.
- Some sites hash or encrypt the password in the page before they send it. The site then gets a changed placeholder, and the sign-in fails.
- Some sites check the password field in the page, for example its length. They can block the sign-in before it starts.
- A site that pins its certificate does not work through the proxy.
- Codes by SMS or email, and passkeys, are not in scope. You type an SMS or email code yourself.
- The proxy uses HTTP/1.1 to the site. Chrome uses HTTP/1.1 through the proxy, and this works for most sites.

## Remove a sign-in key

```powershell
cw proxy remove GITHUB_WEB_PASSWORD
cw delete GITHUB_WEB_PASSWORD
```

Run both in your own terminal.
