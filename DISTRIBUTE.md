# Distributing Root CLI 1.0.0 (Open Beta)

## Where to upload

**GitHub Releases** on the public repo [`botdev2/root-cli`](https://github.com/botdev2/root-cli).

That is the store of truth for binaries. End users should **not** need the GitHub URL — they install via **useroot.sh** short commands below.

Desktop Windows installer (separate product): **https://useroot.sh/downloads**

---

## What users type (no GitHub link)

**Windows (CMD / PowerShell / VS Code):**

```powershell
irm https://useroot.sh/cli | iex
```

**macOS / Linux:**

```bash
curl -fsSL https://useroot.sh/install | bash
```

After install, open a project folder:

```bat
rootcli here
rootcli .
rootcli ask "what does this project do?"
```

### Wire `useroot.sh` (one-time DNS / site setup)

Point these paths at the install scripts in this repo (HTTP 302 redirect or reverse proxy):

| Public URL | Target |
|------------|--------|
| `https://useroot.sh/cli` | `https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.ps1` |
| `https://useroot.sh/install` | `https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.sh` |

Until redirects are live, the same scripts work from GitHub raw:

```powershell
irm https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.ps1 | iex
```

```bash
curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.sh | bash
```

---

## Release assets to publish

Build self-contained binaries, then attach them to a Release tag (e.g. `v1.0.0-beta`):

```bat
REM Windows (from repo root)
dotnet publish src\RootCli\RootCli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish\win-x64
```

```bash
# Linux (from unix/)
dotnet publish src/RootCli/RootCli.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux-x64
dotnet publish src/RootCli/RootCli.csproj -c Release -r linux-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/linux-arm64

# macOS (from macos/)
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/osx-arm64
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/osx-x64
```

Zip / tar names (install scripts look for these exact names):

| Asset | Contents |
|-------|----------|
| `RootCli-win-x64.zip` | `RootCli.exe` |
| `RootCli-linux-x64.tar.gz` | Linux x64 `RootCli` |
| `RootCli-linux-arm64.tar.gz` | Linux ARM64 `RootCli` |
| `RootCli-osx-arm64.tar.gz` | Apple Silicon `RootCli` |
| `RootCli-osx-x64.tar.gz` | Intel Mac `RootCli` |

Create the Release:

```bash
gh release create v1.0.0-beta \
  RootCli-win-x64.zip \
  RootCli-linux-x64.tar.gz \
  RootCli-linux-arm64.tar.gz \
  RootCli-osx-arm64.tar.gz \
  RootCli-osx-x64.tar.gz \
  --repo botdev2/root-cli \
  --title "Root CLI 1.0.0 (Open Beta)" \
  --notes "Open Beta binaries. Install: irm https://useroot.sh/cli | iex   or   curl -fsSL https://useroot.sh/install | bash"
```

---

## Later channels (optional)

| Channel | When |
|---------|------|
| **Scoop** (Windows) | After Releases exist — `scoop install rootcli` |
| **winget** | More paperwork; good once stable |
| **Homebrew** | macOS/Linux formula pointing at the same Release assets |

---

## Checklist

1. Push `main` (includes `scripts/install.ps1` + `scripts/install.sh`)
2. `dotnet publish` all platform RIDs above
3. Create Release + upload the five archives
4. On **useroot.sh**, redirect `/cli` (and optionally `/install`) to those scripts
5. Tell people only the two one-liners — not the GitHub URL
