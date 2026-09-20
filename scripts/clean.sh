#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd -- "$script_dir/.." && pwd)"

clean_targets=(
  "$repo_root/artifacts"
  "$repo_root/src/Alls.Bootstrapper/bin"
  "$repo_root/src/Alls.Bootstrapper/obj"
  "$repo_root/src/Alls.Configurator/bin"
  "$repo_root/src/Alls.Configurator/obj"
)

for target in "${clean_targets[@]}"; do
  case "$target" in
    "$repo_root/artifacts"|\
    "$repo_root/src/Alls.Bootstrapper/bin"|\
    "$repo_root/src/Alls.Bootstrapper/obj"|\
    "$repo_root/src/Alls.Configurator/bin"|\
    "$repo_root/src/Alls.Configurator/obj")
      rm -rf -- "$target"
      ;;
    *)
      echo "Refusing to clean unexpected path: $target" >&2
      exit 2
      ;;
  esac
done

echo "Removed build and packaging outputs."
