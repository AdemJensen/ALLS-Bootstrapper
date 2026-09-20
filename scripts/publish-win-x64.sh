#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"
dotnet_cmd="${DOTNET_CMD:-dotnet}"
configuration="${CONFIGURATION:-Release}"
publish_dir="$repo_root/artifacts/ALLS-win-x64"
archive_path="$repo_root/artifacts/ALLS-win-x64.zip"

case "$publish_dir" in
  "$repo_root/artifacts/ALLS-win-x64") ;;
  *)
    echo "Refusing to clean unexpected publish directory: $publish_dir" >&2
    exit 2
    ;;
esac

rm -rf -- "$publish_dir"
rm -f -- "$archive_path"
mkdir -p -- "$publish_dir"

"$dotnet_cmd" publish "$repo_root/src/Alls.Bootstrapper/Alls.Bootstrapper.csproj" \
  -c "$configuration" -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -m:1 -o "$publish_dir"

"$dotnet_cmd" publish "$repo_root/src/Alls.Configurator/Alls.Configurator.csproj" \
  -c "$configuration" -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -m:1 -o "$publish_dir"

xattr -cr "$publish_dir"
COPYFILE_DISABLE=1 ditto -c -k --norsrc --noextattr --keepParent \
  "$publish_dir" "$archive_path"

if unzip -Z1 "$archive_path" | grep -Eq '(^|/)\._|(^|/)__MACOSX(/|$)'; then
  echo "Archive contains unexpected macOS metadata files: $archive_path" >&2
  exit 3
fi

echo "Published directory: $publish_dir"
echo "Published archive:   $archive_path"
