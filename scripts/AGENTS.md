# Build and packaging script guidance

- Keep `publish-win-x64.ps1` and `publish-win-x64.sh` behaviorally equivalent.
  The Makefile selects between them and passes `DOTNET`/`CONFIGURATION` through.
- The supported package is win-x64, self-contained, and single-file for both
  executables. Preserve `Directory.Build.props` single-file compression/native
  extraction settings and do not introduce WPF trimming.
- Both projects publish into the same clean directory. The expected payload is
  `ALLS.exe`, `ALLS.Configurator.exe`, `alls-launcher.json`, and the two runtime
  images below `Assets/`; investigate unexpected loose runtime DLLs.
- Publish and clean scripts intentionally delete generated paths. Keep their
  targets resolved beneath this repository and retain explicit allowlists or
  equality checks before recursive deletion.
- Never clean `src/Alls.Bootstrapper/Assets`; those are source assets.
- The macOS ZIP path must continue to clear extended attributes, disable resource
  forks, and reject `._*`/`__MACOSX` entries. After changing packaging, inspect
  with `unzip -Z1 artifacts/ALLS-win-x64.zip`.
- `artifacts/` is generated and ignored. Update README commands whenever target
  names, output paths, prerequisites, or package contents change.

