# Legacy native launcher

This directory preserves the original Win32/GDI+ implementation, solution,
icons, and runtime images for history and comparison. It is no longer part of
the repository-root `ALLS.sln`; open this directory's `ALLS.sln` to build it.

The maintained application lives in `src/Alls.Bootstrapper` and uses WPF,
MVVM-style state management, localized resources, a configurable boot timeline,
and a dedicated process-launch service.
