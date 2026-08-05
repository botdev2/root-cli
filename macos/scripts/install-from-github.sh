#!/usr/bin/env bash
# Compatibility wrapper. Prefer: curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.sh | bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
export ROOTCLI_GITHUB_REPO="${ROOTCLI_GITHUB_REPO:-botdev2/root-cli}"
# Asset is auto-detected in scripts/install.sh for darwin.
exec bash "${ROOT}/scripts/install.sh" "$@"
