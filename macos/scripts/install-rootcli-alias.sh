#!/usr/bin/env bash
# Install a `rootcli` launcher into ~/.local/bin and wire bash/zsh profiles.
set -euo pipefail

BIN_SRC="${1:-}"
if [[ -z "${BIN_SRC}" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
  CANDIDATES=(
    "${ROOT}/src/RootCli/bin/Release/net8.0/RootCli"
    "${ROOT}/src/RootCli/bin/Debug/net8.0/RootCli"
  )
  for c in "${CANDIDATES[@]}"; do
    if [[ -x "${c}" ]]; then
      BIN_SRC="${c}"
      break
    fi
  done
fi

if [[ -z "${BIN_SRC}" || ! -f "${BIN_SRC}" ]]; then
  echo "rootcli alias: binary not found. Build first: ./build.sh" >&2
  exit 1
fi

BIN_SRC="$(cd "$(dirname "${BIN_SRC}")" && pwd)/$(basename "${BIN_SRC}")"
chmod +x "${BIN_SRC}" 2>/dev/null || true

LOCAL_BIN="${HOME}/.local/bin"
mkdir -p "${LOCAL_BIN}"
WRAPPER="${LOCAL_BIN}/rootcli"

cat > "${WRAPPER}" <<EOF
#!/usr/bin/env bash
exec "${BIN_SRC}" "\$@"
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
    # Refresh block in place
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
  echo "rootcli alias: profile -> ${profile}"
}

install_profile "${HOME}/.bashrc"
if [[ -f "${HOME}/.zshrc" ]] || [[ "${SHELL:-}" == *zsh* ]]; then
  install_profile "${HOME}/.zshrc"
fi

# Also support login shells that only read .profile
if [[ -f "${HOME}/.profile" ]]; then
  if ! grep -qF "${MARKER_BEGIN}" "${HOME}/.profile" 2>/dev/null; then
    printf '\n%s\n' "${SNIPPET}" >> "${HOME}/.profile"
    echo "rootcli alias: profile -> ${HOME}/.profile"
  fi
fi

echo "rootcli alias: ${WRAPPER} -> ${BIN_SRC}"
echo "rootcli alias: open a new shell (or: source ~/.bashrc) then run: rootcli"
