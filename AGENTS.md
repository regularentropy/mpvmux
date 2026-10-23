# AGENTS.md — project reference and working rules

This file is context for working in this repository, not a runbook. Reading it requires no
action: nothing below is a session-start checklist, so do not run the commands, restructure the
project, or "fix" the items listed here just because you opened this file. Use the section that a
task actually needs, and apply it to that task only.

## Project overview

mpvmux — an Avalonia (.NET 10) desktop app that pairs videos with external audio/subtitle files and plays them through mpv as a queue.

- C# / .NET 10, Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2, SharpHook (global shortcuts), Material.Icons.Avalonia (icons), Markdown.Avalonia.Tight (release notes), xunit.
- Entry point: `src/mpvmux/Program.cs` → `App.axaml.cs` (DI composition, main window, tray icon).
- Solution `mpvmux.slnx`: `src/mpvmux` (WinExe), `tests/mpvmux.Tests` (xunit, has `InternalsVisibleTo`).
- Per-user data lives under `AppConstants.BaseFolderPath` (`%APPDATA%\mpvmux`): `config.json`, `history.json`, `backups/`, `corrupt/`. Both JSON files get a last-known-good slot under `backups/` and a timestamped quarantine copy under `corrupt/` when they cannot be read.
- Windows is the primary target; source also runs on Linux/macOS (tray/menu behavior differs per platform).

## Build and test reference

The commands below are alternatives, not a sequence — when a task needs one of them, run that one.
They are not meant to be run on startup or as a smoke test of the environment.

This environment's `~/.nuget` and NuGet HTTP cache are read-only, so every `dotnet` invocation has
to run with the package cache and all build artifacts under the session temp directory. These
values belong in the environment of the command, not in `-p:` properties:

```bash
export NUGET_PACKAGES="$TMPDIR/nuget/packages"
export NUGET_HTTP_CACHE_PATH="$TMPDIR/nuget/http-cache"
export ARTIFACTS="$TMPDIR/mpvmux-artifacts"
mkdir -p "$NUGET_PACKAGES" "$NUGET_HTTP_CACHE_PATH" "$ARTIFACTS"
```

| Purpose | Command |
| --- | --- |
| All tests | `dotnet test tests/mpvmux.Tests/mpvmux.Tests.csproj --artifacts-path "$ARTIFACTS" -p:RestoreFallbackFolders=` |
| One test group | `dotnet test tests/mpvmux.Tests/mpvmux.Tests.csproj --artifacts-path "$ARTIFACTS" -p:RestoreFallbackFolders= --filter "FullyQualifiedName~BundleCorruptionTests"` |
| Build (implies restore) | `dotnet build mpvmux.slnx --artifacts-path "$ARTIFACTS" -p:RestoreFallbackFolders=` |
| Restore only | `dotnet restore mpvmux.slnx --artifacts-path "$ARTIFACTS" -p:RestoreFallbackFolders=` |

`dotnet run --project src/mpvmux/mpvmux.csproj` starts the GUI. It needs a desktop session and is
user-run only — agents never start it (see Conventions).

Publish (self-contained, single file, `win-x64` + `linux-x64`) is documented in `README.md`;
`.github/workflows/build-self-contained.yml` mirrors it but is manual (`workflow_dispatch`).

## Architecture

This file is the short working reference. `ARCHITECTURE.md` explains the runtime, service graph,
data model, persistence, user flows, playback, shortcuts and UI composition in detail.

## Documentation updates

The docs are part of the change, not a follow-up task. Every change that affects what they
describe must update them in the same commit.

- Update `ARCHITECTURE.md` whenever you change behavior it describes: startup/runtime flow, DI
  registrations or lifetimes, models and their invariants, persistence/file formats and recovery,
  user flows, playback and mpv IPC, shortcuts, or the view-model graph. Add a section or amend the
  existing one; do not leave a rule that no longer matches the code.
- Update `AGENTS.md` when the working reference changes: project structure, build/test commands or
  environment quirks, conventions, user-facing string rules, or troubleshooting entries.
- Correct a document as soon as you notice it disagrees with the code — the code wins, and a stale
  document is a bug. Do not "fix" the code to match a stale document unless the document states
  an intentional rule.
- Prefer editing the existing section over appending a new note, and keep the docs as terse as the
  code they describe.

## Conventions

These are rules for code you change; they do not describe work to do on an untouched tree.

- `internal` by default; `public` only where XAML/designer requires it. Prefer `sealed` for new classes; several existing services and `BundleSession` are deliberately unsealed. `ViewModelBase : ObservableObject`.
- MVVM via CommunityToolkit source generators: `[ObservableProperty]`, `[RelayCommand]`, partial `OnXChanged` hooks. Code-behind is for wiring only (drag/drop, key capture, button handlers that call the VM).
- Compiled bindings only: `x:DataType` + `{CompiledBinding}` (`AvaloniaUseCompiledBindingsByDefault`).
- Services use `Task`/`ConfigureAwait(false)`; UI notifications are marshalled with `Dispatcher.UIThread.Post/Invoke`; user-facing errors go through `IDialogHelper`.
- Comment the *why* for invariants and workarounds (e.g. tray menu behavior, mpv IPC timing); XML docs for non-obvious contracts.
- Never try to launch the GUI or fake a visual check yourself — no `dotnet run`, headless/Xvfb sessions, screenshot scripts, simulated input or similar workarounds. Visual behavior (layout, tooltips, hover/pointer, themes) is verified by the user, who reports what they see; ask them to check instead.
- Tests: xunit; temp directories under `Path.GetTempPath()` with a GUID suffix; fakes as private nested classes implementing the service interfaces; `ConfigService`/`HistoryService` take a file path, so storage tests can exercise the real services against temp files. Never read/write the real `%APPDATA%\mpvmux` from tests.
- Line endings are LF (`.gitattributes`); files are UTF-8, and many existing files start with a BOM — keep the current encoding when editing rather than rewriting a file only to strip it. No formatter/linter is configured; builds currently emit pre-existing nullable warnings in `AppMetadata.cs` / `App.axaml.cs`.

## User-facing strings

Every string shown in the GUI (XAML literals as well as dialog/status text) follows one style:

- `mpv` and `mpvmux` stay lowercase, even at the start of a sentence; only identifiers use `MPV`.
- Sentence case for menu items, buttons, checkboxes and window/dialog titles (e.g. "Save as…", "Check for updates…", "About mpvmux").
- Labels that open a dialog or menu end with a Unicode ellipsis (`…`), never `...`.
- Say "bundle" for `.mpvm` content, not "file".
- Recent-list states are "missing" and "damaged" (internal flags stay `IsMissing` / corrupt).
- Failures lead with "Could not …"; full sentences end with a period, and `{ex.Message}` tails keep their own punctuation.
- Tooltips are terse without a trailing period; multi-sentence hints and status text use full punctuation.

## Troubleshooting reference

- `Unable to find fallback package folder 'A:\...'` means the restore assets were generated on Windows; restore again with `-p:RestoreFallbackFolders=`. The `NUGET_PACKAGES` / `NUGET_HTTP_CACHE_PATH` / `--artifacts-path` setup above avoids the read-only cache errors (they are environment variables, not `-p:` properties).
- Missing Avalonia members such as `InitializeComponent` mean the XAML compiler's generated sources are stale; a rebuild with `--no-incremental` regenerates them.
