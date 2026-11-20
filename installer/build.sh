#!/usr/bin/env bash
# 出一条完整的安装包：自包含发布 -> Inno Setup 编译。
set -euo pipefail
cd "$(dirname "$0")/.."

dotnet publish src/BobIme.Shell -c Release -r win-x64 --self-contained true -o artifacts/publish

for candidate in \
  "$LOCALAPPDATA/Programs/Inno Setup 6/ISCC.exe" \
  "/c/Program Files (x86)/Inno Setup 6/ISCC.exe" \
  "/c/Program Files/Inno Setup 6/ISCC.exe"; do
  if [ -x "$candidate" ]; then
    ISCC="$candidate"
    break
  fi
done

if [ -z "${ISCC:-}" ]; then
  echo "找不到 ISCC.exe，先装 Inno Setup：winget install -e --id JRSoftware.InnoSetup" >&2
  exit 1
fi

"$ISCC" installer/BobIme.iss
ls -l artifacts/installer
