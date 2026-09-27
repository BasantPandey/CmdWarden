# Release signing

The release workflow signs every CmdWarden exe and dll, and the two install scripts ([#42](https://github.com/BasantPandey/CmdWarden/issues/42)). A signed file gets fewer SmartScreen warnings. The Session Agent also checks the signer of the Approval Gate.

## What the release signs

`scripts/Release-Signing.ps1` selects each file whose company name is `CmdWarden`. `Directory.Build.props` sets that name for every project. Third-party files, such as `Google.Protobuf.dll` and the gRPC files, keep the signature of their owner.

The release signs these files:

- The portable folder of the win-x64 zip and the setup zip: `cw.exe`, `cmdwarden.exe`, `agent\`, `shim-payload\`, `secrets-manager\`, `try-agent\`. The .NET files in `runtime\` keep the signature of Microsoft.
- The same files inside the nupkg. The script replaces each entry in place, so the rest of the package stays the same.
- `Install-CmdWarden.ps1` and `Uninstall-CmdWarden.ps1` in the setup zip. `install.cmd` and `uninstall.cmd` cannot carry a signature.

The workflow order is: pack, build the portable folder, stage, sign, put back, verify, smoke test, push the nupkg, then zip. The verify step fails the release when one staged file has no valid signature. On a signed release, the smoke test also checks each CmdWarden exe of the portable folder.

## Set up signing

Use one of the three ways. When more than one is set, the workflow uses SignPath first, then Azure Artifact Signing.

### SignPath Foundation (free for open source)

1. Apply at [signpath.org](https://signpath.org) for the CmdWarden project. SignPath Foundation gives open-source projects a code signing certificate at no cost.
2. In SignPath, add a project with an artifact configuration that signs `**/*.exe`, `**/*.dll`, and `**/*.ps1` in the uploaded zip, and a signing policy.
3. Add the repository secret `SIGNPATH_API_TOKEN` and the repository variables `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`, and `SIGNPATH_SIGNING_POLICY_SLUG`.

The workflow uploads `artifacts/to-sign/files` as a build artifact, asks SignPath to sign it, waits, and puts the signed files back in the same folder.

### Azure Artifact Signing (Trusted Signing)

1. Create an Artifact Signing account and a certificate profile in Azure. Complete the identity validation.
2. Create an app registration. Give it the role **Artifact Signing Certificate Profile Signer** on the account.
3. Add these repository secrets:

| Secret | Value |
|--------|-------|
| `ARTIFACT_SIGNING_ENDPOINT` | The account endpoint, for example `https://eus.codesigning.azure.net/` |
| `ARTIFACT_SIGNING_ACCOUNT` | The account name |
| `ARTIFACT_SIGNING_PROFILE` | The certificate profile name |
| `AZURE_TENANT_ID` | The tenant ID of the app registration |
| `AZURE_CLIENT_ID` | The client ID of the app registration |
| `AZURE_CLIENT_SECRET` | A client secret of the app registration |

### A code signing certificate

1. Export the certificate with its private key as a PFX file.
2. Add these repository secrets:

| Secret | Value |
|--------|-------|
| `CODE_SIGNING_PFX` | The PFX file as Base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))` |
| `CODE_SIGNING_PASSWORD` | The PFX password |

The workflow signs with `signtool` from the Windows SDK and uses the DigiCert timestamp server.

### Require signing

Without a signing secret, the release still builds and shows a warning. Set the repository variable `REQUIRE_SIGNING` to `true` to fail such a release.

## The signer pin in the Session Agent

The Session Agent reads the signer of its own `CmdWarden.Agent.dll`. When that file has a valid signature, the agent starts only an Approval Gate with the same signer. A changed or foreign-signed `CmdWarden.ApprovalGate.exe` gets no start, and the request fails closed. A developer build has no signature, so it has no pin.

Every signature check uses `WinVerifyTrust`. The check makes sure that the file hash matches the signature. It checks no revocation and uses no network. Launcher identity and tool pins use the same check, so a signature block copied onto another exe does not give that exe the policy key of the real publisher.

## Test the script locally

```powershell
dotnet pack src/CmdWarden.Cli/CmdWarden.Cli.csproj -c Release -o out/nupkg -p:Version=0.0.1-test
dotnet publish src/CmdWarden.Cli/CmdWarden.Cli.csproj -c Release -o out/bin -p:PublishSingleFile=false
./scripts/Release-Signing.ps1 -Stage -Bin out/bin -Nupkg out/nupkg/CmdWarden.0.0.1-test.nupkg -Out out/to-sign
# Sign the files in out/to-sign/files, for example with Set-AuthenticodeSignature and a test certificate.
./scripts/Release-Signing.ps1 -Apply -Out out/to-sign
```

`-Verify` accepts only a signature that chains to a trusted root, so a self-signed test certificate fails it.
