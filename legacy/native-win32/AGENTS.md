# Legacy native launcher guidance

- This directory is an archived Win32/GDI+ implementation retained for history
  and behavioral comparison. It is not built by the root `ALLS.sln` and is not
  the maintained product.
- Do not port fixes into this project, modernize it, or replace its historical
  assets unless the user explicitly asks for legacy work.
- For active features, use `src/Alls.Bootstrapper` and
  `src/Alls.Configurator` as the source of truth.
- If legacy changes are requested, build `legacy/native-win32/ALLS.sln`
  separately with the matching Visual Studio C++/Windows SDK toolchain and keep
  generated native outputs out of version control.

