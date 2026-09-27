# Synapse Mouse Adjustments

A Windows desktop app (C# / WPF, .NET 10) that makes an ordinary office mouse as configurable as a gaming mouse, using legitimate Windows software-level customization. It has a MIDNIGHT (dark) interface, runs in the system tray, and saves every setting automatically.

> **Honest by design.** A generic mouse has fixed hardware: its sensor DPI, polling rate and switch debounce are set by its firmware, and Windows cannot change them. Synapse provides **software** equivalents (software DPI, software debounce, remapping, click timing), labels them as software, and never shows a hardware option it can't actually apply. Hardware-only options (such as the 125/250/500/1000 Hz polling rate) appear locked, with an explanation.

Not affiliated with Razer or any mouse manufacturer. There are no macros, recording, playback, auto-clicking, anti-cheat bypass, input hiding, code injection or game-memory access.

---

## Features

| Area | What it does | How it works |
|---|---|---|
| **MASTER ENABLE** | One switch that turns every modification on or off. When it's off, the mouse behaves exactly like normal Windows and the app keeps running in the background. | Off = input hooks removed, held outputs released, and every Windows setting Synapse changed is restored |
| **Button remapping** | Remap Left, Right, Middle, Side 4/5, Scroll up/down and Tilt left/right to another mouse button, a keyboard key (held while held), scrolling, DPI stage up/down/cycle, a sensitivity clutch, or next/previous config | Low-level mouse hook + `SendInput` |
| **Virtual buttons** | For mice without side buttons: a keyboard shortcut acts as an extra mouse button (Back, Forward, a key, DPI, …) | Low-level keyboard hook |
| **Debounce** | Software debounce from 1 to 100 ms, with two modes: *Instant* (no added latency; bounce is filtered) and *Confirmed release* (fixes drag drops). Can be applied per button | Edge filter that always reconciles the switch's final state, so real clicks are never lost |
| **Single Click → Double Click** | One physical click sends two normal clicks. You choose the buttons, the interval, the hold time and the timing (*Both on press* or *After release*) | Scheduled `SendInput` on a high-resolution timer |
| **Click response** | Press delay, release delay, minimum hold time, a duplicate-press filter, and the Windows double-click detection time | |
| **DPI & sensitivity** | Up to 5 software DPI stages, DPI hotkeys, separate X/Y sensitivity, a clutch DPI, Windows pointer speed, and pointer acceleration | Relative movement is scaled with sub-pixel carry. Windows settings are applied per session |
| **Polling rate** | A live measurement of your mouse's real report rate. The hardware rate options stay locked because they can't be changed | Counts Raw Input reports |
| **Scroll** | Scroll lines and characters, software scroll sensitivity (whole or fractional notches), reverse vertical/horizontal, modifier + wheel for horizontal scrolling, scroll acceleration, and wheel remapping | |
| **Configs** | Create, rename, duplicate, delete, reset to preset, set a default, reorder, and switch instantly. Per-config hotkeys (for example Ctrl + Alt + 2). Import and export as `.synapseconfig` | JSON, validated on import |
| **App-specific configs** | For example, `javaw.exe` with a window title containing "Minecraft" switches to *Minecraft PvP*. There's an Automatic Config Switching on/off toggle and a choice of what happens when you leave the app | Event-driven foreground tracking (no polling) |
| **Input tester** | Shows live button states, comparing what the mouse sent with what Windows received. Also shows click hold, interval and CPS, scroll counts, movement rate, Windows double-click detection, a 3-second timeline, an event log, and counts of bounces that were filtered | |
| **Background & tray** | Closing the window hides the app to the tray. The tray menu has Open, Master Enable, Current Config ▸, Settings and Exit | |
| **Persistence** | Everything auto-saves, including the Master Enable state, the current config and each config's settings. The next launch restores it exactly | Atomic write with a `.bak` backup and corruption recovery |
| **Windows startup** | *Start with Windows* launches minimized to the tray and restores the last config and Master Enable state | Per-user `Run` registry key (no admin) |
| **Safety** | Emergency shortcut **Ctrl + Alt + Shift + F12**. Changing the left button's mapping reverts after 15 s unless you confirm it. There's an optional crash watchdog, and replacement is paused while an administrator window is focused | See [Safety](#safety--reliability) |

Starter configs: **Default** (no changes), **Gaming**, **Minecraft PvP**, **Sword PvP**, **Mace PvP**, **Custom**. Default hotkeys are Ctrl + Alt + 1 for Default, Ctrl + Alt + 2 for Minecraft PvP and Ctrl + Alt + 3 for Custom.

---

## Download

Get the latest version from the [Releases page](../../releases/latest):

- **`…-Setup-x64.exe`**: per-user installer. Recommended.
- **`…-win-x64-portable.exe`** / **`…-win-arm64-portable.exe`**: a single portable file.

The files aren't code-signed, so SmartScreen may warn you the first time. Choose *More info → Run anyway*, or check the file against `SHA256SUMS.txt` first.

## Requirements

- Windows 10 (1803 or later) or Windows 11, x64 or ARM64.
- No administrator rights and no drivers.
- To **build** it you need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The published self-contained exe needs nothing else installed.

## Build

```powershell
git clone <this repository>
cd Synapse-Mouse-Adjustments
.\build.ps1
```

This runs the unit tests and then creates `publish\win-x64\SynapseMouseAdjustments.exe`, a single self-contained file of about 70 MB.

Other options:

```powershell
.\build.ps1 -Runtime win-arm64          # ARM64 build
.\build.ps1 -FrameworkDependent         # a small exe that needs the .NET 10 Desktop Runtime installed
dotnet build SynapseMouseAdjustments.sln # a plain debug build
dotnet test                              # unit tests (these run on any OS)
```

You can also open `SynapseMouseAdjustments.sln` in Visual Studio 2026 (or any IDE with .NET 10 support, such as JetBrains Rider) and run the `SynapseMouse.App` project.

Every push also builds on GitHub Actions (`.github/workflows/build.yml`). The workflow runs the unit tests, publishes the exe, runs a runtime smoke test (`--self-test`: the real app starts on the Windows runner, opens every page, switches configs, toggles Master Enable and checks that the input hook is installed and removed), and uploads the exe as a build artifact.

To publish a release, push a tag such as `v1.0.1`. `.github/workflows/release.yml` then builds the x64 and ARM64 exes and the installer, runs the tests and the runtime smoke test, and creates the GitHub Release with checksums and the notes from `.github/releases/<tag>.md`.

**Dependencies:** only the .NET 10 base libraries (WPF, plus WinForms for the tray icon). The test project uses xUnit. There are no third-party runtime packages.

## Install

- **From a release:** run the setup exe, or place the portable exe anywhere and run it.
- **Portable (simplest):** copy `SynapseMouseAdjustments.exe` anywhere, for example `%LOCALAPPDATA%\Programs\SynapseMouseAdjustments\`, and run it. If you add an empty `portable.txt` next to the exe, settings are stored next to it too.
- **Installer (optional):** after `.\build.ps1`, compile `installer\SynapseMouseAdjustments.iss` with [Inno Setup 6](https://jrsoftware.org/isinfo.php). This produces a per-user installer that needs no admin rights. The uninstaller restores your Windows mouse settings and removes the startup entry.

**Uninstalling manually:** Exit Synapse from the tray, run `SynapseMouseAdjustments.exe --uninstall` (this restores your Windows mouse settings and removes the startup entry), then delete the exe and `%APPDATA%\SynapseMouseAdjustments`.

## Using it

1. Open **Dashboard** and set **MASTER ENABLE** to **● ENABLED**.
2. Choose a config, such as *Minecraft PvP*. Anything you change on any page applies immediately and is saved automatically.
3. Close the window. Synapse keeps working from the tray, and **Exit** in the tray menu quits it completely.
4. Use **Input Tester** to check that what you set is really happening. The *Mouse* column shows what the hardware sent, and the *Windows* column shows what apps received.

### Minecraft PvP notes

Synapse is a general Windows mouse utility. It never touches Minecraft's files, memory or process.

- **Single → Double click** (Click Settings): each physical click becomes two ordinary clicks. The interval and hold time are adjustable, and the timing is precise to about 0.5 ms. Generated clicks are regular `SendInput` events, and Windows flags them as software-generated like any remapper's. Synapse does **not** hide them. Some servers don't allow double-click tools, so check the rules where you play.
- **Debounce** stops a worn switch from double-clicking by itself. A second physical click inside the debounce time is also filtered, so keep it low (2–10 ms).
- **Button remapping** can turn a mouse button into a Minecraft keybind. For example, Side 4 can become Left Shift to sneak. Minecraft reads physical key positions, so Synapse sends keys as scan codes.
- **App-specific configs**: *Configs → Add Minecraft (Java Edition)* links `javaw.exe` plus the window title "Minecraft" to *Minecraft PvP*.
- **Sensitivity**: software DPI and X/Y scaling reach Minecraft when "Raw Input" is on or off. For the most predictable result, keep in-game sensitivity fixed and switch Synapse DPI stages.

---

## Safety & reliability

- **Nothing permanent.** Pointer speed, acceleration, scroll amounts and double-click time are changed for the **current session only**. Nothing is written to your Windows profile, which keeps your real settings in the registry. *Restoring normal behavior* re-applies those registry values. Windows also reverts session values when you sign out.
- **MASTER ENABLE off / Exit:** anything Synapse is holding is released, the queued output is flushed, the hooks are removed and the Windows settings are restored.
- **Crash or force-kill:** Windows removes low-level hooks automatically when a process ends, so mouse input can't stay blocked. The optional **watchdog** is a tiny helper process that waits on the app. If the app ends without a clean exit, the watchdog restores your Windows mouse settings and releases any button left logically pressed. The next launch restores everything as well.
- **Fail-open hooks:** hook callbacks do minimal work and catch every exception, and on any error they pass the event through unchanged. A health check reinstalls the hook if Windows ever drops it.
- **Administrator windows:** Windows blocks injected input into elevated apps (UIPI). While one is focused, Synapse passes everything through, so you never lose clicks there.
- **Can't click?** Press **Ctrl + Alt + Shift + F12**. This emergency off can't be remapped or taken by a virtual button. Changing the left button's mapping also asks for confirmation and reverts after 15 seconds.
- **No admin, no drivers, no system files.** The only registry writes are the per-user `Run` value (Start with Windows) and, when you enable startup, clearing Task Manager's "disabled" flag for Synapse's own entry.

---

## Project structure

```
Synapse-Mouse-Adjustments/
├─ SynapseMouseAdjustments.sln
├─ build.ps1                         # test + publish script
├─ global.json / Directory.Build.props
├─ .github/workflows/build.yml       # Windows CI build, uploads the exe
├─ installer/SynapseMouseAdjustments.iss   # optional Inno Setup installer
├─ tools/generate_icons.py           # regenerates the .ico files (no dependencies)
├─ src/
│  ├─ SynapseMouse.Core/             # platform-independent logic (unit tested)
│  │  ├─ Models/                     # MouseConfig, SettingsDocument, KeyChord, enums
│  │  ├─ Input/                      # InputProcessor pipeline, ProcessorSettings, OutputEvent,
│  │  │                              # PointerMath, KeyNames, ActionCatalog, InputSafety
│  │  ├─ Config/                     # ConfigManager, ConfigPresets, ConfigSanitizer,
│  │  │                              # ConfigSerializer (.synapseconfig), FeatureSummary, AutoSwitchLogic
│  │  └─ Persistence/SettingsStore.cs  # atomic JSON save / backup recovery
│  └─ SynapseMouse.App/              # WPF application (Windows only)
│     ├─ Program.cs, App.xaml        # entry point, single instance, helper modes
│     ├─ Native/NativeMethods.cs     # Win32 declarations
│     ├─ Infrastructure/             # Log, StoragePaths, SingleInstance, Watchdog, MessageWindow, AppInfo
│     ├─ Services/                   # InputEngine, InputInjector, PreciseWaiter, WindowsMouseSettings,
│     │                              # DeviceService, RawInputMonitor, HotkeyService, StartupService,
│     │                              # ForegroundWatcher, TrayService, OsdService, AutoSaver,
│     │                              # DialogService, AppController
│     ├─ ViewModels/                 # one view model per page (MVVM, no external libraries)
│     ├─ Views/                      # MainWindow, one XAML view per page, dialogs
│     ├─ Controls/                   # NumericSlider, KeyCaptureBox, ButtonPicker, SettingItem, Icon, InputTimeline
│     ├─ Theme/                      # Colors.xaml (Midnight palette), Controls.xaml (styles), Templates.xaml
│     └─ Assets/                     # app and tray icons
└─ tests/SynapseMouse.Core.Tests/    # xUnit tests for the pipeline and config system
```

## How the major modules work

**`InputProcessor` (Core)** is the input pipeline, and it's pure logic with no Win32 calls, so it's unit tested. For each physical event it returns *Pass* (Windows gets the original event untouched, with zero latency) or *Block*, plus a list of replacement events with delivery times. The order for buttons is: raw edge → debounce → remap → click transform (press/release delay, minimum hold, single → double) → output channel. Output channels are reference counted, so two inputs mapped to the same button act like one held button. They're also time ordered, so a press and its release can never swap places. `ReleaseAll` guarantees that nothing stays pressed when Synapse is disabled.

**`InputEngine` (App)** owns two threads:
- The *hook thread* installs `WH_MOUSE_LL` / `WH_KEYBOARD_LL` only when the active config actually needs them. It runs the processor under a lock and returns within microseconds.
- The *output thread* is the only caller of `SendInput`, which preserves ordering. It sleeps on a high-resolution waitable timer until the next scheduled event and runs debounce reconciliation.

Synapse's own injected events carry a marker in `dwExtraInfo` so its hooks skip them and don't loop. This isn't hidden: Windows still flags them as injected.

**Software sensitivity.** The hook reports where Windows *would* move the cursor. The difference from the current cursor position, divided by Windows' pointer-speed gain, gives the movement in mouse counts. That is multiplied by stage DPI ÷ hardware DPI × axis sensitivity (with the fractional remainder carried over) and re-sent as relative movement. Pointer acceleration is kept off while scaling is active, because otherwise Windows would apply it twice. When every multiplier is 1.00×, movement isn't intercepted at all.

**`WindowsMouseSettings`** applies pointer speed, acceleration, scroll lines/characters and double-click time through `SystemParametersInfo`, for the session only. Changes run on a background thread. It restores from the user's registry values.

**`AppController`** is the coordinator. It loads settings, applies the active config (or the disabled state) to the engine and Windows whenever anything changes, and handles Master Enable, config switching, hotkeys, auto-switching, the tray, the OSD and auto-save.

**`DeviceService` / `RawInputMonitor`** enumerate mice through Raw Input and read names from the HID descriptor (`HidD_GetProductString`) and the device tree (`CM_Get_DevNode_Property`). They listen for plug and unplug events and measure the real report rate. Values that Windows or the device doesn't expose are shown as **Unavailable**.

**`ConfigManager` / `ConfigSerializer` / `ConfigSanitizer` / `SettingsStore`** handle config operations and JSON. Every file read from disk or imported goes through strict size, format and version checks, and every value is clamped to its valid range, so a malformed or malicious file can't crash the app or push bad values into the engine.

## Files

| File | Location |
|---|---|
| Settings (all configs, app settings, Master Enable, current config) | `%APPDATA%\SynapseMouseAdjustments\settings.json` (+ `.bak`), movable in Settings |
| Log | `%APPDATA%\SynapseMouseAdjustments\logs\synapse.log` |
| Exported config | `Name.synapseconfig` (JSON: `format`, `schemaVersion`, `config`) |
| Full backup | `*.synapsebackup` (the complete settings document) |

## Command-line options

| Option | Purpose |
|---|---|
| `--startup` | Used by *Start with Windows*: starts hidden in the tray |
| `--restore-windows-settings` | Re-applies your own Windows mouse settings, then exits |
| `--uninstall` | Restores settings and removes the startup entry, then exits |
| `--self-test <report.txt>` | Launches the full UI with temporary settings, visits every page and exercises the engine; exits 0 on success (used by CI) |

## Known limitations

- **Hardware DPI, polling rate and debounce** can't be changed on generic mice. Synapse measures them, provides software equivalents and says so.
- **Software sensitivity in games:** it works in Windows and in games that re-center the cursor or read Raw Input (including Minecraft). A game that locks the cursor to a screen edge without re-centering may lose movement at that edge. In that case, use the game's own sensitivity.
- **Administrator apps:** while an elevated window is focused, Synapse passes input through unchanged, because Windows blocks injected input into it. You can run Synapse as administrator if you need its settings there.
- **Anti-cheat:** some games or servers don't allow input-modifying software. Synapse makes no attempt to hide itself.
