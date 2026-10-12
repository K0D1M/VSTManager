# VST Manager — Handoff

_Last updated: 2026-10-12. Version in csproj: **1.8.2**. Repo: `K0D1M/VSTManager` (public)._

VST Manager scans the user's audio plugins (VST2, VST3, AAX, CLAP, AU), groups the copies of
each plugin into one card, looks up newer versions online (KVR), and lets the user tag,
categorise, favourite, hide and file them into folders. Settings can sync through the cloud
(MEGA or Google Drive).

There are **two desktop apps over one shared core**. Almost all recent work is in the
cross-platform Avalonia app; the WPF app is the original Windows-only app and is kept working.

---

## 1. Repository layout

```
VstManager/
├─ src/
│  ├─ VstManager.Core/                 net10.0 — all domain logic, no UI. Shared by both apps.
│  ├─ VstManager.Ui/                   net10.0 — Avalonia 12.1.1 app (Windows + macOS). MAIN APP.
│  ├─ VstManager.Ui.WindowsNotifications/  net10.0-windows10.0.19041.0 — native Windows toasts,
│  │                                    built by VstManager.Ui and loaded only on Windows.
│  └─ VstManager.App/                  net10.0-windows — original WPF app (Windows only).
├─ build/        macOS packaging + icon scripts and icon master PNG
├─ artifacts/    publish output (gitignored)
├─ tests/        only stale bin/obj — there are currently NO tests
└─ VstManager.slnx
```

User data (shared by both apps): `%APPDATA%\VstManager\` (macOS: `~/.config/VstManager/`)

| File | Contents |
|---|---|
| `library.json` | Every plugin found (path, format, versions, tag, kind, category, flags) + all settings |
| `plugin-tags.json` | Tag assignments (manual + auto from KVR categories, with suppressions) |
| `plugin-folders.json` | Folder tree and which plugin is filed where |
| `manual-metadata.json` | **Hand-set** name/vendor/current/latest version per plugin (overrides win over everything) |
| `manual-logos.json`, `logos/` | Artwork overrides and the logo cache |
| `lookup-cache.json` | Cached KVR results (fresh 7 days; "not found" 2 days) |
| `excluded-files.local.json` | Paths the user removed from scanning |
| `google-client.json`, `gdrive-token/` | Google Drive OAuth client + saved login (secrets, never commit) |

---

## 2. Build, run, publish

```powershell
dotnet build src/VstManager.Ui                       # Debug, Windows
dotnet build -r osx-arm64 src/VstManager.Ui          # proves it compiles for macOS (cannot run here)
dotnet build -c Release src/VstManager.Ui
dotnet build src/VstManager.App                      # WPF app — build it after any Core change
dotnet publish src/VstManager.Ui -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

- **Kill the running app before building** (`taskkill //F //IM VstManager.Ui.exe //T`) or the
  copy step fails with a locked DLL.
- The user usually runs the **self-contained publish** (`artifacts/win-x64/VstManager.Ui.exe`) or
  the Release build — after a change, republish/rebuild Release too, not just Debug.
- **NoStatus variant**: `-p:NoLicenseStatus=true` compiles out the Legit/Cracked feature.
  C# uses `#if !NO_LICENSE_STATUS`; XAML uses `<!--#if LICENSE_STATUS-->…<!--#else-->…<!--#endif-->`
  comment markers that an MSBuild step strips before Avalonia compiles (output in
  `obj/…/conditional-xaml/`). Keep markers balanced or the build errors.
- macOS app bundle: `python build/make-app.py` (assembles `.app`, never launched — no Mac).
- App icon: `python build/make-icon.py` then `python build/make-icns.py` (§6).

---

## 3. Architecture

### Core (`src/VstManager.Core`)
Pure .NET, no UI types. Key services:

| Service | Role |
|---|---|
| `ScanPathProvider` | Default plugin folders per format and OS (Windows paths; macOS `/Library/Audio/Plug-Ins/…` + `~/Library/…`; AU on macOS only) |
| `PluginScanner` | Walks folders. Windows: files (`.dll`, `.vst3`/`.aaxplugin` bundles de-duplicated to the inner binary, `.clap`). macOS: the bundle **folder** is the plugin (`OutermostBundles`). Reads vendor + installed version on every scan. |
| `PluginVersionDetector` | Version from the file's version resource (prefers the longer FileVersion when ProductVersion is a truncated prefix, e.g. Pigments "7.0" vs "7.0.1.6772"), then VST3 `moduleinfo.json`, UA manifest, macOS `Info.plist` |
| `VendorDetector` | Vendor from moduleinfo / AU component name / version resource / UA manifest / parent folder |
| `BundleInfoPlist` | Minimal XML `Info.plist` reader (binary plists → null) |
| `LibraryStore` | Loads/saves `library.json`; `MergeOnRescan` keeps user fields across scans. **Every user-settable field must be copied there** or a rescan silently resets it. File's own version now wins over the stored one. |
| `PluginDisplayBuilder` | Groups installs into display items (catalog match or normalised name), applies manual overrides and tags |
| `KvrLookupService` | KVR search/product parsing (uses `curl`), plus "Get Info" from any URL |
| `ManualMetadataOverrideService` | Hand-set name/vendor/versions. `SetVersionOverride(…, setCurrent, setLatest)` pins only the fields asked for. |
| `PluginTagService`, `PresetTags`, `CategoryTagMapper` | Tags and KVR-category → tag mapping |
| `PresetCategories` + `CategoryDefinition` | N-way categories (Instrument/Effect presets + custom, user-ordered) |
| `PluginFolderService` | Folder tree, filing, cycle guard |
| `Cloud/*` | `CloudSyncService` (upload/download/conflict), `ICloudSyncProvider`, `MegaS4SyncProvider`, `GoogleDriveSyncProvider` (appDataFolder, OAuth loopback), `SwitchableCloudProvider`, `GoogleDriveConfig` |

### Avalonia app (`src/VstManager.Ui`)
MVVM with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`); compiled bindings on.

- **`ViewModels/MainViewModel.cs`** (~3,000 lines) owns almost everything: scan/load, filtering
  and sorting (`ApplyFilters` → `MatchesFilters`), sections, folders, sidebar views, update checks
  (plugin + app self-update), cloud sync, settings persistence. Dialog-free: it raises events and
  the windows open dialogs.
- **`MainWindow.axaml(.cs)`**: sidebar + header + toolbar + grid/list of cards, sticky section
  headers, row-snapping wheel scroll, zoom (Ctrl +/−/0, `Services/UiScale`), drag-and-drop to
  folders, startup width fitting.
- **Views/**: PluginDetailWindow (edit, versions, KVR lookups, artwork, Files tab),
  SettingsWindow (General, Appearance, Scanning, Tags, Categories, Cloud, Data, About),
  UpdateAvailableWindow, ThumbnailEditorWindow (crop/zoom/pan), ClassifyPluginsWindow,
  CloudConflictWindow, MatchPickerWindow, FolderEdit/FolderPicker, WelcomeWindow, ConfirmDialog,
  LoadingSpinner (custom; the NuGet one targets Avalonia 11 and crashes on 12).
- **Themes/**: `Palette.axaml` (dark/light colour tokens via ThemeDictionaries; `AccentBrush`,
  `AccentTintBrush`), `Controls.axaml` (all control styles — global), `Icons.axaml` (stroke
  geometries; mostly hand-drawn, gear from Lucide/ISC). `ThemeService` applies theme + user accent.
- **Services/**: UpdateChecker (GitHub releases, progress download), NotificationService,
  TrayIconService, AutostartService, Reveal, UiScale.

### WPF app (`src/VstManager.App`)
Original app; shares Core and the same data files. Gets only Core-level fixes plus a few ports
(AAX/CLAP scanning and labels, version-pin fix). It does **not** have categories, sidebar, AU, etc.

---

## 4. Current state (what exists and works)

Main window (Avalonia), as of the latest work:
- **Left sidebar** (just built): logo + name; **views** All plugins / Favourites / Updates available
  / Hidden with live counts (selected = accent tint); **folders** tree (+ new folder, right-click
  menu, drag-and-drop filing); **Settings** (gear) and **Collapse** at the bottom. Collapses to a
  68 px icon rail; choice saved (`LibraryData.SidebarCollapsed`); auto-rail when the content width
  < 1000 px (`IsWindowNarrow`). Views are single-choice filters implemented over
  `FavoritesOnly` / `OutdatedOnly` + a hidden-only branch in `MatchesFilters`; picking a folder
  resets the view to All and the header shows "Folders / Name".
- **Header**: page title (view or folder) + status line; Rescan, Theme (◐), Sync (cloud, when
  sync on), Quit. Every toolbar/header button has a tooltip.
- **Toolbar**: search, All/Legit/Cracked, Grid/List, Compact/Default/Large (with size icons),
  Filter popup (status, format incl. AAX/CLAP/AU, show hidden, tags), Sort + **⇅** toggle (lit
  while descending), Mark All, Refresh All Metadata.
- **Grid** sections per category, sticky headers, density classes, whole-card startup width.
- Single click opens the detail window (Ctrl/Shift select; drag to folder).
- Logos decoded at 256 px (memory ~290 MB, was ~770 MB).

Other features done: AAX/CLAP (Windows) and AU + full macOS scanning (untested on a Mac);
Google Drive + MEGA sync with conflict prompt (also when sync is switched on after local edits);
app self-update window with release notes, progress, Skip/Later; plugin update checks (startup
optional + every N hours; skips plugins already flagged outdated); thumbnail editor; image URL
field; new app icon (plug "V" on an orange gradient tile).

---

## 5. Important behaviours and gotchas

- **Version pins (`manual-metadata.json`) always win.** A pinned *latest* version hides all future
  updates; a pinned *current* version hides reinstalls. Fixed (2026-10-12): Save only pins fields
  the user actually edited (both apps), and context-menu **Re-detect Version** drops the current
  pin and re-reads the file. Refresh All Metadata still respects pins. 17 plugins still carry
  old current-version pins (Kontakt 8.13.1, Pigments, …) — a Re-detect on each clears it.
- **Update checks skip plugins already flagged outdated** (user's request) — a second newer
  release is not noticed until the plugin is updated.
- **KVR is the only source** for automatic latest versions; it can lag vendors by days/weeks.
- **Status line vs sidebar counts differ by design**: "Updates available" counts installed,
  non-hidden plugins (what the view shows); the status line's "N with updates" includes hidden
  and uninstalled.
- Avalonia pitfalls hit in this project: Fluent's own `accent` button class overrides
  backgrounds at template level (fixed in Controls.axaml); `RadioButton` + `Command` doesn't fire
  on programmatic select — bind `IsChecked` two-way to bool properties; OneWay `IsChecked` on a
  ToggleButton fights the click; XAML `Image` can't show `.ico` reliably — use PNG
  (`Assets/app-mark.png`); a `TrayIcon` must be registered with `TrayIcon.SetIcons`.
- Secrets: `MegaS4Config.cs` contains live S4 keys committed to the **public** repo (user chose to
  keep them; env vars `VSTMANAGER_S4_*` override). Google client secret is gitignored and loaded
  from env / `%APPDATA%\VstManager\google-client.json` / embedded copy.
- Settings live in the shared `library.json`; any new field must be additive and optional so the
  WPF app (and older builds) still open the file.

---

## 6. Assets and branding
- Icon source of truth: `build/make-icon.py` (Pillow, 4× supersampled) → `build/app-icon-master.png`
  (1024 px) + `src/VstManager.Ui/Assets/app-icon.ico` (16–256). `build/make-icns.py` → `Assets/app-icon.icns`.
  Colours: tile gradient `#FB923C → #F97316 → #C2410C`, plug body `#1B1206`, prongs `#FFF7ED`.
- `Assets/app-mark.png` (128 px) is the sidebar logo.
- The WPF app still uses its old icon (`VstManager.App/app-icon.ico`).
- Design canvases (private claude.ai artifacts): "Main Window Sidebar", "VST Manager Logo",
  "Update Available Window".

---

## 7. Verification approach used
No automated tests exist. Changes were verified by:
- building Windows Debug + `-r osx-arm64` + Release + WPF;
- **headless Avalonia renders** (a scratch console project referencing `VstManager.Ui` with
  `Avalonia.Headless` + `Avalonia.Skia`, `CaptureRenderedFrame()`) — used instead of screenshots;
- small Core probes against real files (scanner counts, version diffs, pin rule);
- backing up `%APPDATA%\VstManager\*.json` before anything that writes user data.

---

## 8. Repo state
All work up to this handoff is committed on branch `VstManager.Ui` (tracks `origin/avalonia`),
**not pushed**. The old `/build` ignore rule was removed, so the icon pipeline
(`build/make-icon.py`, `build/app-icon-master.png`) is now tracked. Publish output stays
ignored via `/artifacts`.

---

## 9. Next steps (suggested, in priority order)
1. **Push** `VstManager.Ui` → `origin/avalonia` when the user asks (committed locally only).
2. **Folder emoji** in sidebar rows (currently colour square only) — user's choice.
3. **Align counts** (sidebar "Updates available" vs status line) if the user wants one number.
4. **Clear remaining current-version pins** in bulk (offer a Settings → Data action), or leave
   to per-plugin Re-detect.
5. **WPF parity** decisions: new icon, sidebar, categories — or formally freeze WPF.
6. **macOS verification** when a Mac is available: scanning (`.component`/`.vst3`/`.vst`/`.clap`
   bundles), Info.plist versions, curl lookups, notifications, autostart, `.app` launch,
   code-signing (`codesign --deep -s -`, quarantine removal).
7. **Tests**: re-create `tests/VstManager.Core.Tests` (xUnit) for `PluginScanner`,
   `LibraryStore.MergeOnRescan`, `PluginVersionDetector`, `ManualMetadataOverrideService`,
   `CloudSyncService` decisions — these are where regressions have actually happened.
8. Optional: lazy-load logos only for visible cards; MediaFire provider (waiting on the user's
   Application ID + API key); icon set migration (Fluent UI System Icons) if desired.
