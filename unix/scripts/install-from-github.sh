#!/usr/bin/env bash
# Download RootCli from GitHub Releases and install ~/.local/bin/rootcli
set -euo pipefail

REPO="${ROOTCLI_GITHUB_REPO:-botdev2/root-cli}"
TAG="${1:-latest}"
ASSET="${ROOTCLI_ASSET:-RootCli-linux-x64.tar.gz}"

API="https://api.github.com/repos/${REPO}/releases/${TAG}"
if [[ "${TAG}" == "latest" ]]; then
  API="https://api.github.com/repos/${REPO}/releases/latest"
else
  API="https://api.github.com/repos/${REPO}/releases/tags/${TAG}"
fi

echo "Fetching ${API} …"
URL="$(curl -fsSL "${API}" | python3 -c "
import json,sys
r=json.load(sys.stdin)
asset='${ASSET}'
for a in r.get('assets',[]):
  if a.get('name')==asset:
    print(a['browser_download_url']); break
else:
  names=', '.join(a.get('name','') for a in r.get('assets',[]))
  sys.stderr.write('Asset not found: '+asset+' (have: '+names+')\n'); sys.exit(1)
")"

DEST="${HOME}/.local/share/root-cli/app"
TMP="$(mktemp -d)"
trap 'rm -rf "${TMP}"' EXIT

echo "Downloading ${URL} …"
curl -fsSL "${URL}" -o "${TMP}/rootcli.tgz"
rm -rf "${DEST}"
mkdir -p "${DEST}"
tar -xzf "${TMP}/rootcli.tgz" -C "${DEST}"

BIN="$(find "${DEST}" -type f -name RootCli | head -n1)"
if [[ -z "${BIN}" ]]; then
  echo "error: RootCli binary missing in archive" >&2
  exit 1
fi
chmod +x "${BIN}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
bash "${SCRIPT_DIR}/install-rootcli-alias.sh" "${BIN}"

echo
echo "Installed. Open a new shell, cd into a project, then:"
echo "  rootcli here"
echo "  rootcli ask 'what is this?'"
