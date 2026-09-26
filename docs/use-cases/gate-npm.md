# Gate npm and your npm token

**When:** An AI harness runs `npm` in your projects, and your npm token can publish packages. You want `npm publish` and token commands to ask you, and install scripts to never see the token.

```powershell
cw save NPM_TOKEN                 # paste the token once
cw harden npm                     # pin npm.cmd, install the npm pack shim
cw scan                           # find a plain token in .npmrc
```

Then write the registry line in `~/.npmrc` with a variable, not the value:

```ini
//registry.npmjs.org/:_authToken=${NPM_TOKEN}
```

*Expect:* `npm` through PATH starts the shim. The shim asks the Session Agent, and the agent puts `NPM_TOKEN` in the env of that one npm run.

| Command | Class | Token |
|---------|-------|-------|
| `npm view`, `npm ls`, `npm whoami`, `npm audit` | read | yes |
| `npm publish`, `npm deprecate`, `npm dist-tag add` | write | yes |
| `npm unpublish` | write, high risk: always asks below Full | yes |
| `npm token create`, `npm config get //…:_authToken` | secret-reveal | yes |
| `npm install`, `npm ci`, `npm update` | write | **no** |
| `npm ci --ignore-scripts`, `npm install --ignore-scripts` | write | yes |
| `npm run`, `npm test`, `npm exec` | write | **no** |

The install scripts of every dependency run with the env of npm. A bad package can read `NPM_TOKEN` there, so install and run commands get no token. For a private registry, install with `--ignore-scripts`, then run `npm rebuild` without the token.

With the default policy, your terminal (Trusted) runs read and write commands with no popup. An AI harness (Read) gets the Approval Gate for `npm publish`.

## What `cw scan` finds

- **npm.plain_secret_file** (high): a `_authToken`, `_auth`, or `_password` value in `~/.npmrc` or the project `.npmrc`. `${NPM_TOKEN}` is not a finding.
- **npm.ambient_secret_env** (high): `NPM_TOKEN` is set in the environment, so every child sees it.
- **npm.not_hardened** (medium): npm is on PATH with no pin.

## Undo

```powershell
cw unharden npm
```

See [Tool packs](../tool-packs.md) for the pack format.
