# Shared configuration model guidance

- `LauncherSettings.cs` and `BootPhase.cs` are compiled into both WPF projects.
  The Configurator links these exact source files; do not create a second DTO
  model there.
- Treat public property names, enum member names, defaults, and nullability as
  the persisted JSON contract. Prefer additive, backward-compatible changes.
- When adding or changing a field, update all applicable locations:
  1. Bootstrapper loading/defaulting in `Services/ConfigurationService.cs`;
  2. Configurator normalization and validation;
  3. Configurator controls and enum display options;
  4. `src/Alls.Bootstrapper/alls-launcher.json`;
  5. `docs/configuration.md` and, when relevant, the README/design document.
- Collections and nested settings may be null in hand-edited or older JSON even
  when the C# property is non-nullable. Both readers must normalize them before
  use.
- String enum serialization is intentional. Renaming an enum member is a JSON
  breaking change and requires an explicit compatibility strategy.
- After a model change, build both projects and perform an open-preview-save-
  reopen round trip with Configurator before considering the change complete.

