#!/usr/bin/env bash
# Build RootCli for Linux / Unix (net8.0, no WinForms).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "${ROOT}"

CONFIG="${1:-Release}"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: .NET SDK not found. Install .NET 8: https://dotnet.microsoft.com/download" >&2
  exit 1
fi

echo "Building RootCli (${CONFIG})…"
dotnet build "${ROOT}/RootCli.sln" -c "${CONFIG}"

BIN="${ROOT}/src/RootCli/bin/${CONFIG}/net8.0/RootCli"
if [[ ! -f "${BIN}" ]]; then
  echo "error: expected binary missing: ${BIN}" >&2
  exit 1
fi
chmod +x "${BIN}"

echo
echo "Built: ${BIN}"
echo "Install alias:  ./scripts/install-rootcli-alias.sh"
echo "Run:            ${BIN}"
echo "Or after alias: rootcli"
