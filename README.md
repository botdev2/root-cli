# Root CLI 1.0.0 (Open Beta)

**Root CLI** is a terminal agent for your projects. Chat with local or cloud [Ollama](https://ollama.com) models, inspect and edit a repository, run shell commands (with approval), use git/GitHub helpers, and connect MCP tools — from Windows, macOS, or Linux.

This is the **1.0.0 Open Beta**. APIs, menus, and install paths may still change.

---

## Downloads

| Platform | How to get Root CLI |
|----------|---------------------|
| **Windows** | Preferred: desktop installer from **[useroot.sh/downloads](https://useroot.sh/downloads)**. Or build/install the CLI from this repo (below). |
| **macOS** | Build from [`macos/`](macos/) or install a Release binary (see [DISTRIBUTE.md](DISTRIBUTE.md)). |
| **Linux** | Build from [`unix/`](unix/) or install a Release binary (see [DISTRIBUTE.md](DISTRIBUTE.md)). |

Windows users who already use the Root desktop app should start at **[useroot.sh/downloads](https://useroot.sh/downloads)**.

---

## What you can do

- **Ask** — read-only Q&A over your repo  
- **Plan** — read-only numbered implementation plan  
- **Agent** — edit files, run commands (approve with `y` / `n` / `ay`), git, PRs, MCP  
- **Chats** — saved sessions you can reopen  
- **Models** — pick any local or signed-in Ollama cloud model  
- **Login** — `rootcli login` runs `ollama signin` for cloud models  

In chat: `g` = agent, `p` = plan, `q` = ask.

---

## Requirements (all platforms)

1. **[.NET 8 SDK](https://dotnet.microsoft.com/download)** (to build from source)  
2. **[Ollama](https://ollama.com)** installed and running  
3. At least one model (`ollama pull …`) — or sign in for cloud models  
4. Optional: `git`, `gh`, MCP servers such as `codebase-memory-mcp`

---

## Windows

### Install (recommended)

Get the Windows installer from:

**https://useroot.sh/downloads**

### Build the CLI from this repo

```bat
dotnet build RootCli.sln -c Release
```

Binary:

```text
src\RootCli\bin\Release\net8.0-windows\RootCli.exe
```

Each build refreshes a `rootcli` alias (PowerShell profile + `%LOCALAPPDATA%\root-cli\bin`). Open a **new** terminal, then:

```bat
rootcli
rootcli here
rootcli login
```

### Config (Windows)

```text
%LOCALAPPDATA%\root-cli\
  sessions\
  mcp-servers.json
  recent-repos.json
  github-token.txt
```

### VS Code / current folder

```bat
cd your-project
rootcli here
rootcli ask "What does this project do?"
```

---

## macOS

### Build

```bash
cd macos
chmod +x build.sh scripts/*.sh
./build.sh
./scripts/install-rootcli-alias.sh
source ~/.zshrc   # or ~/.bashrc
rootcli
```

Binary:

```text
macos/src/RootCli/bin/Release/net8.0/RootCli
```

Publish (Apple Silicon / Intel):

```bash
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64
```

### Config (macOS)

```text
~/Library/Application Support/root-cli/
```

### Notes

- Repo picker is console-based (browse home/Documents or paste `~/…`).  
- Shell tools use `$SHELL` / bash / zsh.  
- Homebrew Ollama: `brew install ollama`  
- More detail: [macos/README.md](macos/README.md)

---

## Linux

### Build

```bash
cd unix
chmod +x build.sh scripts/*.sh
./build.sh
./scripts/install-rootcli-alias.sh
source ~/.bashrc   # or ~/.zshrc
rootcli
```

Binary:

```text
unix/src/RootCli/bin/Release/net8.0/RootCli
```

Publish:

```bash
dotnet publish src/RootCli/RootCli.csproj -c Release -r linux-x64 --self-contained true -o publish/linux-x64
```

### Config (Linux)

```text
~/.local/share/root-cli/
  sessions/
  mcp-servers.json
  recent-repos.json
  github-token.txt
```

### Notes

- Works on typical distros (Debian/Ubuntu, Fedora, Arch, …) with .NET 8.  
- `run_command` uses `$SHELL`, then bash, then `sh`.  
- System paths like `/`, `/etc`, `/usr` cannot be used as the project root.  
- More detail: [unix/README.md](unix/README.md)

---

## Commands (all platforms)

```text
rootcli                 Interactive menu
rootcli here            Open menu for the current folder
rootcli .               Same as here
rootcli login           ollama signin (cloud models)
rootcli logout          ollama signout
rootcli ollama login    Same as login
rootcli models          List Ollama models
rootcli ask "…"         Read-only chat (defaults to cwd)
rootcli plan "…"        Read-only plan
rootcli agent "…"       Full agent
rootcli tools           List tools
rootcli mcp             MCP status
rootcli --help          Help
```

### Flags

| Flag | Meaning |
|------|---------|
| `-r`, `--repo` | Project folder (default: current directory for ask/plan/agent) |
| `-m`, `--model` | Ollama model (`ROOTCLI_MODEL` if unset) |
| `--yes` | Pref-approve non-high-risk tools |
| `--no-mcp` | Disable MCP for this run |
| `--max-steps N` | Agent loop cap (default 12) |
| `--json` | Machine-readable output where supported |

### Approvals

When a tool needs permission: **`y`** = once, **`n`** = deny, **`ay`** = always yes for the rest of the session.

---

## Modes

| Mode | Behavior |
|------|----------|
| **Ask** | Read-only answers from the repo |
| **Plan** | Read-only numbered plan, then stop |
| **Agent** | Edits, shell, git write, PRs, mutating MCP |

---

## Cloud models (Ollama)

```text
rootcli login
```

This runs `ollama signin` and opens the browser so you can authorize cloud models. Then pick a cloud model in the menu or with `-m`.

```text
rootcli logout
```

---

## MCP

Config file is created under the platform config folder (`mcp-servers.json`).

Default server: **codebase-memory** (optional). Resolve order:

1. `ROOTCLI_MCP_CODEBASE_MEMORY`  
2. Platform tools / PATH (`codebase-memory-mcp`)  

---

## Environment

| Variable | Purpose |
|----------|---------|
| `OLLAMA_HOST` | Ollama URL (default `http://localhost:11434`) |
| `ROOTCLI_MODEL` | Default model |
| `ROOTCLI_REPO` | Default project path |
| `ROOTCLI_GITHUB_TOKEN` / `GITHUB_TOKEN` | GitHub PAT |
| `ROOTCLI_MCP_CODEBASE_MEMORY` | Path to MCP binary |

---

## Repository layout

```text
src/RootCli/           Windows host
src/RootCli.Core/      Shared agent, tools, Ollama, MCP, git
unix/                  Linux host
macos/                 macOS host
scripts/               Windows install helpers
DISTRIBUTE.md          Release / publish notes
```

---

## Open Beta

Root CLI **1.0.0 (Open Beta)** — thank you for trying it. Report issues on the GitHub repo. For the Windows desktop installer and product downloads, use **[useroot.sh/downloads](https://useroot.sh/downloads)**.
