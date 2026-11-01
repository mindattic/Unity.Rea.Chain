# Unity.Rea.Chain

A mouse-steered angel swings a spiked mace on a spring chain, knocking down, grabbing and throwing a flock of
enemies. A Unity 6 rebuild of the 2005 VB.NET / DirectX 9 *Chain Game*, which was itself built on Philip Winston's
2000 *Elastic Trail* script, a mouse-follow chain of spring-linked dots. The original project folder's own
`Backup`, `Backup1`, `Backup2` and `.vs` folders are all last modified 1/10/2024, the most recent build on file.

[![Unity](https://img.shields.io/badge/Unity-6000.4.3f1-222222)](ProjectSettings/ProjectVersion.txt)
[![C#](https://img.shields.io/badge/language-C%23-512BD4)](Assets/Unity.Rea.Chain)
[![Status](https://img.shields.io/badge/status-feature%20complete-green)](docs/BIBLE.md)

There is no public build. Build it from source with the steps below; `docs/demo` holds screenshots and a clip of a scripted run of the built player.

## The game

Move the mouse and the angel flies; behind it hangs a chain ending in a mace, simulated link by link as springs
with drag and gravity, so it lags, swings and whips. Circle the mouse and the mace winds up like a flail. A hit
knocks an enemy back and scores; hold the left button and the mace grabs instead, and letting go throws the enemy
into the others. Fourteen enemies home in on you, each touch costs one of your 30 HP, a heart drifts down for +10,
and at zero the light fades and the round starts again by itself.

## Status

Every behaviour of the 2005 game is ported, fixed or recorded as a deliberate deviation or dead code; the last two open ones, wall bounce and the window frame, are settled to the original's behaviour (U.CHN-T76, U.CHN-T63, [docs/RECONSTRUCTION.md §8](docs/RECONSTRUCTION.md)). On main and tested:

- the spring chain (`ChainSimulation`, `ChainController`), the mace with `[` / `]` chain length and the overhead
  boost (`MaceController`)
- the angel's flight: mouse steering, hover bob, facing, flap frames, bounds (`AngelFlight`, `AngelController`)
- one `GameWorld` in Core that only `GameSession` steps: players send commands carrying their player id, and the
  enemies, audio and HUD play the events it produces (`Core/World/`, docs/BIBLE.md §5.3)
- grapple and throw (`Core/Grapple/`), enemies with seek, knockback, grab, throw and flocking, hit by a swept circle so
  fast swings never tunnel (`Enemies/`), the enemy spawner and the heart pickup (`Items/`)
- HP, score, pause, and the original's game-over fade and automatic reset (`GameState`, `GameSession`), with the
  UI Toolkit HUD of Gui.vb and renderDisplay (HP, score, clock, readouts, FPS, status box, pause banner, Tab menu,
  F12 settings screen) laid out from `hud.json` (`GameHud`)
- the original's lighting: the ambient fade times the r/g/b tint the Day, Dusk, Night, Morning cycle walks (now on a
  24-minute day; the original's never moved), on a URP 2D global light, L toggling it (`LightingState`, `SceneLight`)
  under the URP 2D pipeline (`Rendering/ChainURP`, `Editor/RenderPipelineBuilder`)
- level bounds and a camera that fits or follows (`Level/`), sound and music players with the original's own clips (`Audio/`)
- every input from one Input System action asset built in code, with keyboard, mouse and gamepad bindings; a
  gamepad's left stick steers like the mouse and J switches it off and on (`Input/ChainControls`, `Input/SeatInput`,
  `Core/Input/SteerInput`, `input.json`)
- Background.vb's backdrop and Cube.vb's brick room behind it: the beach from the start, B and N showing the beach
  and utopia, every gfx/area picture listed by name in `area.json` (`Level/AreaBackdrop`, `Core/Level/Backdrop`)
- the scenes and the Windows player generated in code (`Editor/SceneBuilder`, `Editor/PlayerBuilder`)
- online play on the shared Unity.Multiplayer kit, beside local play: O opens the kit's menu (Host, Join by IP, Join
  by code with a lobby, Settings, Play alone); every connection is a `ChainPlayer` carrying its seat, a client's
  commands go to the host's `GameSession` instead of its own world, and the host sends its world back as snapshots
  (`Online/`, `Core/Net/`, docs/BIBLE.md §5.4). A client draws the host's whole scene from those snapshots (U.CHN-T73)
- Form1.vb Startup's loading picture: the player opens on the Splash scene, one of `load640_1..12` chosen at
  random over the whole window until Main has loaded (`UI/SplashScreen`, `Core/Hud/LoadingScreen`, `hud.json` `loading`)

Every tuning figure is data: the original's own figures in `Assets/StreamingAssets/Settings/*.json`, validated
on load and mapped to world units in one place (`Core/Settings`, see [BIBLE §6](docs/BIBLE.md)).

Also on main: the original's art and sound, the Unity.Shared profile and settings, the window and icon, the live
chain tuning, game speed, round clock, debug camera, hit boxes and render toggles, the scripter console, the
Zone/Menu/Tab panels, and a scripted run of the built player that is asserted in PlayMode and captured under
`docs/demo`. Nothing from the original is left unported. The full list, one ticket per step, is
[docs/RECONSTRUCTION.md §6](docs/RECONSTRUCTION.md); the tickets are in the MindAttic ticket database
(`tickets ticket list --repo Unity.Rea.Chain`).

## Build and test

Prerequisites: Unity 6000.4.3f1. Close the Editor before any batchmode run.

In the Editor, open the project and use the **MindAttic > Chain** menu:

- **Build Main Scene** regenerates `Assets/Scenes/Main.unity`, `Assets/Scenes/Splash.unity` and the build settings
  (Splash first, so a build opens on it, then Main) from code; **Build Splash Scene** regenerates the splash alone.
- **Apply Project Settings** sets the window (640x480, windowed, resizable), company/product name,
  `applicationIdentifier` and the Standalone icon (`battle.ico`, UNI-T38) from code.
- **Build Windows Player** writes `Builds/Windows/Unity.Rea.Chain.exe`.

Play `Assets/Scenes/Splash.unity` (the loading picture, then Main) or `Assets/Scenes/Main.unity`, or run the built player. Run the tests from **Window > General > Test Runner**
(EditMode and PlayMode).

From the command line (PowerShell, from the repo root):

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\6000.4.3f1\Editor\Unity.exe"

# regenerate the scene, apply project settings, then build the Windows player
& $unity -batchmode -quit -projectPath . -executeMethod MindAttic.Chain.EditorTools.SceneBuilder.BuildMainScene -logFile -
& $unity -batchmode -quit -projectPath . -executeMethod MindAttic.Chain.EditorTools.ProjectSettingsBuilder.Apply -logFile -
& $unity -batchmode -quit -projectPath . -executeMethod MindAttic.Chain.EditorTools.PlayerBuilder.BuildWindows -logFile -

# edit-mode and play-mode tests
& $unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults TestResults-EditMode.xml -logFile -
& $unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults TestResults-PlayMode.xml -logFile -
```

Or, from the repo root (each wraps a `tools/*.ps1` script, same shape as Unity.SpaceGod's and
Unity.GridGame's):

```
build.bat     # tools/build.ps1: the three -executeMethod calls above, logged to Logs/build-*.log
test.bat      # tools/test.ps1: Core.Tests, then EditMode and PlayMode (includes MainSceneSmokeTests)
run.bat       # asks 1) Deploy & Run: tools/run.ps1 -Force -Launch (pull, build.bat, publish to
              # C:\Apps\Unity.Rea.Chain, refresh the Desktop shortcut, launch) or 2) Run: the installed copy
play.bat      # tools/play.ps1: launch Builds/Windows/Unity.Rea.Chain.exe for manual/dev testing,
              # muted by default (building it first if missing)
```

The engine-free rules are tested by `Tests/Core.Tests` (`dotnet test Tests/Core.Tests`, or
`./run-core-tests.ps1`).

### Muted launches

`play.bat` (`tools/play.ps1`) launches `Builds/Windows/Unity.Rea.Chain.exe` muted: it sets
`MINDATTIC_CLI_MUTED=1` and passes `-muted`, so a CLI or agent starting the game for manual or dev
testing never plays audio. It builds the player first (`tools/build.ps1`) if it is missing; pass
`-Build` to rebuild, `-Sound` to launch with audio, or `-Wait` to wait for it to exit. The player's
log is `Logs/play.log`. Same shape as Unity.QuickDuel's `play.ps1`.

```powershell
play.bat                      # muted
powershell -File tools/play.ps1 -Sound   # with audio
powershell -File tools/play.ps1 -Capture "screen 1000"   # U.CHN-T62 scripted -demo run of the capture build (Builds/Capture), muted, exits non-zero on a failed shot
```

Either switch also works on its own, against any Unity.Rea.Chain.exe:

```powershell
$env:MINDATTIC_CLI_MUTED = '1'; .\Builds\Windows\Unity.Rea.Chain.exe
.\Builds\Windows\Unity.Rea.Chain.exe -muted
```

The mute is the shared `com.mindattic.unity.shared` session mute (Unity.Shared's `ProfilesBootstrap`,
read before the first scene loads): it makes the current profile's common settings
(`Profiles.Current.Settings.Common`) report music and sound-effect volume 0 and both muted, for that
run only. `MusicPlayer` and `ChainAudio` (U.CHN-T36) already play at those volumes -- `MusicPlayer`
zeroes and mutes its `AudioSource`, and `ChainAudio` never calls `PlayOneShot` once the computed
volume is 0 -- so the mute is audible-off end to end without a separate `AudioListener` override.
The player's saved `settings.json` is never written, so the next normal launch has sound. A profile
whose own settings mute music or sound effects is honoured the same way. See also
[Unity.Shared's README](https://github.com/mindattic/Unity.Shared#readme) ("Root override for tests
and automation").

## Controls

| Keyboard and mouse | Gamepad | Effect |
|---|---|---|
| Mouse | left stick | fly |
| Left button (hold, release) | right trigger or A (hold, release) | arm the grapple, throw what it holds |
| `[` / `]` | left / right shoulder | shorten / lengthen the chain |
| F | Y | chain follows the hand / hangs slack |
| U | | gem links: links 1-5 become aqua, emerald, teardrop, tablet, pearl (no key takes them off) |
| M | | debug: the mace's skin, spike ball / magnet (the original declared the magnet but never reached it; `chain.json` `maceSkins.mode` picks the starting one) |
| Pause or P | Start | pause / resume |
| Escape | | quit |
| R | Back (Select) | start over |
| J | | gamepad stick steering on / off |
| L | | lighting on / off |
| 0 | | screen shake: the view jumps, the next press puts it back |
| Tab | | the original's menu panel on / off (lists F1-F4, F12 and Escape with their current state) |
| F1 / F2 / F3 | | wireframe / untextured / back-face culling, drawn by a URP renderer feature |
| F4 | | frame rate on / off |
| F12 | | the settings screen (frmConfig: windowed / fullscreen, resolution, music / sfx / ambient volume; Submit saves to the profile), pausing while open |
| E | | hit boxes (Zone.vb's overlay) on / off; on from the start |
| B / N | | beach / utopia background |
| Insert / Delete | | chain resistance up / down |
| Home / End | | chain gravity down / up |
| Page Up / Page Down | | chain spring up / down |
| Minus / Equals | | game speed down / up (held) |
| Numpad + Enter, 4 6, 8 2, X, Z | | debug camera: forward / back, left / right, up / down, isometric / not |
| ` (grave) | | the scripter console: type a script, Enter runs it, Escape closes it |

The bindings live in one `InputActionAsset` built in code (`Runtime/Input/ChainControls`); the stick's speed and
dead zone are in `input.json`.

Every key of the original is listed with its ticket in
[docs/RECONSTRUCTION.md §3.5](docs/RECONSTRUCTION.md); the ones with no effect in the original (the arrow keys,
the right and wheel buttons) are recorded there as dead.

## Project layout

```
Assets/
  Scenes/Splash.unity              the loading picture a build opens on, then Main; generated by SceneBuilder
  Scenes/Main.unity                generated by SceneBuilder; do not edit by hand
  StreamingAssets/Settings/*.json  every tuning figure, the original's own (BIBLE §6)
  Unity.Rea.Chain/
    Core/                          Unity.Rea.Chain.Core asmdef (no engine): the GameWorld and every rule
      World/  Chain/  Player/  Enemies/  Grapple/  Game/  Level/  Audio/  Maths/  Settings/
    Runtime/                       Unity.Rea.Chain.Runtime asmdef: the authority, input and presentation
      Chain/  Player/  Enemies/  Grapple/  Items/  Level/  UI/  Audio/  Input/  Lighting/  Rendering/  Settings/  Demo/
    Editor/                        Unity.Rea.Chain.Editor asmdef: SceneBuilder, PlayerBuilder
    Tests/EditMode/                NUnit edit-mode tests (Unity Test Framework)
    Tests/Online/                  the online play tests
    Tests/PlayMode/                the main-scene smoke test, the splash test and the scripted playthrough
    Materials/
docs/
  BIBLE.md                         design rules, architecture, data, fixed defects and deviations
  RECONSTRUCTION.md                the original reverse-engineered in full, parity audit, build order
  chain-physics.md                 the spring algorithm and its tuning
  USER_STORIES.md                  generated from the ticket database
  demo/                            screenshots and a clip of the scripted run (RECONSTRUCTION §7)
```

The target architecture (an engine-free Core assembly under Runtime, with its own `dotnet test` project) is in
[docs/BIBLE.md §5](docs/BIBLE.md).

## Documentation

- [docs/BIBLE.md](docs/BIBLE.md): what the game is, the design rules, the architecture, the data files, every
  defect of the original that the rebuild fixes and every deliberate deviation, with the reason.
- [docs/RECONSTRUCTION.md](docs/RECONSTRUCTION.md): the reverse engineering of the original, module by module,
  and the build order to parity.
- [docs/chain-physics.md](docs/chain-physics.md): the chain algorithm from *Elastic Trail* to Unity.
- [AGENTS.md](AGENTS.md): the rebuild standard and where the original source lives.
