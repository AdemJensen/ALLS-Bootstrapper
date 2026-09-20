# ALLS repository guidance

## Repository shape

- The maintained solution is `ALLS.sln` and contains two .NET 8 WPF applications:
  `src/Alls.Bootstrapper` (`ALLS.exe`) and `src/Alls.Configurator`
  (`ALLS.Configurator.exe`).
- `legacy/native-win32` is historical reference code and is not part of the root
  solution.
- `artifacts/`, `outputs/`, `bin/`, and `obj/` are generated output. Do not treat
  their contents as source or commit them.
- The application is Windows-only at runtime and targets x64, although the
  solution can be cross-built from macOS because both projects set
  `EnableWindowsTargeting`.

## Build and verification

- Run commands from the repository root.
- Preferred entry points are `make build`, `make publish`, and `make clean`.
- A direct build is `dotnet build ALLS.sln -c Release -m:1`.
- In the managed macOS workspace, the available SDK can be invoked with:

  ```sh
  env DOTNET_CLI_HOME=/private/tmp/alls-dotnet-home \
    NUGET_PACKAGES=/private/tmp/alls-nuget-packages \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
    /private/tmp/alls-dotnet-sdk/dotnet build ALLS.sln -c Release -m:1
  ```

- There is currently no automated test project. At minimum, build every changed
  WPF project and run `git diff --check`. Behavior involving windows, HID,
  process monitoring, power actions, or native focus must ultimately be checked
  on Windows.
- Do not enable WPF trimming. XAML, data binding, JSON reflection, and embedded
  resources make `PublishTrimmed` unsafe here.

## Cross-cutting change rules

- Preserve existing JSON compatibility. Enum values are serialized as strings
  and JSON property names are camelCase in Configurator output.
- Configuration model changes have a repository-wide blast radius. Follow the
  more specific instructions under `src/Alls.Bootstrapper/Models`.
- Keep README, `docs/configuration.md`, `docs/configurator.md`, the default
  `alls-launcher.json`, and the UI consistent with implemented behavior.
- Preserve user-provided assets in `src/Alls.Bootstrapper/Assets`; cleanup tasks
  must only remove generated directories.
- Keep external side effects suppressible in preview mode. Do not let preview
  mode launch programs, copy updates, or invoke Windows power actions.

