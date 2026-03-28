---
name: publish
description: Bump version, build, and publish ProdToy to C:\Publish\ProdToy with metadata.json
user-invocable: true
disable-model-invocation: true
allowed-tools: Bash, Read, Edit
---

# Publish ProdToy

Bump the version, publish a single-file executable, and deploy to `C:\Publish\ProdToy`.

## Steps

1. **Bump version** — Read `src/ProdToy.Win/Core/AppVersion.cs`, increment the patch number (third segment), and save the file.

2. **Publish** — Run the publish script which builds, generates metadata, and deploys:
   ```bash
   powershell -ExecutionPolicy Bypass -File publish.ps1 -DeployPath "C:\Publish\ProdToy"
   ```

3. **Verify** — Confirm both files exist at the deploy path and print a summary:
   - `C:\Publish\ProdToy\ProdToy.exe` (print file size)
   - `C:\Publish\ProdToy\metadata.json` (print contents to confirm version and timestamp)

## Notes

- The publish script (`publish.ps1`) handles version bumping, `dotnet publish`, metadata.json generation, and copying to the deploy path automatically.
- If the user provides release notes, pass them via `-ReleaseNotes "..."`. Otherwise the default is used.
- Do NOT commit or push after publishing unless the user asks.
