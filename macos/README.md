# Root CLI 1.0.0 (Open Beta) — macOS

Terminal agent for macOS (Apple Silicon and Intel). Uses [Ollama](https://ollama.com) for local and cloud models.

Windows installer (separate product download): [useroot.sh/downloads](https://useroot.sh/downloads).

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) (`brew install ollama` works well)
- `git` on PATH (recommended)
- zsh (default) or bash

## Build & install

```bash
cd macos
chmod +x build.sh scripts/*.sh
./build.sh
./scripts/install-rootcli-alias.sh
source ~/.zshrc
rootcli
```

Binary: `macos/src/RootCli/bin/Release/net8.0/RootCli`

## Quick start

```bash
cd ~/your-project
rootcli here
rootcli login
rootcli ask "Summarize this repo"
```

## Config

```text
~/Library/Application Support/root-cli/
```

## Publish

```bash
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
dotnet publish src/RootCli/RootCli.csproj -c Release -r osx-x64 --self-contained true -o publish/osx-x64
```

See the main [README](../README.md) for full command reference.
