# Configurator guidance

## Independence and shared inputs

- Configurator is a standalone WPF executable. It must not require `ALLS.exe` or
  the Bootstrapper assembly at runtime.
- The two configuration model files are linked at compile time from the
  Bootstrapper project. Language XAML files are embedded as raw resources so
  `EnumOptions` can enumerate every message key.
- Preserve `WithCulture="false"` and the explicit `LogicalName` metadata for
  embedded `Strings.*.xaml`. Without it, MSBuild moves culture-suffixed files to
  satellite assemblies and static XAML initialization can terminate the app at
  startup.
- Static properties referenced through `{x:Static}` execute while the window is
  being parsed. Keep them deterministic and ensure failures are surfaced by the
  startup exception handler.

## Document lifecycle

- With no adjacent or requested JSON file, show the empty-document state; do not
  silently create an in-memory document.
- `hasDocument`, `currentPath`, `isDirty`, and `baselineSettings` jointly define
  document state. Opening/newing captures a baseline, saving replaces it, and
  “放弃所有修改” restores a clone of it.
- New/open/close/window-close must respect the save/discard/cancel prompt.
  Explicit “放弃所有修改” requires its own destructive confirmation.
- Programmatic binding and selection changes must be wrapped by
  `suppressChanges`; user edits must still mark the document dirty.
- Commit pending `DataGrid` edits and reject WPF binding conversion errors before
  validation or save.

## UI and persistence

- The model classes do not implement `INotifyPropertyChanged`. Conditional UI
  state and refreshed list labels therefore require explicit event handlers and
  `Items.Refresh()` calls.
- Use `HelpLabel` or equivalent tooltips for non-obvious settings. Enum controls
  display localized labels but bind to the actual enum/string value written to
  JSON.
- Hide parameters that do not apply to the selected discriminator, such as HTTP
  versus USB source fields and custom-command-only operation fields.
- Keep `ConfigurationDocumentService` tolerant when reading comments, trailing
  commas, and case differences. Saving remains indented camelCase UTF-8 without
  BOM, via a temporary file, with a `.bak` copy when overwriting.
- Validation errors block saving; warnings do not. Keep normalization,
  validation, UI fields, and model defaults synchronized.
- Relative paths selected by the user are made relative to the configuration
  file location. Do not silently reinterpret HTTP paths as local directories.

## Verification

- Cross-building on macOS verifies C#/XAML compilation but cannot validate WPF
  interaction. Also smoke-test on Windows: empty startup, open/new/close,
  dirty prompts, discard restoration, validation, save/backup, conditional
  editors, and reopening the saved JSON.

