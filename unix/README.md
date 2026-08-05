# Root CLI 1.0.0 (Open Beta) — Linux

Terminal agent for Linux. Uses [Ollama](https://ollama.com) for local and cloud models.

## Install

```bash
curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.sh | bash
```

Desktop Windows app: [useroot.sh/downloads](https://useroot.sh/downloads).

## Requirements (build from source)

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com)
- `git` on PATH (recommended)
- bash or zsh

## Build from source

```bash
cd unix
chmod +x build.sh scripts/*.sh
./build.sh
./scripts/install-rootcli-alias.sh
source ~/.bashrc
rootcli
```

Binary: `unix/src/RootCli/bin/Release/net8.0/RootCli`

## Quick start

```bash
cd ~/your-project
rootcli here
rootcli login
rootcli ask "Summarize this repo"
```

## Config

```text
~/.local/share/root-cli/
```

See the main [README](../README.md) for full command reference.
