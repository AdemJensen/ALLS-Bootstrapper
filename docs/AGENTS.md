# Documentation guidance

- Documentation is written primarily in Simplified Chinese. Keep product names,
  enum values, JSON keys, commands, and paths exact.
- `README.md` is the overview and operational quick start.
  `configuration.md` is the authoritative field/behavior reference.
  `configurator.md` explains editor architecture, save behavior, and extension
  work.
- When configuration behavior changes, update the model/default JSON/UI first,
  then document the behavior actually implemented. Do not describe planned
  fields as available.
- Keep examples valid JSON and consistent with string enum serialization.
  Mention path bases, defaults, ranges, conditional applicability, and safety
  behavior when they affect deployment.
- Build/publish instructions must remain valid on both Windows and macOS. Keep
  Make targets and direct `dotnet` alternatives synchronized with the scripts.
- Use repository-relative Markdown links and avoid duplicating the full JSON
  reference back into README.

