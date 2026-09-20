# Bootstrapper guidance

## Architecture

- `App.xaml.cs` composes services and the `LauncherViewModel`; keep startup
  wiring explicit rather than introducing a container for this small app.
- `LauncherViewModel` owns the boot/menu/update/error state machine.
  `MainWindow.xaml.cs` owns WPF window behavior, layout, focus handoff, and
  translation of keyboard/HID events into view-model actions.
- Long-running game and update flows use cancellation tokens plus a session
  generation check. Preserve both so stale async continuations cannot mutate the
  current screen.
- UI changes from HID or service callbacks must return to the WPF dispatcher.

## Runtime invariants

- Resolve deployed assets, logs, launch working directories, and the default
  configuration relative to `AppContext.BaseDirectory`. An explicit
  `--config` path is resolved from the caller's current directory.
- `--preview` must disable game launch, game updates, custom commands, and power
  actions while still allowing the UI flow to be exercised.
- Suspend and release HID devices before starting a game so game-side IO
  modules can acquire them; resume input when returning to launcher UI.
- Keep Windows P/Invoke and power functionality guarded by
  `OperatingSystem.IsWindows()` where it can be reached elsewhere.
- The boot layout is authored on a 1280x720 design surface. Preserve the
  distinct Chunithm, maimai DX, Ongeki, and Card Maker layout behavior and test
  both landscape and portrait dimensions when changing layout math.

## Safety-sensitive services

- Do not weaken update path protections in `GameUpdateService`: reject path
  traversal, source/target overlap, and filesystem-root `FullReplace` targets.
- Update sources are tried in configured order. The first successful or
  up-to-date source ends the attempt; total update failure is logged but does
  not prevent game launch.
- Process and window monitoring have separate ready/loss policies. Keep `Any`
  versus `All` and `AnyMissing` versus `AllMissing` semantics intact.
- Input decoding is device-profile-specific and edge-triggered. Preserve
  report offsets, active-low IO4 handling, Maimoller `MI_00`/feature-report
  selection, and the 2+7 chord behavior.

## Localization and verification

- Every localization key must exist in all three `Resources/Strings.*.xaml`
  files. Keep their key order aligned; the Configurator also consumes these
  files to build its message-key list.
- A useful Windows smoke test is `ALLS.exe --preview --windowed`. Verify startup,
  menu navigation, confirmation, timeline skipping, and clean exit without
  performing external actions.

