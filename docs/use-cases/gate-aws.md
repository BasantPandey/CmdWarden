# Gate the AWS CLI

**When:** An AI harness runs `aws`, and you want commands that print keys or delete resources to ask you. Optional: keep your access keys in the vault, not in `~/.aws/credentials`.

```powershell
cw harden aws
cw scan                           # find a plain key in ~/.aws/credentials
```

Optional, to move static keys into the vault:

```powershell
cw save AWS_ACCESS_KEY_ID
cw save AWS_SECRET_ACCESS_KEY
# then delete the two lines from ~/.aws/credentials
```

*Expect:* `aws` through PATH starts the shim. On an allowed run, each of `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, and `AWS_SESSION_TOKEN` that is in the vault goes into the env of that one run. With no vault keys, the shim gates the run and aws uses its own profiles or `aws sso login`.

| Command | Class |
|---------|-------|
| `aws sts get-session-token`, `aws sts assume-role` | secret-reveal |
| `aws configure get`, `aws configure export-credentials` | secret-reveal |
| `aws secretsmanager get-secret-value` | secret-reveal |
| `aws ssm get-parameter … --with-decryption` | secret-reveal |
| `aws iam create-access-key`, `aws ecr get-login-password`, `aws eks get-token` | secret-reveal |
| `aws s3 presign`, `aws kms decrypt` | secret-reveal |
| `aws <service> list-*`, `describe-*`, `get-*`, `aws s3 ls` | read |
| `aws <service> delete-*`, `terminate-*`, `aws s3 rb`, `aws s3 rm --recursive` | write, high risk: always asks below Full |
| other `aws <service> <command>` | write |

## What `cw scan` finds

- **aws.plain_secret_file** (high): an `aws_secret_access_key` line in `~/.aws/credentials`.
- **aws.ambient_secret_env** (high): an AWS key variable is set in the environment.
- **aws.not_hardened** (medium): aws is on PATH with no pin.

## Undo

```powershell
cw unharden aws
```

See [Tool packs](../tool-packs.md) for the pack format.
