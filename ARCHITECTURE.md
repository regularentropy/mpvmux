# Architecture

mpvmux is a single-process Avalonia desktop app. This document explains how the pieces fit
together and why the non-obvious ones behave the way they do. It complements `AGENTS.md` (the
short working reference) and `README.md` (user-facing documentation): when this document and the
code disagree, the code wins, and the document should be corrected.

Keep this document current: a change to the runtime, service graph, model, persistence, user flows,
playback, shortcuts or UI composition must update the matching section here in the same change
(see "Documentation updates" in `AGENTS.md`). If a new area has no section yet, add one rather than
leaving the behavior undocumented.

The sections follow the runtime from startup through composition, data, persistence, user flows,
playback, shortcuts and the UI, and finish with guidance for changing each area and the test
seams.

## 1. Runtime overview

### Process startup

1. `src/mpvmux/Program.cs` reads the command line, takes the single-instance lock, and then
   builds the Avalonia app (`AppBuilder.Configure<App>()`, `UsePlatformDetect()`, `WithInterFont()`,
   `LogToTrace()`) and calls `StartWithClassicDesktopLifetime(args)`. A second launch never gets
   this far with a window: it forwards its bundles or reports that mpvmux is already running
   (see "Single instance").
2. `App.Initialize()` loads `App.axaml`.
3. `App.OnFrameworkInitializationCompleted()` is the composition root. For the desktop lifetime it:
   - in message mode shows one "already running" window and returns without a service graph;
   - otherwise creates `AppConstants.BaseFolderPath` (`%APPDATA%\mpvmux`) before any service
     touches disk;
   - builds a `ServiceCollection` through `AddCommonServices()` and resolves the provider;
   - resolves `IHistoryService` and `IConfigService` and applies the saved theme with
     `ThemeManager.Apply(Config.UseDarkTheme)`;
   - resolves the singleton `MainWindowViewModel`, creates `MainWindow`, assigns it as the
     `DataContext`, and makes it the desktop main window;
   - subscribes `Loaded` to `MainWindowViewModel.InitializeAsync()` and `Closing` to the
     two-pass close handler, which runs the unsaved-changes guard and then saves `history.json`
     and `config.json`;
   - creates the tray icon.
4. Window-level startup work is deliberately deferred to `Loaded`, so dialogs opened by the view
   models have an owner window.

### Single instance

mpvmux allows one instance per user, because everything it owns — config, history, backups and
the per-path bundle backups — lives in that user's data folder. `SingleInstanceGuard` opens
`instance.lock` inside the folder with `FileShare.None` before Avalonia is configured and holds
the handle for the process lifetime. The operating system releases it on exit, so a crash cannot
leave a stale block; the file itself stays in place and records the PID for diagnostics. The
lock fails open when the folder cannot be created or opened at all: an unusable data folder is
reported by the storage services, and refusing to start over the lock would be worse than two
windows.

A launch that cannot take the lock then:

1. reads its arguments through `LaunchArguments.BundlePaths`, which keeps only existing
   `.mpvm` files as absolute paths (everything else on the command line is ignored);
2. forwards each path over `InstanceChannel` and exits as soon as one was accepted — that is
   `SecondLaunch.ShouldShowAlreadyRunning` returning false;
3. otherwise sets `App.ShowAlreadyRunningMessage`, shows
   `MessageBoxWindow("mpvmux", "mpvmux is already running.")` and exits when that window closes.
   Message mode skips the service graph, the main window and the tray; the default shutdown mode
   ends the process with its only window.

`InstanceChannel` derives its endpoint from the data folder, so it is per user too: a named pipe
`mpvmux-<hash>` on Windows, `<temp>/mpvmux-<hash>.sock` elsewhere — the same platform split as
the mpv IPC. The running instance starts its listener together with the main window, posts every
accepted path to the UI thread, shows and activates the window and opens the bundle through
`HistoryViewModel.OpenAsync`, so history bookkeeping, the load lock and the unsaved-changes
prompt apply exactly as they do for File → Open. The sender retries a missing listener
(5 × 200 ms) so a launch that races the first instance's startup still lands, and only the
instance that bound the socket removes it on exit — a sender-side channel never unlinks it.

### Startup sequence

`MainWindowViewModel.InitializeAsync()` runs in this order:

1. `ReportSettingsRecovery()` — drains the one-shot recovery reports from both storage services
   and shows at most one informational dialog (see §5.7).
2. `LoadLastSessionAsync()` — asks `IHistoryService.GetLastDatabasePath()` for the last bundle,
   then opens it through `HistoryViewModel.OpenAsync` so the history load lock and the
   backup-restore prompt are reused. If the load fails, the stored path is cleared and saved, so
   a broken file is not retried on every launch.
3. `EnsureMpvConfiguredAsync()` — skips when `Config.MpvSetupCompleted`; otherwise probes
   `Config.MpvPath` with `MpvLocator.ResolveAsync`, falls back to `LocateAsync` (app folder,
   `PATH`, known installs), saves a detected path silently, or opens the setup window.
4. `CheckForUpdatesOnStartupAsync()` — only when `Config.CheckForUpdatesOnStartup`; failures are
   reported without blocking startup.
5. `RefreshGlobalMode()` — starts or stops the system-wide shortcut hook and reports failure
   through `IDialogHelper`.

### Tray icon

The tray menu is built manually (`Show mpvmux`, `Autoplay`, `Start playback in fullscreen`,
`Exit`) rather than from XAML. `Autoplay` and `Start playback in fullscreen` are checkable items
mirroring `ConfigModel.EnableAutomaticPlayback` and `EnableFullscreen`.

On Windows the tray menu is a managed flyout built from `NativeMenu.Items`, and
`NativeMenu.NeedsUpdate` is **not** raised for it. Check state is therefore pushed explicitly:
`App` subscribes to `ConfigModel.PropertyChanged` and calls `SyncTrayMenuItems()` when either
flag changes; `NeedsUpdate` covers the platforms where it does fire. The tray icon itself follows
the OS taskbar theme (light/dark asset), not the app theme, because it sits on the taskbar.

### Window and application icons

`logo.png` / `logo-dark.png` back the theme-aware window icon (`AppWindowIcon`) and the About
logo; `taskbar-light.ico` / `taskbar-dark.ico` back the theme-aware tray icon. The executable
icon is separate and theme-independent: `Assets/app.ico` is embedded as Win32 resources through
`<ApplicationIcon>` in `mpvmux.csproj` and excluded from the `AvaloniaResource` glob, so it is
not also shipped as an unused `avares` resource. Since the exe icon cannot follow the theme, it is
generated from the light `logo-dark.png` mark so it stays visible on dark taskbars and Explorer
themes; regenerate it from that file rather than editing it by hand.

### Shutdown

Closing the main window is two-pass. The first `Closing` is cancelled so
`IUnsavedChangesGuard` can run (flush pending autosave, then ask with Save / Don't save /
Cancel; see §5), and the confirmed pass saves history and config (best effort — both save
methods report failure instead of throwing) and closes the window. A cancelled first pass
leaves the window open and the bundle untouched. The tray's `Exit` closes the main window
through the same path instead of shutting the lifetime down, so the guard can cancel it; the
default shutdown mode exits once the last window is gone.

### Platform notes

- Windows is the primary target: packaged build, managed tray flyout, named-pipe mpv IPC and
  shortcut suppression are Windows-tested behavior.
- Linux and macOS run from source; the tray menu uses `NeedsUpdate`, mpv IPC uses a Unix domain
  socket, and macOS otherwise behaves like the Windows path for shortcut suppression.
- Nothing in the app assumes a specific desktop environment; GUI startup is user-run only (see
  `AGENTS.md`).

## 2. Composition and service lifetimes

Everything is registered in `ServiceCollectionExtensions.AddCommonServices` at the bottom of
`App.axaml.cs`. Services are interface + implementation pairs grouped into concern folders under
`src/mpvmux/Services` (`Bundles`, `Storage`, `Playback`, `Shortcuts`, `Shell`, `App`) while all
share the flat `mpvmux.Services` namespace, so callers never import a folder name.

### Singleton services

| Area | Registration | Responsibility |
| --- | --- | --- |
| Bundles | `IBundleBackupStore` → `BundleBackupStore` | Hash-keyed backup slots, quarantine folder. |
| Bundles | `IBundleRepository` → `BundleRepository` | Selected path, save/load/restore, save serialization. |
| Bundles | `IBundleFileService` → `BundleFileService` | Session-level new/open/save/restore plus history bookkeeping. |
| Bundles | `IBundleRelinkService` → `BundleRelinkService` | Find missing bundles by name in a folder. |
| Bundles | `IMediaRelinkService` → `MediaRelinkService` | Detect missing media files and propose renamed replacements. |
| Bundles | `IBundleEditTracker` → `BundleEditTracker` | Dirty state of the open bundle; suppression for non-user writes. |
| Bundles | `IBundleAutoSaveService` → `BundleAutoSaveService` | Debounced automatic bundle writes, flush and failure reporting. |
| Bundles | `IUnsavedChangesGuard` → `UnsavedChangesGuard` | Flush-then-prompt decision when a bundle is replaced or the app closes. |
| Storage | `IConfigService` → `ConfigService` | `config.json`, normalization, dirty tracking, recovery report. |
| Storage | `IHistoryService` → `HistoryService` | `history.json`, recent ordering, pin/missing/damaged markers. |
| Playback | `IPlayerService` → `PlayerService` | The single mpv process, IPC, autoplay advance. |
| Playback | `IMpvLocator` → `MpvLocator` | Resolve/probe an mpv executable or configured path. |
| Shortcuts | `IShortcutService` → `ShortcutService` | Effective gestures, validation, persistence, local/global routing. |
| Shortcuts | `IGlobalShortcutHook` → `GlobalShortcutHook` | System-wide key capture via SharpHook. |
| Shell | `IWindowService` → `WindowService` | Open dialogs, own them by the main window, resolve dialog VMs. |
| Shell | `IDialogHelper` → `DialogHelper` | Error/info message boxes (the user-facing error channel). |
| Shell | `IFilePickerService` → `FilePickerService` | Open/save file, folder and executable pickers. |
| App | `IUpdateService` → `UpdateService` | GitHub release check (release notes + URL). |
| Infra | `HttpClient` | One long-lived client so sockets are reused between update checks. |
| State | `BundleSession` | The live `MediaBundleModel`; replacing it swaps the open document. |
| State | `AppMetadata` | Version, title, GitHub API/release URLs. |
| View models | `HistoryViewModel`, `FileMenuViewModel`, `TopMenuViewModel`, `ShortcutGesturesViewModel`, `BottomPanelViewModel`, `TrackTableViewModel`, `AboutWindowViewModel`, `MainWindowViewModel` | Shared state and commands; see §9. |

These are singletons because they own state that must not be duplicated (recent list, config,
the running mpv process, the selected bundle), publish events other surfaces subscribe to
(`BundleSession.PropertyChanged`, `ShortcutService.Changed`, `HistoryService.OnHistoryChanged`),
or both.

### Transient dialog view models

`UpdaterViewModel`, `PreferencesWindowViewModel`, `SetupWindowViewModel`,
`RecentBundlesViewModel` and `BundleChoiceViewModel` are transient: `WindowService` resolves a
fresh one each time it opens the dialog. `BundleChoiceViewModel` is initialized with the dropped
paths before the window is shown. `AboutWindowViewModel` is deliberately a singleton because its
content is static metadata and the window is trivial.

### Directly-called static helpers

These are not registered and are called directly because they hold no state or subscriptions:

- `BundleSerializer` — parse/validate/serialize/normalize the `.mpvm` shape.
- `BundleDropClassifier` — decide whether a drop carries a bundle.
- `AtomicFile` — temp-file-plus-rename writes for every JSON file the app owns.
- `MediaFileScanner` — dropped paths/folders to `MediaRecord` lists.
- `ThemeManager` — applies the theme variant and swaps the logo/icon resources.
- `Desktop` — locates the current main window as a dialog owner.

### Dependency rules

- Singletons only depend on other singletons; no dialog view model is captured by a singleton.
- The main window is created directly (not by DI) and receives the singleton
  `MainWindowViewModel` as its `DataContext`.
- Services never touch the UI directly: user-visible errors go through `IDialogHelper`, and
  cross-thread UI work is posted to `Dispatcher.UIThread`.
- New service checklist: interface + implementation in the matching folder (flat namespace),
  register in `AddCommonServices` with the intended lifetime, `internal` unless XAML needs it,
  and a test seam in the constructor when the service touches a path or process.

## 3. Model layer

`src/mpvmux/Models` holds the plain data shapes, all in the flat `mpvmux.Models` namespace. They
are `ObservableObject` subclasses where bindings or change tracking need notifications, and
records where value semantics help. The UI never serializes them directly except through
`BundleSerializer`; the storage services own the JSON options.

### `ConfigModel` (`config.json`)

| Member | Meaning |
| --- | --- |
| `MpvPath` | Executable, folder, or bare command name (serialized as `MPVPath`). Defaults to `mpv`. |
| `MpvSetupCompleted` | True once mpv was detected or the setup window was dismissed; gates the startup probe. |
| `EnableAutomaticPlayback` | Advance to the next video row when mpv exits naturally. |
| `EnableFullscreen` | Pass `--fullscreen` to mpv. |
| `CheckForUpdatesOnStartup` | Run the GitHub release check during `InitializeAsync`. |
| `AutoSaveEnabled` | Write the open bundle automatically a moment after the last edit. Off by default; only bundles with a destination are written, and backups still change on explicit saves only. |
| `UseDarkTheme` | Requested theme variant (window, menus, logo). |
| `GlobalShortcutActions` | Action ids handled by the system-wide hook. Empty means the hook is not started at all. |
| `EnableGlobalShortcuts` | Legacy switch: on load it is expanded into `GlobalShortcutActions` for every action, then always written back `false`. |
| `RecentLimit` | Number of unpinned entries the Recent list shows; clamped to 1..50 (default 10). |
| `ShortcutOverrides` | Gesture text by action id. Only actions deviating from their default are stored, so new defaults reach users who never customized them. |

`ConfigService` subscribes to `PropertyChanged` for dirty tracking, so mutating a collection in
place is invisible to it. Code that changes `ShortcutOverrides` or `GlobalShortcutActions`
assigns a **new** collection (see `ShortcutService.Apply`), which raises the change and marks the
config dirty.

### `HistoryModel` (`history.json`)

- `LastDatabasePath` — bundle restored on startup, if it still exists.
- `History` — `ObservableCollection<MediaRecord>`; pinned entries form the prefix, unpinned
  entries follow newest-first.
- `PinnedPaths`, `CorruptPaths` — persisted marker lists. Pin state and damage state deliberately
  live here, not inside the bundle, so a bundle file never carries its own recovery state and
  copying a bundle does not copy history.

### `MediaBundleModel` (`.mpvm`)

- `SchemaVersion` — defaults to 1; files without the field deserialize as 1.
- `VideoList`, `AudioList`, `SubtitleList` — `ObservableCollection<MediaRecord>`.
- `SelectedVideoIndex` — `-1` means no selection; always clamped into range after normalization.
- `Volume` — 0..100; `IsMuted` is derived as `Volume == 0` (muting is not a separate flag).
- `TryGetSelectedTracks(out video, out audio, out subtitle)` — the single source of truth for
  playback arguments. The selected video must exist and have a non-empty path; audio and subtitle
  are taken from the same index and are `null` when the list is shorter or the cell is empty.
- `TryIncrement()` — advances `SelectedVideoIndex` while another video row exists, returning
  `false` at the end. Audio/subtitle do not drive the queue length.
- `SetVideoList` replaces the contents and resets the selection to row 0; `SetAudioList` and
  `SetSubtitleList` replace their contents without touching the selection.

The three lists are aligned **by index**: the video list is the row spine. Audio and subtitle
cells may be missing (shorter lists or empty paths), and rows without a video are displayed but
cannot be played. `MediaRecord.Placeholder` (`Name == ""`, `Path == ""`) is used as an explicit
empty cell when one track of a row is cleared, so later rows keep their alignment instead of
shifting up.

### `MediaRecord`

A value record `(Name, Path)`, so `with` copies and equality are cheap and bundles serialize
without custom converters. `Name` is the display name (normally the file name without
extension); `Path` is the full path. Three optional fingerprint fields are written only when they
were captured and are omitted when null, so older bundles and history stay byte-compatible:
`SizeBytes`, `ModifiedUtc` and `ContentHash` describe the media file as of the last open or save
and drive rename detection (§5). `ContentHash` is a SHA-256 over the length and the first/last
64 KiB of the file — sampled so opening and saving a bundle that references multi-gigabyte media
stays cheap — and is reused while size and modification time are unchanged. Three properties are
`[JsonIgnore]` and rebuilt from the filesystem/history on load:

- `IsPinned` — mirrors `HistoryModel.PinnedPaths`.
- `IsMissing` — the bundle file is not on disk right now; the record stays in history for
  re-linking.
- `IsCorrupt` — the file exists but failed bundle validation.

### `ShortcutAction` / `ShortcutActions`

`ShortcutAction` is `(Id, Label, DefaultGesture)`. `Id` is also the key in
`ConfigModel.ShortcutOverrides` and `GlobalShortcutActions`, so it is a persistence contract and
must stay stable. `ShortcutActions` exposes the constants and the ordered `All` list used by
Preferences, validation and the DI-independent defaults. `Alt+F4` is reserved for closing the
window and is not a remappable action.

### Model invariants

- The three track lists are never null after load; normalization replaces nulls and repairs
  shapes.
- Alignment is positional and must survive every edit (remove, clear, replace, normalize).
- Duplicate rows (same video/audio/subtitle triple) and entirely empty rows are removed during
  bundle normalization, using one keep-mask for all three lists.
- `SelectedVideoIndex` is `-1` or a valid video index; `Volume` is 0..100.
- History markers reference paths case-insensitively and may outlive the files they name; the
  flags are recalculated, the markers are not silently dropped.

## 4. Persistence

mpvmux owns four kinds of file: the user's `.mpvm` bundles, `config.json`, `history.json`, and the
backup/quarantine artifacts for all three. Every write goes through `AtomicFile`; every read is
validated before it can reach the UI.

### 4.1 Bundle format

A bundle is indented JSON serialized from `MediaBundleModel` with
`BundleSerializer.Options = { WriteIndented = true }`:

```json
{
  "SchemaVersion": 1,
  "VideoList": [ { "Name": "episode-01", "Path": "/media/episode-01.mkv" } ],
  "AudioList": [ { "Name": "episode-01", "Path": "/media/episode-01.flac" } ],
  "SubtitleList": [],
  "SelectedVideoIndex": 0,
  "Volume": 100
}
```

`SchemaVersion` is the only compatibility contract. `BundleSerializer.CurrentSchemaVersion` is 1;
files written before versioning existed have no field and deserialize as 1. A file with a newer
version is rejected rather than guessed at. Apart from the schema version, selection and volume, a bundle only references paths and display
names — there is no history state and no cached media data. `MediaRecord.SizeBytes`,
`ModifiedUtc` and `ContentHash` are the one exception: optional fingerprints captured when the
bundle is opened and refreshed on every save, written only when the file could be read and
omitted when null. They are additive, so the schema version stays 1 and builds without them still
open new bundles (unknown members are skipped). A bundle larger than `MaxBundleBytes` (4 MiB) is
treated as corrupt because a valid one only references paths.

### 4.2 Load and validation

The read path is:

```
BundleFileService.LoadAsync
  → BundleRepository.LoadFromFileAsync
      → AtomicFile.CleanupTemp(file)            remove a stale .tmp from an interrupted save
      → BundleSerializer.LoadAsync
          → FileInfo checks + UTF-8 read (BOM-aware)
          → BundleSerializer.Parse
      → on failure: CanRestoreBackup = backup validates
  → only on success: MediaFileFingerprint.Capture (worker thread, size/mtime/content hash)
                     then BundleSession.Bundle = parsed bundle; history bookkeeping
```

`BundleSerializer.LoadAsync` maps failures to `BundleLoadError`:

| Error | Cause |
| --- | --- |
| `Missing` | The path does not exist (or is not a file). |
| `TooLarge` | File length exceeds 4 MiB. |
| `Unreadable` | `IOException` / `UnauthorizedAccessException` while opening or reading. |
| `InvalidJson` | Empty/whitespace text, or a `JsonException`. |
| `InvalidShape` | JSON parses but is `null` rather than a bundle object. |
| `NewerSchema` | `SchemaVersion` is greater than the build's version. |

A failed result never carries a partially loaded model, so callers keep the current session
untouched. `BundleLoadResult.CanRestoreBackup` is only computed on failure and tells the history
UI whether to offer recovery.

### 4.3 Normalization

`BundleSerializer.Normalize` repairs everything the UI relies on, on a detached model:

- null lists become empty collections; null entries inside a list become
  `MediaRecord.Placeholder` so alignment survives;
- `Name`/`Path` are trimmed; a blank name is derived from the path's file name;
- rows whose complete video/audio/subtitle triple repeats, and rows that are entirely empty, are
  removed — one keep-mask is applied to all three lists so alignment is preserved;
- `SelectedVideoIndex` is clamped to `-1`..`count-1` (and forced to `-1` for an empty video list);
- `Volume` is clamped to 0..100.

### 4.4 Save pipeline

```
FileMenuViewModel.Save/SaveAs
  → BundleFileService.SaveAsync/SaveAsAsync
      → BundleRepository.SaveToFileAsync
          → [save lock]
          → read the existing file (NewerSchema ⇒ refuse)
          → if the existing file parses: promote its text to the backup store
          → MediaFileFingerprint.Capture (size/mtime/content hash of every readable media file)
          → Serialize (sets SchemaVersion) → AtomicFile.WriteAllTextAsync
          → SelectedFilePath = path
```

Details that matter:

- `_saveLock` (`SemaphoreSlim`) serializes saves, because Save can be triggered from a command and
  from shutdown.
- `SaveAsync` with no selected path is a no-op; `SaveAs` is how a new bundle gets a path.
- A file written by a newer schema is never overwritten (`BundleSaveException`), so an older build
  cannot flatten data it does not understand.
- The backup is promoted only when the **current** file still parses. If it is already corrupt,
  the older backup is left alone so it remains restorable.
- If no backup slot exists yet, a valid legacy `<bundle>.bak` sidecar is imported once; the sidecar
  itself is left in place.
- A backup-write failure does **not** fail the save. The result carries a `Warning`, which
  `FileMenuViewModel` shows as non-blocking info text.
- The save takes a `BundleBackupMode`. Autosave passes `Keep`, so the recovery slot still holds
  the previous *deliberate* save; Save and Save as promote. A `Keep` save skips reading the
  current file and leaves the backup untouched, but keeps the `NewerSchema` guard and the
  atomic write.
- `BundleFileService` marks `IBundleEditTracker` clean after a successful save, using the
  revision it read before the write, so an edit that landed while the file was being written
  keeps the bundle dirty. The fingerprint capture inside the save runs with tracking suspended,
  because replacing records is not a user edit.
- Write failures are wrapped as `BundleSaveException` with a "Could not write …" message; the
  in-memory bundle is untouched, so the user can retry or Save as elsewhere.

### 4.5 Backup and quarantine layout

Both stores live under `AppConstants` (`%APPDATA%\mpvmux`):

- `backups/` — bundle backups are keyed by the SHA-256 of the full path
  (`<hash>.mpvm.bak`); on Windows the path is upper-cased first so differently cased spellings of
  the same file share one slot. Legacy `path.mpvm.bak` sidecars are still read.
- `corrupt/` — bundles that failed validation are moved to
  `<hash>-yyyyMMdd-HHmmss.mpvm.corrupt` (with a numeric suffix on collision), so the original is
  preserved rather than overwritten.
- `backups/config.json.bak`, `backups/history.json.bak` — one fixed slot per settings file.
- `corrupt/config-<stamp>.json.corrupt` and `corrupt/history-<stamp>.json.corrupt` — timestamped
  copies of damaged settings (same stem/extension convention as `SettingsBackupStore.Quarantine`).
  A quarantine failure returns `null` and the caller leaves the damaged original in place.

`SettingsBackupStore.ForFile` derives both folders next to the file it protects, which is what
lets tests point at a temporary directory and exercise the real logic.

### 4.6 Restore

`BundleRepository.RestoreBackupAsync`:

1. Read the stored backup (validated through `BundleSerializer`), or import a valid legacy
   sidecar.
2. If no backup validates, return the failure with `CanRestoreBackup = false`.
3. Quarantine the current (corrupt) file, preserving evidence.
4. Write the restored text atomically over the original.
5. Return the validated bundle and set the selected path — but `BundleFileService` immediately
   clears it again, so recovered content opens as **unsaved** and Save asks for a destination
   instead of silently overwriting the file that was just repaired.

### 4.7 Settings files and recovery reports

`ConfigService` and `HistoryService` are constructed before a window exists. Each:

- ensures the data folder exists (best effort), removes a stale `.tmp`, and then
  - treats a missing file as `Fresh` (defaults are written immediately);
  - otherwise reads and parses the file.
- On a parse/read failure it quarantines the damaged original, then:
  - restores the validated backup → `RecoveredFromBackup`;
  - otherwise falls back to defaults/an empty model → `ResetAfterCorruption`.
- Only writes the repair back when quarantine succeeded, because overwriting a locked damaged
  file would destroy the only copy.
- Normalizes what parsed: config fixes the mpv path, recent limit, shortcut overrides (known ids
  with parseable gestures) and global action ids, and migrates the legacy global switch; history
  drops null/blank entries, de-duplicates by path, puts pinned entries first, normalizes marker
  lists and recalculates `IsPinned` / `IsMissing` / `IsCorrupt`.
- Reports the outcome once through `TakeRecoveryReport()` (the report is cleared when read).

`MainWindowViewModel.ReportSettingsRecovery` drains both reports, keeps only those with
`NeedsUserAttention` (`RecoveredFromBackup` or `ResetAfterCorruption`), and builds a single
informational dialog naming the files, the quarantine folder and whether content was restored or
defaults are in use. This is why startup can survive a damaged JSON file without a crash or a
per-service popup.

Settings saves are synchronous (`AtomicFile.WriteAllText`) because they run from menu toggles and
window closing, which do not await. `Save`/`SaveHistory` return `false` instead of throwing; a
failed settings write never crashes shutdown, and the next save retries.

### 4.8 Atomic writes

`AtomicFile` writes to `<path>.tmp`, flushes to disk, then `File.Move(..., overwrite: true)` —
same directory, so the move is a rename. On failure it deletes the temp file and rethrows. The
temp file is deleted before every write (a crash may have left a half-written one) and cleaned up
again when a file is loaded. The async variant is used for bundles; the sync variant exists for
the settings files whose callers are UI paths.

## 5. User flows

### New, open, save, save as

`FileMenuViewModel` owns the File menu commands; the load itself is always routed through
`HistoryViewModel` so the history's load lock, generation check and backup prompt apply to every
entry point (menu, recent list, drag and drop, startup restore).

- **New…** asks for a destination first, then calls `BundleFileService.NewFile()` (fresh
  `MediaBundleModel`, selected path cleared) and saves it as that path. A failed save is reported
  and leaves the new, unsaved bundle in the session. On success the history selects the new path.
- **Open…** uses the bundle file picker and calls `HistoryViewModel.OpenAsync(path)`.
  `OpenAsync` takes the load lock and increments a generation counter; a load that was superseded
  by a newer one drops its result instead of applying it. `BundleFileService.LoadAsync` swaps
  `BundleSession.Bundle` **only** when the result validates, then adds the entry to history, sets
  `LastDatabasePath` and clears any corrupt marker.
- **Save** calls `SaveAsync` when a bundle path is selected and falls back to **Save as…**
  otherwise. Saves run through `TrySaveAsync`, which turns `BundleSaveException`, `IOException`
  and `UnauthorizedAccessException` into a "Could not save the bundle: …" dialog and keeps the
  in-memory bundle intact. A backup-update failure is shown as non-blocking info.
- **Save as…** writes to the chosen path, then selects it in history.

### Unsaved changes and autosave

`BundleEditTracker` marks the open bundle dirty for everything the file stores — the three
lists, the selected row and the volume — and resets to clean when a bundle is loaded, created
or restored, and after a successful save. Fingerprint capture inside a save runs suspended,
because it replaces records without being an edit.

While dirty, the window title shows a leading `*` and the bottom panel says "Unsaved changes".

When `Config.AutoSaveEnabled` is on, `BundleAutoSaveService` writes the bundle two seconds after
the last edit. It only writes a bundle that already has a path, verifies that the bundle
instance and the selected path are still the ones the edit belonged to (a pending write is
dropped when another bundle opens), and saves with `BundleBackupMode.Keep` so backups still
change only on an explicit save. A failure is shown in the bottom panel as
"Could not save automatically: …", never as a dialog, and the bundle stays dirty. A successful
write replaces that text with "Saved <time>".

Replacing the bundle (Open, recent list, drag and drop, New…) or closing the window runs
`UnsavedChangesGuard`. It flushes pending autosave first and asks only when the edits still
cannot be written: autosave is off, the bundle has no destination yet, or the write failed. The
question is Save / Don't save / Cancel. Save without a destination opens the Save as picker,
Cancel keeps the current bundle open, and a failed save is reported without closing anything.

Only one mpvmux runs per user (see "Single instance"), so a bundle's autosave has a single
writer. The `NewerSchema` refusal still prevents an older build from flattening a file written
by a newer one; two users editing one bundle in a shared folder are last-writer-wins.

### Opening a damaged bundle

When `BundleRepository.LoadFromFileAsync` fails and a backup validates
(`CanRestoreBackup == true`), `HistoryViewModel` asks "Restore bundle backup" with the validation
message. Restoring quarantines the damaged file, writes the backup and opens the result as
unsaved; the confirmation explains that Save as is needed to keep the repaired bundle. Declining
marks the entry corrupt (only `InvalidJson`, `InvalidShape` and `TooLarge` count as damage — a
locked or missing file does not) and shows no second dialog. Without a usable backup, the entry
is marked corrupt and an error dialog appears.

### History ordering and state

The history list is a single ordered collection with an invariant: **pinned entries form the
prefix**, unpinned entries follow newest-first.

- Opening an entry: a pinned entry keeps its position (curated by hand); an unpinned entry moves
  to the top of the recent group; a new entry is inserted directly below the pinned group.
- Pinning moves an entry to the top of the pinned group; unpinning moves it to the top of the
  recent group. `PinnedPaths` is updated in step, so the order survives a restart.
- Rename changes only the display name; the bundle keeps its file name and path.
- Delete removes the entry and its markers. If it was the open bundle, the next visible entry is
  selected and loaded, or a new bundle is started when the list becomes empty.
- The Manage window (`RecentBundlesViewModel`) groups pinned/recent, searches name and path with
  a 150 ms debounce, and shows counts for missing and damaged entries. Manual reordering is only
  allowed inside the same group.
- Missing (`IsMissing`) means the file is not on disk right now; damaged (`IsCorrupt`) means the
  bundle failed parse/shape/size validation. Both are recalculated by
  `IHistoryService.RefreshAvailability()` when a window opens, so files deleted or restored while
  the app runs are reflected.

### Re-linking

A missing record can be re-pointed at a file:

- Single re-link asks for an existing `.mpvm` and calls `HistoryService.RelinkEntry`. If the new
  path is already another history entry, the two are merged into one (pin wins, corrupt flag
  cleared) instead of leaving a duplicate.
- Bulk re-link asks for a folder, then `BundleRelinkService.FindByNameAsync` scans it recursively
  for bundles whose file name matches a missing entry. Reparse points/symlinks are skipped to
  avoid cycles, name matching is case-insensitive, and names with more than one match are counted
  as ambiguous rather than guessed. The summary reports re-linked and unresolved counts.
- "Remove missing…" confirms first and re-checks availability so a file that reappeared is not
  removed.

### Missing media and rename detection

Media references are absolute paths, so a file renamed outside mpvmux leaves a dead cell. The
grid reports those cells instead of letting mpv fail silently:

- Detection runs after a bundle opens and after any of the three lists changes, debounced by
  150 ms and cancelled by the next edit; there is no filesystem watcher.
  `MediaRelinkService.DetectAsync` is read-only and returns one `MediaRelinkProposal` for every
  missing cell (including ones with no match), each with up to five ranked
  `MediaRelinkCandidate` values.
- Missing cells are muted and italic with an alert icon; the banner above the grid counts them
  and offers **Review matches…** when at least one proposal has a candidate. Play on a missing
  video reports "Could not play '<name>': the file is missing.".
- Ranking, strongest first: `CaseOnly` (the same path, different casing), `ContentHash` (identical
  sampled content, so a rename of the name, folder, extension or timestamps still matches;
  candidate hashing only runs when the lengths are equal, which is part of the hash) and
  `ExactName` (same file name anywhere under the original folder; the recursive pass skips
  reparse points). There is deliberately no fuzzy name matching: `S01E01` and `S01E02` are one
  character apart and a wrong pairing is invisible until the user watches the wrong file, so a
  re-encoded file is only found when its name is unchanged.
- A file that another cell still references is never proposed, and greedy assignment by score
  gives one file to at most one cell. Competing candidates stay in a cell's list — unless the
  cell has a definitive match: a case-only path or an identical content hash collapses the list
  to those matches. Every bundle opened by this build has hashes for its present files (§4.2),
  so content is the normal path; `ExactName` covers bundles recorded before hashing existed and
  files re-encoded since.
- Nothing is rewritten before the user confirms in `RelinkMediaWindow`.
  `MediaRelinkService.Apply` re-checks the cell's original path (an edit made while the dialog
  was open wins), verifies the target still exists and refreshes the display name and
  fingerprint. The bundle stays unsaved, so the change reaches disk only through Save.

### Drag and drop

`Controls/TrackTable.axaml.cs` owns the drag/drop wiring (Avalonia event handlers are code-behind
by convention):

- Before any row exists, the whole empty table targets the video column. Afterwards the three
  equal-width columns are hit-tested by pointer X, and a drop overlay shows the target.
- `TrackTableViewModel.LoadDroppedAsync` first asks `BundleDropClassifier.FindBundles` for every
  dropped `.mpvm` in drop order. A bundle **always** opens as a bundle, from any column, even when
  the drop also contains media, and joins the history like File → Open.
- A single bundle opens directly. A drop with several bundles first registers all of them through
  `IBundleFileService.RememberBundles` (so they appear in the recent list without being loaded),
  then opens the `BundleChoiceWindow` picker: name plus full-path tooltip per bundle, first
  dropped bundle preselected, Enter/Open loads the selection, Cancel opens none. The picked
  bundle opens through the same `OpenAsync` path, which promotes it to the top of the recent
  group. They are registered in reverse drop order because every `AddEntry` lands at the top of
  the recent group, so the drop order survives.
- Cancelling the picker is lossless: history keeps every dropped bundle, and the previously open
  bundle stays open.
- Otherwise the drop is filtered by the target column's extension list (`AppConstants`), expanded
  by `MediaFileScanner`: dropped folders are scanned **one level deep**, dropped files must match
  the column, results are de-duplicated case-insensitively, sorted, and use the file name without
  extension as the display name. Loading happens on a background task.
- An empty result shows a message and leaves the existing column untouched (it never silently
  wipes the loaded list). A successful drop replaces that column; loading videos resets the
  selection to row 0.
- Row-level commands replace a single cell through the file picker. `SetAt` pads with
  `MediaRecord.Placeholder` so filling a later row first does not break alignment. Delete removes
  the video row together with its aligned audio/subtitle cells.

### mpv setup

`MainWindowViewModel.EnsureMpvConfiguredAsync` probes the configured value, then searches the
usual locations. `MpvLocator` accepts an executable, the folder containing it, or a bare command
name (PATH lookup), expands environment variables, and verifies the candidate by running
`--version` with a 3-second timeout. Because mpv is a GUI-subsystem binary on Windows, empty
`--version` output is accepted only when the file is actually named `mpv`; otherwise the process
must exit 0 with an `mpv v…` first line.

The setup window re-runs detection, lets the user pick a folder or executable, validates the
choice before saving, and links to the mpv installation page. Dismissing the window counts as
"not now" and sets `MpvSetupCompleted`, so it does not reappear on every start; it stays reachable
from the File menu.

### Update check

`UpdateService` performs a `GET` on `AppMetadata.APILink` (a single shared `HttpClient` with the
app title as user agent), reads `tag_name`, `html_url` and `body`, trims a leading `v`/`V` from
the tag, and returns `null` when the local `AppMetadata.Version` is greater than or equal to the
published version. The manual command distinguishes no-internet, repository-not-found and
generic failures; the optional startup check is silent except for an available update, so a
network problem never interrupts startup.

## 6. Playback

### One mpv process per session

`PlayerService` is the only place that starts mpv. `LaunchMPV(bundle)`:

1. Validates the selection through `bundle.TryGetSelectedTracks`; no playable video throws
   `ArgumentException`.
2. Returns silently when a process is already running — mpvmux never runs two players.
3. Builds `ProcessStartInfo(Config.MpvPath)` with `UseShellExecute = false`, appends the
   arguments below plus `--input-ipc-server=<path>`, attaches the `Exited` handler and sets
   `EnableRaisingEvents` before `Start()`.
4. Stores the process and IPC path, then raises `IsPlayingChanged`.

`IsPlaying` is simply `_currentProcess is not null`.

### Arguments

`PlayerService.BuildArguments(bundle, fullscreen)` is the authoritative construction:

| Order | Argument | Why |
| --- | --- | --- |
| 1 | `--fullscreen` (optional) | Mirrors `Config.EnableFullscreen`. |
| 2 | `--no-sub-auto` | The track grid is authoritative; mpv must not load a sidecar subtitle that is not in the table. Embedded subtitles remain mpv's business. |
| 3 | `--volume={bundle.Volume}` | 0..100; volume 0 is the mute state (`MediaBundleModel.IsMuted`). |
| 4 | `<video path>` | The selected row's video. |
| 5 | `--audio-file=<path>` (optional) | Only when the aligned audio cell has a usable path. |
| 6 | `--sub-file=<path>` (optional) | Only when the aligned subtitle cell has a usable path. |
| 7 | `--input-ipc-server=<path>` | Added by `LaunchMPV`, not by `BuildArguments`. |

### IPC and pause

The IPC endpoint is created per process: `\\.\pipe\mpvmux-<guid>` on Windows, a
`mpvmux-<guid>.sock` file under the temp directory elsewhere. Pause sends the JSON line
`{"command":["cycle","pause"]}`:

- Windows: `NamedPipeClientStream` (the `\\.\pipe\` prefix is stripped), connect timeout 200 ms.
- Unix: `Socket(AF_UNIX)` to a `UnixDomainSocketEndPoint`, written through a `NetworkStream`.

`PauseAsync` retries up to 20 times with a 50 ms delay, but only for `IOException`,
`TimeoutException` and `SocketException`: mpv creates the endpoint shortly after launch, so a
pause pressed immediately after Play needs a few attempts. Failures after the retry budget are
surfaced by `BottomPanelViewModel.Pause` as "Could not pause mpv: …".

### Stop versus natural exit

This distinction drives autoplay:

- `Stop()` sets `_stopRequested` and kills the process if it is still alive. Already-exited and
  permission errors are swallowed; the exit handler still clears state.
- `StopAsync()` waits on the shared exit signal, bounded by a 3-second timeout so a stubborn
  process cannot hang the caller. The Next/Previous commands use it before launching the newly
  selected row.
- `OnMpvExited` unsubscribes and disposes the process, clears `_currentProcess`/`_ipcPath`,
  signals all waiters and then posts to the UI thread. On the UI thread:
  - if the exit was **not** caused by Stop, `EnableAutomaticPlayback` is on and
    `bundle.TryIncrement()` advances the selection, the next row is launched automatically; if
    that launch succeeds, no idle state is published;
  - otherwise `IsPlayingChanged` is raised.
- Because a user-initiated stop sets the flag, pressing Stop never advances the queue. Autoplay
  exceptions are swallowed, and `IsPlayingChanged` is always raised on the UI thread so bindings
  observe a consistent state.

### Bottom panel state

`BottomPanelViewModel` mirrors the player and the session:

- Commands are gated by `CanPlay` (not playing and a playable selection), `IsPlaying`, and the
  selection bounds (`Previous` needs index > 0, `Next` needs a later video row).
- `PlaybackPosition` is "n / total" for a valid selection; `PlaybackHint` explains the next
  required action ("Playing in mpv", drag/load a video, or select one). Audio/subtitle cells
  never block playback.
- `SkipAsync` changes the selection first; when something is playing it then stops the current
  process and launches the new row. Launch failures are reported as "Could not start mpv: …" and
  point at File → Set up mpv when the executable looks missing or unlaunchable.
- The VM subscribes to `BundleSession.PropertyChanged` (a restored/replaced bundle re-attaches
  the collections), to the current bundle's `PropertyChanged`/`VideoList.CollectionChanged`, to
  `PlayerService.IsPlayingChanged`, and to `ConfigModel.PropertyChanged` for the autoplay and
  fullscreen mirrors. Replacing a list during deserialization invalidates the old collection
  subscription, so the handler re-subscribes to the new instance.

## 7. Shortcuts

### Action catalog and persistence

`ShortcutActions.All` is the ordered catalog used by Preferences, validation and defaults:

| Id | Label | Default |
| --- | --- | --- |
| `File.New` | New bundle | `Ctrl+N` |
| `File.Open` | Open bundle | `Ctrl+O` |
| `File.Save` | Save bundle | `Ctrl+S` |
| `File.SaveAs` | Save bundle as | `Ctrl+Shift+S` |
| `Playback.Play` | Play | `Ctrl+Enter` |
| `Playback.Pause` | Pause | `Ctrl+P` |
| `Playback.Stop` | Stop | `Ctrl+Shift+Enter` |
| `Playback.Previous` | Previous video | `Ctrl+Left` |
| `Playback.Next` | Next video | `Ctrl+Right` |
| `Playback.Autoplay` | Autoplay | `Ctrl+Shift+A` |
| `Playback.Fullscreen` | Start playback in fullscreen | `Ctrl+Shift+F` |
| `Help.CheckForUpdates` | Check for updates | `F5` |
| `Help.About` | About mpvmux | `F1` |

`Id` is the persistence key in `ConfigModel.ShortcutOverrides` and
`ConfigModel.GlobalShortcutActions`, so it must never change. Only gestures that differ from the
default are persisted, which lets a future default reach users who never customized that action.
`ShortcutActions` intentionally has no Exit entry: `Alt+F4` is handled by the window manager.

### Resolution and validation

- `ShortcutService.GetGesture(id)` returns the stored override only when it parses **and** passes
  `IsAllowed`; otherwise it falls back to the default. A hand-edited or stale value can never
  leave the app with an unusable gesture.
- `IsAllowed` rejects `Alt+F4` (reserved for closing the window) and requires Ctrl or Alt as a
  modifier, unless the key is F1–F24.
- `Apply(gestures, globals, out error)` is all-or-nothing: it validates every action's effective
  gesture, rejects a duplicate gesture case-insensitively with "… is already assigned to …",
  filters globals to known action ids in display order, assigns **new** dictionaries/lists (so
  `ConfigService` sees a change), saves, and raises `Changed`. Nothing is persisted when any
  gesture is invalid.
- `ConfigService.Normalize` independently drops unknown action ids and unparseable override text,
  so a damaged `config.json` cannot smuggle in stale actions.

### Preferences editing

`PreferencesWindowViewModel` snapshots the effective gestures into one
`ShortcutEntryViewModel` per action (gesture plus the global checkbox) and edits only those
pending values. `TrySetShortcut` validates a captured gesture and rejects a clash with another
pending row; Backspace resets a row, Reset all restores every default gesture. On Save,
`TryApplyAsync` validates the mpv path first, then applies shortcuts before the other settings so
a rejected gesture cannot leave a half-applied config. If the global hook cannot start, the
shortcuts are re-applied with an empty global set and the checkboxes are cleared, rather than
leaving the app configured to retry a setup that cannot work.

While the user is capturing a gesture, `BeginShortcutCapture`/`EndShortcutCapture` suspend and
resume the global hook; otherwise pressing a combination that is already global would fire it
instead of recording it.

### Local versus global

- **Local** shortcuts are ordinary Avalonia `KeyBinding`s owned by `MainWindow`.
  `MainWindowViewModel.CreateShortcutBindings` builds the list from `ShortcutGesturesViewModel`
  and **omits actions whose scope is global**, because the hook handles those. Handling a global
  action locally as well would fire its command twice. `MainWindow.RebuildKeyBindings` runs on
  `DataContextChanged` and on `ShortcutService.Changed`, which is raised by Apply, Refresh and a
  hook failure.
- **Global** shortcuts are captured by `GlobalShortcutHook` through SharpHook's
  `SimpleGlobalHook`. Bindings are keyed by `(KeyCode, normalized EventMask)`; modifier keys are
  ignored as triggers, and a held-key set prevents auto-repeat from firing an action repeatedly.
  Matches are posted to the UI thread as `Triggered(actionId)`, and
  `MainWindowViewModel.OnShortcutTriggered` maps the id to the same command the local binding
  would execute.
- Suppression (`e.SuppressEvent`) is only available on Windows and macOS; on Linux other
  applications still receive the combination, and Wayland may need elevated privileges. macOS
  asks for Accessibility permission on first use. These limitations are stated in the Preferences
  help text.
- A hook that faults asynchronously raises `Failed`, which becomes
  `ShortcutService.GlobalShortcutFailed` (shown by `MainWindowViewModel`) and
  `ShortcutService.Changed`, so the window immediately takes the affected shortcuts back over.
- `RefreshGlobalMode` stops the hook when nothing is global or while suspended, and otherwise
  restarts it with the gestures of the global actions. It is called after startup and after
  Preferences saves.

## 8. UI composition and config sync

### Window and control layout

- `Views/*.axaml` — the main window, Preferences, Setup, About, Updater, Manage recent bundles and
  the multi-drop bundle picker, plus the generic `ConfirmWindow`, `MessageBoxWindow` and
  `RenameWindow` that `WindowService` opens for confirmations, messages and renames.
- `Controls/*.axaml` — the main-window sections: `TopMenu`, `BottomPanel` and `TrackTable`.
- `MainWindow.axaml` declares `x:DataType="vm:MainWindowViewModel"`; the project enables
  `AvaloniaUseCompiledBindingsByDefault`, so bindings are `{CompiledBinding …}` and checked at
  build time. There is no reflection-based binding path in the app.
- `MainWindow.Title` binds to `MainWindowViewModel.WindowTitle` (the bundle name, with a leading
  `*` while there are unsaved edits), and `BottomPanelViewModel.SaveStatus` shows
  "Unsaved changes", the last automatic save time or the autosave failure in the free centre
  column of the bottom panel. A save icon in the bottom panel's indicator group (next to the
  autoplay and fullscreen icons) is visible while autosave is enabled.

Code-behind is limited to wiring that cannot live in XAML or the VM:

- `MainWindow` rebuilds `KeyBindings` when the data context or shortcuts change, because gestures
  are user-configurable.
- `TrackTable` registers drag/drop handlers, maps the pointer X to a column (or the whole table to
  video before the first row exists), shows the drop overlays, and handles the Delete key for the
  selected row.
- `RecentBundlesWindow` forwards context-menu and key-down gestures to commands.
- `PreferencesWindow` starts/stops shortcut capture (which suspends the global hook) and forwards
  the captured gesture to the VM.
- The small dialogs handle their own OK/Cancel/Rename buttons and close with the result.

### Main-window view-model graph

`MainWindowViewModel` is the window's data context and composes the singleton sub-view-models:

| View model | Owns |
| --- | --- |
| `TopMenuViewModel` | Menu-level commands (theme, autoplay/fullscreen mirrors, open bundle folder, MPV setup, updates, about) and re-exposes the shared VMs as `FileMenu`, `History`, `Playback`, `Shortcuts` for XAML. |
| `FileMenuViewModel` | New/Open/Save/Save as commands; delegates to `IBundleFileService` and routes opens through history. |
| `HistoryViewModel` | History projection, load lock/generation, dropdown selection, pin/rename/delete commands, Manage-window launch. |
| `TrackTableViewModel` | Row projection and editing, column loads, drag/drop entry point, row selection. |
| `BottomPanelViewModel` | Playback state and commands (play/pause/stop/previous/next) plus position and status hints. |
| `ShortcutGesturesViewModel` | Bindable current gestures for menu hints; refreshes on `IShortcutService.Changed`. |

Each is registered as a singleton because `TopMenuViewModel` and other surfaces must share the
**same** instance; creating a second `HistoryViewModel` would duplicate its load lock and
selection. Dialog view models are resolved by `WindowService` as needed (transient, except
About).

### Track rows

`TrackRow` is a rebuild-on-change projection, not a live wrapper: it captures the row index and
the three aligned `MediaRecord`s, exposes display names (`—` for an empty cell), full-path
tooltips, `HasVideo/HasAudio/HasSubtitle`, `IsPlayable`, and forwards the remove/replace commands
to `TrackTableViewModel`. `HasSameData` lets `RebuildRows` replace only rows whose underlying
records changed.

`RebuildRows` sizes the row list to the longest of the three collections, so orphan audio or
subtitle entries remain visible; a row without a video is not playable. List replacement during
deserialization invalidates old collection subscriptions, so the VM re-attaches to the new
collections and rebuilds. Selection is synchronised in both directions with a pair of guard
flags: selecting a row without a video clears `SelectedVideoIndex` (the row stays highlighted so
the orphan is visible), and changing `SelectedVideoIndex` elsewhere moves the grid selection.

### History projections

`HistoryViewModel` keeps two projections:

- `Entries` — the full history in service order (pinned prefix, then recents by use), used by the
  Manage window and selection lookup.
- `QuickEntries` — the dropdown projection: available entries only (`!IsMissing && !IsCorrupt`),
  with every pinned entry plus at most `Config.RecentLimit` unpinned entries.

`CurrentBundleName` falls back from the history record's display name to the selected file's name
and finally to "Select a bundle". `RecentLimit` changes rebuild the projection immediately.

### `ConfigModel` as the single source of truth

No view binds directly to the config object. View models keep observable mirrors, seed them from
the config, and write changes back:

- `TopMenuViewModel` seeds `IsDarkTheme` / `IsAutoplayEnabled` / `IsFullscreenEnabled`, writes the
  config and saves in the generated `On…Changed` hooks (theme also calls `ThemeManager.Apply`),
  and listens to `ConfigModel.PropertyChanged` so Preferences, the tray and shortcuts are
  reflected in the menu checkmarks.
- `BottomPanelViewModel` mirrors autoplay and fullscreen; `HistoryViewModel` rebuilds on
  `RecentLimit`; `PreferencesWindowViewModel` snapshots every value it edits and subscribes to the
  config while open, so an external change (tray, Playback menu, shortcut) is not silently
  reverted by Save.
- The tray in `App` builds its menu manually and pushes check state in both directions:
  click → config → `Save`, and config change → `SyncTrayMenuItems()`. On Windows
  `NativeMenu.NeedsUpdate` is not raised for the managed flyout, which is why the explicit
  `PropertyChanged` subscription exists; `NeedsUpdate` covers the platforms where it fires.
- Shortcuts follow the same rule through `IShortcutService`: Preferences applies through the
  service, and the service updates `ConfigModel` with new collections, saves, and raises
  `Changed` so menus and the window rebuild.

### Command and notification conventions

- Commands are generated `[RelayCommand]` members; `CanExecute` methods derive from live state and
  the VMs call `NotifyCanExecuteChanged` when that state changes (for example
  `BottomPanelViewModel.RefreshState`).
- Services never raise UI events from background threads: `PlayerService` posts exit handling to
  `Dispatcher.UIThread`, `GlobalShortcutHook` posts trigger/failure events, and
  `ShortcutService` forwards them unchanged.
- User-visible errors always go through `IDialogHelper`, keeping the message wording in one
  place and making VMs testable with a fake.

## 9. Where to make a change

### Add a setting

1. Add an `[ObservableProperty]` to `ConfigModel` (keep the existing JSON name when renaming, or
   annotate with `[property: JsonPropertyName(...)]`).
2. Constrain it in `ConfigService.Normalize` if it has a valid range or an old value must be
   migrated.
3. Seed an observable mirror in every surface that shows it (`TopMenuViewModel`,
   `BottomPanelViewModel`, `PreferencesWindowViewModel`), write it back in the generated
   `On…Changed` hook, and subscribe to `ConfigModel.PropertyChanged` for external edits.
4. If the setting changes behavior outside the UI (playback, shortcuts, update check), consume it
   in the owning service rather than passing it through several layers.
5. Cover the mirroring with a test in the style of `PreferencesSyncTests`.

### Add a shortcut action

1. Add a constant and an `All` entry in `ShortcutActions` with a **stable** id, a sentence-case
   label, and a default gesture that passes `IsAllowed`.
2. Expose the gesture in `ShortcutGesturesViewModel` if a menu hint needs it.
3. Map it in `MainWindowViewModel.CreateShortcutBindings` (local) and in the
   `OnShortcutTriggered` switch (global), pointing at the same command.
4. Persistence, validation, normalization and the Preferences list pick it up automatically.
5. Update the shortcut table in `README.md` if the default is user-facing.

### Add a dialog

1. Add `Views/<Name>Window.axaml` (and code-behind for button wiring only) and
   `ViewModels/Dialogs/<Name>ViewModel.cs`.
2. Register the view model as transient in `AddCommonServices`.
3. Add a method to `IWindowService` and open the window with `ShowDialog(Desktop.OwnerWindow)`,
   assigning the resolved view model as `DataContext`.
4. Keep user-facing text in the "Could not …" / sentence-case style from `AGENTS.md`.

### Add or change a persisted file

1. Serialize to indented JSON through the existing models; write with `AtomicFile`.
2. Give the file a backup slot and a quarantine path, following the bundle or settings
   conventions, and derive the folders through `SettingsBackupStore.ForFile` so tests can point at
   a temp directory.
3. Expose a one-shot `TakeRecoveryReport()` when the file is loaded at startup and the user should
   learn about a failure.
4. Never place sidecar files next to the user's media; all app state belongs under
   `AppConstants.BaseFolderPath`.
5. Add tests for fresh/damaged/recovered/unrecoverable loads.

### Change the bundle schema

1. Increment `BundleSerializer.CurrentSchemaVersion` and the `MediaBundleModel` default together.
2. Keep reading the previous shape: normalize missing fields instead of rejecting the file.
3. Never write a file with a newer schema — `BundleRepository` already refuses to overwrite one.
4. Add a load test for the old shape and a save test for the new one.

### Add a media extension

Add it to the relevant array in `AppConstants`. The pickers, drag/drop filtering and folder
scanner all read those arrays, so no other change is needed.

### Change the rename heuristics

1. Detection has three rules in `MediaRelinkService.Score`: case-only path, identical sampled
   content, unchanged file name. A new rule needs its own score band in `ScoreFor`.
2. Prefer content over names. A fuzzy name rule would also need a digit guard and a review step,
   because episode numbers are one character apart and a wrong pairing is invisible until the
   user watches the wrong file.
3. Hashes are sampled (length plus both ends) on purpose: whole-file hashing of a film library
   would make open and save unusable. A rule that needs more bytes must say why the extra IO is
   affordable, and the reuse-by-size/mtime check in `MediaFileFingerprint.Capture` must stay.
4. Cover the rule in `MediaRelinkTests`; the heuristics are defined against real files in a GUID
   temp directory, so create both the missing reference and the candidate on disk.

## 10. Tests and test seams

The xunit project `tests/mpvmux.Tests` references the app and uses `InternalsVisibleTo`, so tests
can exercise `internal` types directly. The suite is deliberately focused on logic that is hard
to verify by looking at the UI:

| File | Covers |
| --- | --- |
| `BundleCorruptionTests` | Save/load/restore, backup promotion, quarantine, failed backup writes (a `FailingWriteStore` wraps the real store), schema and validation errors. |
| `StorageCorruptionTests` | `config.json`/`history.json` fresh/loaded/recovered/reset outcomes, backup slots, quarantine copies, one-shot recovery reports. |
| `BundleDropTests` | `BundleDropClassifier` multi-bundle detection, drop order, case-insensitivity and non-bundle drops. |
| `BundleDropHistoryTests` | Multi-bundle drop registration: recent-list order, existing entries staying behind the dropped group, idempotence. |
| `BundleChoiceTests` | Multi-bundle picker view model: first bundle preselected, name/path projection, selection changing the chosen path. |
| `PreferencesSyncTests` | Config mirroring in view models, using fake `IConfigService`/`IShortcutService` implementations. |
| `MediaRelinkTests` | Missing-media detection (case-only, content hash, exact name, one-to-one assignment, present-path exclusion), apply re-verification and fingerprint persistence. |
| `BundleEditTrackerTests` | Dirty tracking on list, selection and volume edits, suppression, list replacement, bundle swap, revision-guarded clean-after-save. |
| `AutosaveTests` | Debounce and burst coalescing, no write when disabled or without a destination, pending write dropped on bundle swap, backup slot kept until an explicit save, failure reporting, flush. |
| `UnsavedChangesGuardTests` | Flush-then-prompt decisions: clean bundle, autosaved, Cancel, Discard, Save, Save as without a destination, failed save. |

Test conventions:

- Temp directories live under `Path.GetTempPath()` with a GUID suffix; tests never touch the real
  `%APPDATA%\mpvmux`.
- `ConfigService` and `HistoryService` accept a file path (and a `SettingsBackupStore`), which is
  the seam that makes the real services testable.
- Fakes are private nested classes implementing the service interfaces; there is no mocking
  framework.
- Tests do not start the GUI or assert layout/visuals. Visual behavior is verified by the user;
  see `AGENTS.md` for the run/test commands and the environment variables required in this
  checkout.
