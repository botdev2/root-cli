#!/usr/bin/env bash
# Root CLI installer (macOS / Linux). Safe for:
#   curl -fsSL https://raw.githubusercontent.com/botdev2/root-cli/main/scripts/install.sh | bash
#
# Downloads the matching GitHub Release archive and puts `rootcli` on PATH.
set -euo pipefail

REPO="${ROOTCLI_GITHUB_REPO:-botdev2/root-cli}"
TAG="${ROOTCLI_TAG:-latest}"

os="$(uname -s | tr '[:upper:]' '[:lower:]')"
arch="$(uname -m)"

case "${os}" in
  linux)
    case "${arch}" in
      x86_64|amd64) ASSET="${ROOTCLI_ASSET:-RootCli-linux-x64.tar.gz}" ;;
      aarch64|arm64) ASSET="${ROOTCLI_ASSET:-RootCli-linux-arm64.tar.gz}" ;;
      *) echo "error: unsupported Linux arch: ${arch}" >&2; exit 1 ;;
    esac
    ;;
  darwin)
    case "${arch}" in
      arm64) ASSET="${ROOTCLI_ASSET:-RootCli-osx-arm64.tar.gz}" ;;
      x86_64) ASSET="${ROOTCLI_ASSET:-RootCli-osx-x64.tar.gz}" ;;
      *) echo "error: unsupported macOS arch: ${arch}" >&2; exit 1 ;;
    esac
    ;;
  *)
    echo "error: unsupported OS: ${os} (use the Windows installer on Win)" >&2
    exit 1
    ;;
esac

if [[ "${TAG}" == "latest" ]]; then
  API="https://api.github.com/repos/${REPO}/releases/latest"
else
  API="https://api.github.com/repos/${REPO}/releases/tags/${TAG}"
fi

echo "Root CLI — installing ${ASSET} (${TAG})…"
URL="$(curl -fsSL -H "User-Agent: rootcli-install" "${API}" | python3 -c "
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

echo "Downloading ${URL}…"
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

LOCAL_BIN="${HOME}/.local/bin"
mkdir -p "${LOCAL_BIN}"
WRAPPER="${LOCAL_BIN}/rootcli"
cat > "${WRAPPER}" <<EOF
#!/usr/bin/env bash
exec "${BIN}" "\$@"
EOF
chmod +x "${WRAPPER}"

MARKER_BEGIN="# >>> rootcli >>>"
MARKER_END="# <<< rootcli <<<"
SNIPPET="${MARKER_BEGIN}
export PATH=\"\${HOME}/.local/bin:\${PATH}\"
${MARKER_END}"

install_profile() {
  local profile="$1"
  [[ -f "${profile}" ]] || touch "${profile}"
  if grep -qF "${MARKER_BEGIN}" "${profile}" 2>/dev/null; then
    local tmp
    tmp="$(mktemp)"
    awk -v begin="${MARKER_BEGIN}" -v end="${MARKER_END}" -v snippet="${SNIPPET}" '
      $0 == begin { skip=1; print snippet; next }
      $0 == end { skip=0; next }
      !skip { print }
    ' "${profile}" > "${tmp}"
    mv "${tmp}" "${profile}"
  else
    printf '\n%s\n' "${SNIPPET}" >> "${profile}"
  fi
}

install_profile "${HOME}/.bashrc"
if [[ -f "${HOME}/.zshrc" ]] || [[ "${SHELL:-}" == *zsh* ]]; then
  install_profile "${HOME}/.zshrc"
fi
if [[ -f "${HOME}/.profile" ]] && ! grep -qF "${MARKER_BEGIN}" "${HOME}/.profile" 2>/dev/null; then
  printf '\n%s\n' "${SNIPPET}" >> "${HOME}/.profile"
fi

echo
echo "Installed Root CLI → ${BIN}"
echo "Open a new terminal, then:"
echo "  rootcli here"
echo "  rootcli ask 'what is this?'"
echo "  rootcli login"
