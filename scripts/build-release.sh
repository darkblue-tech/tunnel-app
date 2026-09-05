#!/usr/bin/env bash
# Local build script for DarkTunnel Client releases
set -e

VERSION="${1:-1.0.1}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/.." && pwd)"

echo "=== Building DarkTunnel Client v${VERSION} ==="

TARGETS=("win-x64" "win-arm64" "linux-x64" "linux-arm64" "osx-x64" "osx-arm64")

mkdir -p "${ROOT_DIR}/out/dist"
mkdir -p "${ROOT_DIR}/out/installers"

for TARGET in "${TARGETS[@]}"; do
  echo "--> Publishing ${TARGET}..."
  dotnet publish "${ROOT_DIR}/Client.Desktop/Client.Desktop.csproj" \
    -c Release \
    -r "${TARGET}" \
    --self-contained true \
    -o "${ROOT_DIR}/out/${TARGET}"

  if [[ "${TARGET}" == win-* ]]; then
    ARCHIVE="${ROOT_DIR}/out/dist/DarkTunnel-Client-v${VERSION}-${TARGET}.zip"
    rm -f "${ARCHIVE}"
    (cd "${ROOT_DIR}/out/${TARGET}" && zip -r "${ARCHIVE}" .)
  else
    ARCHIVE="${ROOT_DIR}/out/dist/DarkTunnel-Client-v${VERSION}-${TARGET}.tar.gz"
    rm -f "${ARCHIVE}"
    tar -czvf "${ARCHIVE}" -C "${ROOT_DIR}/out/${TARGET}" .
  fi
  echo "Created: ${ARCHIVE}"
done

echo "=== Build Complete! Products in out/dist ==="
