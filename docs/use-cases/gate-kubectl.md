# Gate kubectl

**When:** An AI harness runs `kubectl` against your clusters. You want it to read pods and logs freely, but ask you before it reads Secret objects, prints the raw kubeconfig, or deletes something.

```powershell
cw harden kubectl
cw scan                           # find a static token or key in ~/.kube/config
```

*Expect:* `kubectl` through PATH starts the shim. kubectl keeps its own kubeconfig; the shim gates the run.

| Command | Class |
|---------|-------|
| `kubectl get secret …`, `kubectl get pods,secrets`, `kubectl describe secret …` | secret-reveal |
| `kubectl config view --raw`, `kubectl create token …` | secret-reveal |
| `kubectl exec`, `cp`, `attach`, `debug`, `port-forward`, `proxy`, `run` | unknown: asks below Full |
| `kubectl delete`, `kubectl drain` | write, high risk: always asks below Full |
| `kubectl get`, `describe`, `logs`, `top`, `config view`, `auth can-i` | read |
| `kubectl apply`, `create`, `patch`, `scale`, `rollout restart`, `config use-context` | write |

A shell in a pod can read every secret that the pod mounts, so `exec` and its kin are `unknown`, not `read`.

## What `cw scan` finds

- **kubectl.plain_secret_file** (high): a `token`, `client-key-data`, or `password` line in `~/.kube/config`. An exec credential plugin (for example `kubelogin`) is safer than a static token.
- **kubectl.not_hardened** (medium): kubectl is on PATH with no pin.

## Undo

```powershell
cw unharden kubectl
```

See [Tool packs](../tool-packs.md) for the pack format.
