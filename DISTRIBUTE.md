# Distributing Root CLI 1.0.0 (Open Beta)

## Windows installer

For end users on Windows, prefer the product download page:

**https://useroot.sh/downloads**

## GitHub Releases (CLI binaries)

Create a public GitHub repo, push this project, then attach platform binaries to a **Release**. That is the usual way people install the CLI from CMD / PowerShell / bash with one command.

### Why GitHub Releases

- Works from any CMD / PowerShell / VS Code terminal via `irm` / `curl`
- Versioned tags (`v0.1.0`)
- Free for public repos
- Later you can add Scoop / winget / Homebrew on top of the same assets

Other options (later):

| Channel | When |
|---------|------|
| **Scoop** (Windows) | After Releases exist — one-line `scoop install` |
| **winget** | More paperwork; good once stable |
| **Homebrew** | macOS/Linux later |
| npm / pip | Possible but odd for a .NET native binary |

---

## Release assets to publish

Build on each OS (or use `dotnet publish`):

```bat
REM Windows (from repo root)
dotnet publish src\RootCli\RootCli.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64
```

```bash
# Linux (from unix/)
dotnet publish src/RootCli/RootCli.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64

# macOS Apple Silicon (from macos/)
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64

# macOS Intel
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64
```

Zip / tar and name consistently:

| Asset | Contents |
|-------|----------|
| `RootCli-win-x64.zip` | `RootCli.exe` (+ deps if not single-file) |
| `RootCli-linux-x64.tar.gz` | Linux `RootCli` binary |
| `RootCli-osx-arm64.tar.gz` | Apple Silicon `RootCli` |
| `RootCli-osx-x64.tar.gz` | Intel Mac `RootCli` |

Optional single-file:

```bat
dotnet publish ... -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

---

## One-line install (after you publish)

Replace `botdev2/root-cli` in the scripts with your real repo.

**Windows (CMD / PowerShell / VS Code):**

```powershell
irm https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install-from-github.ps1 | iex
```

Or clone then:

```powershell
.\scripts\install-from-github.ps1 -Repo botdev2/root-cli
```

**Linux:**

```bash
curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/unix/scripts/install-from-github.sh | bash
```

Set the real repo in the script defaults, or:

```bash
ROOTCLI_GITHUB_REPO=botdev2/root-cli bash unix/scripts/install-from-github.sh
```

---

## Open Root in the current folder (VS Code)

After `rootcli` is on PATH:

```bat
cd your-project
rootcli here
```

Same:

```bat
rootcli .
rootcli open
```

Ask/plan/agent default to the current folder if you omit `-r`:

```bat
rootcli ask "what does this project do?"
rootcli agent "add a README section"
```

---

## Checklist

1. Create GitHub repo and push
2. `dotnet publish` Windows + Linux self-contained builds
3. Create Release `v0.1.0` and upload zip/tar assets
4. Edit `botdev2/root-cli` in install scripts to your repo
5. Tell people the one-liner above
