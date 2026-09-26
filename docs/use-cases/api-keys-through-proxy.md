# Keep API keys out of the agent

**When:** An AI harness calls an API with curl, Python, or Node, for example the OpenAI API. The SDK reads the key from the environment, so the agent can print it or send it anywhere. You want the agent to use a placeholder. The real key must go only to the API host, after the policy allows it.

```powershell
cw proxy setup                                        # make the per-user CA, turn the proxy on
cw save OPENAI_API_KEY                                # the real key goes to the vault
cw proxy add OPENAI_API_KEY --host api.openai.com     # the hosts that may get the key
cw launch codex                                       # the harness sends HTTPS through the proxy
```

In the harness, use the placeholder as the key:

```powershell
$env:OPENAI_API_KEY = "cw://OPENAI_API_KEY"
curl.exe https://api.openai.com/v1/models -H "Authorization: Bearer cw://OPENAI_API_KEY"
```

*Expect:* The Session Agent runs the proxy on `127.0.0.1:47831`. For a host that a key lists, the proxy opens the TLS connection with a certificate from your CA. It finds `cw://NAME` in the request line and the headers. It asks the launcher policy, and puts the vault value in place. The API gets the real key. The agent sees only the placeholder.

- A `GET`, `HEAD`, or `OPTIONS` request is a read. Other methods are a write. An AI harness at level Read gets the Approval Gate for a write. The card shows the method, the URL, and the key names.
- A placeholder for a listed host that its key does not list gets `403`.
- A placeholder that no key names gets `403`, with the `cw proxy add` command to fix it.
- The audit shows one row per request with tool `proxy`. The key value never goes to the audit.

## What cw launch sets

With the proxy on, `cw launch` gives the harness these variables:

| Variable | Value | For |
|---|---|---|
| `HTTPS_PROXY`, `HTTP_PROXY` | `http://127.0.0.1:47831` | Most tools and SDKs |
| `NO_PROXY` | `localhost,127.0.0.1,::1` | Local servers |
| `NODE_USE_ENV_PROXY` | `1` | Node `fetch` and `https` |
| `NODE_EXTRA_CA_CERTS` | `ca.pem` | Node |
| `SSL_CERT_FILE`, `REQUESTS_CA_BUNDLE`, `CURL_CA_BUNDLE` | `ca-bundle.pem` | Python, OpenSSL, and Git Bash curl |

`ca-bundle.pem` holds the Windows root certificates and your CA. A client that uses it still trusts the hosts that the proxy passes through.

## Windows curl.exe

`C:\Windows\System32\curl.exe` uses Schannel. It ignores `CURL_CA_BUNDLE`. Trust the CA for your Windows user:

```powershell
cw proxy setup --trust
```

Windows asks you to confirm. The CA goes to the root store of your user, not of the PC. No admin right is necessary. Other apps of your user that use the Windows store then trust the CA too.

The proxy serves an empty revocation list (CRL) of the CA. Schannel checks it, so you do not need `--ssl-no-revoke`.

## Other hosts

A host that no key lists passes through untouched. The proxy does not open its TLS. A placeholder in that traffic stays a placeholder, so the host never gets the key. To block every host that no key lists:

```powershell
cw proxy strict on
```

Then a call to such a host gets `403`. Use it when the agent must only reach the listed APIs.

## Your CA

- `cw proxy setup` makes the CA in `CurrentUser\My`. The private key cannot be exported.
- The CA is valid for 10 years. Each host certificate is valid for 30 days, and the proxy makes a new one before the end.
- The proxy accepts connections only from processes of your Windows user.
- `cw proxy list` shows the port, the CA, the keys, and their hosts.

## Limits

- The proxy reads placeholders in the request line and the headers only. A placeholder in a request body does not change.
- The proxy speaks HTTP/1.1 to the API. WebSocket upgrades pass through after the check of the first request.

## Undo

```powershell
cw proxy remove OPENAI_API_KEY    # stop using one key in the proxy
cw proxy uninstall                # remove the CA and the proxy config
```

`cw proxy uninstall` removes the CA from your user stores and turns the proxy off. If you trusted the CA, Windows asks you to confirm. The keys stay in the vault. Start the harness again with `cw launch` to clear the proxy variables.
