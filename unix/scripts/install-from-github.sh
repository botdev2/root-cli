#!/usr/bin/env bash
# Compatibility wrapper. Prefer: curl -fsSL https://useroot.sh/install | bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export ROOTCLI_GITHUB_REPO="${ROOTCLI_GITHUB_REPO:-botdev2/root-cli}"
export ROOTCLI_ASSET="${ROOTCLI_ASSET:-RootCli-linux-x64.tar.gz}"
exec bash "${ROOT}/scripts/install.sh" "$@"
