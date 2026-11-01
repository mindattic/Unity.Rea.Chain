---
codex: 1
project: Unity.Rea.Chain
code: UNI
layer: reconstruction
status: living
updated: 2026-10-25
---

# Unity.Rea.Chain: reconstruction of the original (REA) and parity audit

Written for UNI-T25 (owner 2026-10-23). It records the reverse engineering of the original prototype
in full, compares it with the rebuild as it stands on main, audits the project against Unity
best practice and the shared MindAttic standards, and lists in build order every story and ticket
that still separates the rebuild from a complete proof of concept.

The earlier, narrower notes stay in [chain-physics.md](chain-physics.md) (the spring algorithm and its
tuning table). Where the two disagree, this file is the newer reading.

## 1. What the original is

Two source trees are the original (both read directly as source; there was no binary to decompile, though
`bin\Chain.exe` was checked for the shipped asset set):

| Path | What it is |
|---|---|
| `R:\Projects\_PRE_2022\_PRE_2021\_PRE_2020\_PRE_2017\Chain Game\` | The VB.NET / DirectX 9 game (2005). The reference for the rebuild. Modules: `Root`, `Engine`, `Chain`, `Angel`, `Enemy`, `Item`, `Input`, `Sound`, `Camera`, `Background`, `Cube`, `Lights`, `Gui`, `Text`, `Zone`, `Terrain`, `Textures`, `Script`, `Character` (empty), `Ref\Time`; forms `Form1`, `frmMain` (an older `Form1`), `frmConfig`. `Backup`, `Backup1` and `Chain Game - Copy\Backup*` are earlier snapshots. |
| `R:\Projects\_PRE_2022\_PRE_2021\_PRE_2020\_PRE_2017\chain\default.htm` | Philip Winston's *Elastic Trail* (2000), the chain algorithm the game ported. |
| `R:\Projects\_PRE_2022\_PRE_2021\_PRE_2020\GameMaker\gmlchain\ChainGame.gmx` | A GameMaker stub: one room, one object heading for the mouse. Nothing to port (see chain-physics.md, UNI-T12). |
| `...\Chain Game - Copy\sfx\` and `bin\Chain\sfx\` | The audio: 17 WAVs and `music\music.wma`. The working copy `Chain Game\` itself has no `sfx` folder. |

Inventory of the shipped game: 27 source modules/forms, 70 image files under `gfx\` (angel 10, area 10,
brick 1, chain 9, cursor 9, enemy 8, gui 20 incl. 12 loading pictures, item 1, `angel_dev`, `enemy`,
`terrain`), 17 WAVs, one WMA, one icon (`battle.ico`). No data files: the Jet `.mdb` databases are commented
out and `loadTerrain` reads an unset `DataRow`. The only persisted data is the registry key
`HKCU\Software\MindAttic\Engine` (read) and `...\War` (written) holding Width, Height, ColorDepth and Fullscreen.

## 2. How the original runs

`Form1.Startup` shows a random `gfx\gui\load\load640_<1..12>.jpg` while it builds the `Engine` (ported as the
Splash scene, UNI-T58, BIBLE V23), hides the
Windows cursor, takes the mouse exclusively through DirectInput, then loops
`getInput()` / `oneFrameRender()` / `Application.DoEvents()` with no frame cap and **no time step**: every
physics number is "per frame". The window is a 640x480 borderless black form titled `*** UNDER DEVELOPMENT ***`.

`Engine.initDevice` creates the D3D device (16-bit depth buffer, alpha blending on, linear filters, lighting
on, ambient 64/64/64), then initialises in this order: camera, input, sound, fonts, textures, game clock,
cube, background, enemies, angel, item, text, zone, menu, status, chain, `rndMusicPos`.

Frame order in `oneFrameRender` (paused frames only draw `- PAUSE -`):

1. clear white, `renderCube`, `renderBackground`
2. `playMusic`, `renderFPS`, `renderMenu`, `renderStatus`, `renderLight`
3. `checkFlocking`, `checkMaceImpact`, `checkThrowImpact`, `checkAngelHit`, `checkItemCollect`
4. `renderAngel` (steer + hover + frame), `renderChain` (anchor, simulate, draw), `renderEnemy`, `renderItem`
5. `showScripter`, `updateClock`, `renderDisplay` (HP/score/readouts), `levelText`, `GameOver`, `initCamera`, `renderFPS`

World units: the camera is a 45 degree perspective at z = -1 looking at the z = 0 plane with a 4:3
aspect, so the visible half-height is tan(22.5) = 0.414 and half-width 0.552. All sizes below are in those units.

## 3. Module-by-module reverse engineering and rebuild status

Status key: **ported** (in main, tested), **partial**, **missing** (ticket filed), **dead** (in the source
but unreachable or does nothing; not ported, recorded here), **n/a** (DirectX plumbing with no Unity meaning).

### 3.1 Chain (`Chain.vb`)

| Behaviour | Original | Rebuild | Status / ticket |
|---|---|---|---|
| Links | 26 (`maxLinks` 25, index 0..25), each with X, Y, dX, dY, texture, mass 1; link 0 is the anchor | `ChainSimulation`, 26 links; `SimulateAllLinks` (chain.json's `port.simulateAllLinks`) true by default so every link simulates whether or not it is drawn, matching `animateChain`; false parks the inactive tail on the mace instead (kept only where asked for explicitly) | settled, default matches (U.CHN-T45, BIBLE V3); drawn: every link 0..Mace-1 is link.png on its own linkSize quad (`LinkSkinView`, U.CHN-T94; it had been a tiled line, now chain.json's `port.chainLine` option) |
| Spring | Hooke past `segLength` 0.002, `springK` 0.1, from `link(l-1)` **only** (the `If l > 0 / ElseIf` always takes the first branch); JS original pulled from both | `PullBothNeighbors` (chain.json's `port.pullBothNeighbors`) false by default, pulling from the previous link only, matching Chain.vb's own `If/ElseIf`; true pulls from both neighbours as the JS original did, kept only where asked for explicitly | settled, default matches (U.CHN-T45, BIBLE D7) |
| Integration | semi-implicit Euler per frame, drag `resistance` 0.25, gravity (0, -0.0025), jitter stop 1e-7 | per fixed step, `PhysicsStepRate` (60 by default, space.json's `framesPerSecond`) cutting real time into sub-steps close to the original's own frame length, so the discrete integration keeps its feel at any host frame rate | settled (U.CHN-T45, BIBLE D9) |
| Mace | link index `Mace` (6; keys `[` / `]` 1..12), drawn at 3.5x scale, hit rectangle `rectMace`; `texMace`, `texMaceGrapple` while the button is held | `MaceController`, ball visual (art UNI-T19); grapple swap to `maceSkins.files.grapple` (mace2.png) while `Grapple.IsArmed`, reverting to the current mode's skin on release | ported, grapple swap fixed (U.CHN-T72; claimed but unported since UNI-T19, found by UNI-T43) |
| Overhead boost | spring.Y doubled when pulling up and above `link(0).Y - linkSize` | `ChainController` 2x boost | ported |
| Drift hacks | `link(Mace).X/Y += maceOffset` every frame (cumulative); `link(1).Y -= 0.0004` per frame | `MaceOffsetPerSecond` and `Link1DropPerSecond` (chain.json's `maceOffset` and `link1DropPerFrame`, mapped to world units a second): zero in the bare `Settings.Default` (render-only, no fudge term), but chain.json maps the original's own figures onto them so the shipped game carries both drifts | settled, shipped default matches (U.CHN-T45, BIBLE D8) |
| Wall bounce | commented out | `LevelBounds` bounce 0.5 | settled (BIBLE V4, U.CHN-T76): no bounce by default, as the original's is commented out; `keepInArena: true` in chain.json brings back the `LevelBounds` bounce |
| `followMouse` (F key) | starts `True`, never reset; when false `animateChain` leaves every spring force zero (gravity and drag still act, so the chain falls away from the hand) and `renderChain` skips the mace's 3.5x scale, offset and `rectMace`, which is left where it last was | `ChainSimulation.IsFollowing`, `ChainFollow` (Core: `ToggleFollowCommand` with a player id -> `FollowChangedEvent`, the 3.5x and 1x mace scales), F action on `ChainController`; off, the mace draws at link size and neither hits nor catches (BIBLE D17) | ported, UNI-T47 |
| Live tuning | Ins/Del resistance +/-0.001, Home/End gravity -/+0.00001, PgUp/PgDn spring +/-0.001 (`changeResistance`, `changeGravity` on `gravity.Y`, `changeSpring` just add, no limits; works paused), `changeBounce` unused | Core `TuneChain` command (player id, `ChainKnob`, steps) -> `ChainTuned` event, `ChainTuning` (chain.json's `tuning`: the steps and the port's limits, BIBLE V19), the Player map's six actions through `SeatInput` and `ChainController.Tune`; the HUD's Res/Grav/Spring readout follows; `GameWorld.Reset` restores the data files' figures | ported, UNI-T51 |
| Link skins | `loadChain` gives every link `texChain` (link.png); U key sets `link(1)`..`link(5).texture` to aqua, emerald, teardrop, tablet, pearl (16x16, magenta-keyed); nothing sets them back | Core `SkinLinks` command (player id) -> `LinksSkinned`, `PlayerEntity.HasGemSkins` / `LinkSkin` from chain.json's `skins` (`LinkSkins`, `LinkSkinData`), drawn by `LinkSkinView` over the line one link wide; the mace link stays the mace (BIBLE V25); works paused, no reset touches it | ported, UNI-T41 |
| `theMaceMode.Magnet` + `magnet.PNG` | `theMaceMode` (SpikeBall, Magnet) declared and `maceMode = SpikeBall`, but nothing reads or sets it and no key reaches it; `magnet.PNG` ships (`Chain.vbproj`) but Textures.vb never loads it: **the original had no way to show the magnet** | Core `MaceMode` / `MaceModes` on `PlayerEntity.MaceMode`, starting in chain.json's `maceSkins.mode`; the Player map's ToggleMaceMode (M, the rebuild's debug key) -> `ToggleMaceMode` -> `MaceModeChanged`; `MaceController` swaps the visual to `maceSkins.files`' picture; hit and grapple rules untouched (BIBLE D4) | fixed, UNI-T43 |
| `bRicochet`, `grappleMax` | `bRicochet` unused; `grappleMax` 1 | `grappleMax` honoured | ported |

### 3.2 Angel (`Angel.vb`)

| Behaviour | Original | Rebuild | Status |
|---|---|---|---|
| Steering | `X += mouseX * 0.00171`, `Y -= mouseY * 0.00171` from the exclusive mouse delta | `AngelFlight` | ported |
| Facing | right when delta.x > 0, left when < 0 (kept otherwise); hand offset 0 right / 0.033 left | `AngelController`; the hand (link 0) hangs at the flight position plus the hover, plus angel.json's `linkOffsetX` (0 facing right, 0.033 facing left), as `renderChain` puts `link(0)` (`AngelFlight.HandOffset`/`HandOffsetLeft`; it had been a mirrored +/-0.3 world units) | ported (U.CHN-T93) |
| Hover bob | triangle wave +/-0.01 in 0.001 steps per frame, applied to the draw offset | `AngelFlight.Triangle` (it had been a sine until U.CHN-T85) | ported (U.CHN-T85) |
| Sprite quads | `setupVertex(v, size, size / 2)` draws every sprite on a size x size quad from its X, centred on its Y: the angel's `angelSize` 0.3 at `angelOffset` (-0.122, 0.1) plus the hover, each enemy's `enemySize` 0.3, each link's `linkSize` 0.025, the mace's at 3.5x, the heart's `itemSize` 0.05 | `AngelController.SpriteOffset`/`SpriteWidth`, `Enemy.SpriteWidth`, `MaceController.LinkWidth`, `LinkSkinView.LinkWidth`, `HeartPickup` (left-edge pivot): each whole picture drawn on its quad, in place; they had been shrunk to the port's contact circles and line width, a third of the size or less | ported (U.CHN-T93) |
| Flap | 4 frames per facing, advance every 0.1 s, loops | frame logic in place, `angel_left1-4`/`angel_right1-4` wired | ported (UNI-T19) |
| Bounds | X -0.6..0.5, Y -0.6..0.4 (`checkBounds`) | `AngelFlight`, clamped to angel.json's `boundsX`/`boundsY` (x 9: X -5.4..4.5, Y -5.4..3.6); it had been the 16 x 9 arena inset by an invented margin, now `port.arenaBounds`, an option only | ported (U.CHN-T86) |
| HP / hurt | 30 HP, 1 per enemy contact at most every 0.25 s, game over below 0; `checkAngelHit` tests every enemy, a held one included (and a dead one's stale rectangle, BIBLE D34) | `GameState`; `HpOvershoot` 1 by default (the HUD shows `Hp + 1` bars, game over once `Hp < 1 - HpOvershoot`, matching the original's own HP+1 quirk exactly); 0 is exactly `Hp` bars and game over at 0, kept only where asked for explicitly | settled, default matches (U.CHN-T45, BIBLE D10) |
| Contact rect | X..X+0.06 by Y+0.03..Y+0.15 | physics circle | deliberate deviation |
| `angelAI.Hurt` | sinks 0.01/frame; nothing ever sets it | none | dead |

### 3.3 Enemies (`Enemy.vb`, `Engine.checkFlocking/checkMaceImpact/checkThrowImpact`)

| Behaviour | Original | Rebuild | Status / ticket |
|---|---|---|---|
| Count and spawn | 14 (`maxEnemy` 13). `getRndNum(-1, 1)` returns the integers -1, 0 or 1 and 0 is rejected, so every enemy spawns at one of the four corners (+/-1, +/-1) | `GameWorld.SpawnWave` and `Reset` spawn 14 enemies (`enemies.json`'s count); the four-corner spawn is the fixed defect D1, real random points instead | ported (UNI-T21, D1 via UNI-T48; Core.Tests `GameWorldTests.SpawnWave_PutsTheConfiguredCountInTheArena_WithMassInRange`, EditMode `EnemySpawnerTests.Reset_SpawnsFourteen_AndAnotherResetReplacesThem`) |
| Start values | `loadEnemy`: HP 0.2, mass 1 + rand(0..1), random frame 0..6. `Reset`: HP 1.0, mass unchanged | `GameWorld.SpawnWave`'s very first wave spawns at `enemies.json`'s `loadHP` (0.2, `EnemySetup.LoadHp`), every wave after it at `resetHP` (1.0, `EnemySetup.Hp`); mass and frame as `GameWorld.SpawnEnemy` already drew them (UNI-T21, initial `EnemyBrain.Frame` UNI-T19) | ported (UNI-T70) |
| Seek | `d = (angel - offset - pos) * 0.006` (proportional, so it eases in) | `EnemyBrain` | ported |
| Hurt | decays velocity by `resistSPD` 0.001 per axis, back to Seek when both are near zero | `EnemyBrain` | ported |
| Grabbed | follows the mace link (+0.03, small random jitter), spins | `Grapple` in `GameWorld` | ported |
| Thrown | Input.vb's release sets velocity = mace velocity and `Thrown`, and `thrownNum` stays set until the next press. As written the `Thrown` state never lasts a frame: `checkThrowImpact` loops over every enemy, **the thrown one included**, and `rectIntersect` of a rectangle with itself is true, so in the frame it is released it divides its own velocity by its mass and turns `Hurt`, and does so again every frame until the next press. As run, a thrown enemy flies a short way (its speed divided by its mass each frame, then `resistSPD` drag), then stays `Hurt` (immune to the mace, never seeking) until the button is pressed again, and stuns any enemy that touches it meanwhile. With the self-hit removed, `Thrown` has no drag and no end state, so it would leave the arena for good | the self-hit is the defect fixed (BIBLE D5): the thrown enemy goes `Hurt` and decelerates like a knocked one, knocks the enemies it touches while it flies, then seeks again (`EnemyBrain.Throw`); the rate is `enemies.json`'s own `resistSPD` | fixed, rate confirmed (U.CHN-T45); as-run reading corrected (U.CHN-T89) |
| Dead | HP <= 0: skipped entirely (vanishes) | dead state | ported |
| Mace hit | alive, not Hurt, not Grabbed: velocity = mace velocity / mass, damage = abs(dX + dY/2), score += damage * 1000; a held enemy takes the same | `GameWorld` mace step (swept circle); `MaceDamageFormula.Original` by default, the original's own abs(dX + dY/2) of the mass-divided velocity (`VelocityToOriginalScale` converts back to the original's per-frame units); `SpeedTimesFactor` (mace speed x `damagePerSpeed`) kept only where asked for explicitly | settled, default matches (U.CHN-T45, BIBLE D11) |
| Throw impact | thrown enemy knocks any overlapping enemy: v = thrown v / mass, Hurt | `EnemyBrain` | ported |
| Flocking | overlapping live, not-grabbed pairs pushed apart by 0.0005 per axis, each pair visited twice a frame (so 0.001); range test commented out, `flockRange` unused | `EnemyBrain.FlockPush`, 0.06 u/s | ported, UNI-T23, UNI-T35 |
| Animation | 7 frames (`Shadow\enemy1..7`), 0.1 s per frame, starts at a random frame (`loadEnemy`); `renderEnemy` animates every live enemy, a Grabbed one too | `EnemyBrain.Frame`, advanced in `Step` and, for a held enemy the world does not step, `Animate`; read by `Enemy`'s view | ported (UNI-T19); a held enemy keeps animating as in the original (U.CHN-T84) |
| AI states `Cautious`, `Align` | coded but never selected. `Cautious`: `d = 6 * seekSpd * -getSlope(angel, pos) / mass`, and `getSlope` returns pos - angel, so as written it charges; `Align`: `+/-seekSpd` per axis toward the angel (y with an `angelSize / 3` offset) | `EnemyBrain` Cautious/Align, reached only when `enemies.json`'s `seekCap` is below the enemy count; shipped at 14, so every enemy seeks as in the original | settled to the original (U.CHN-T81, BIBLE D18); the hang-back (UNI-T65) is a data option, spike U.CHN-T90 |
| AI states `Retreat`, `Wander`, `Limbo`, `Death` | empty or just sink | none | dead |

### 3.4 Item (`Item.vb`)

One heart (`maxItem` 0): Health10, starts at x 0, y 0.4 (sprite `gfx\item\heart.png`, 16 x 16, magenta colour key, drawn
0.05 wide with its left edge on X and centred on Y). Each frame: falls `fallRate` 0.001 (not `floatSPD`), wraps to 0.5 below
-0.5, and sways by six sequential `If` blocks on `floatSPD` (0.002 from `loadItem`, 0.001 from `Reset`), `floatAcc` 0.00001
and `floatMaxSPD` 0.001, each block seeing what the ones above changed (so with the 0.002 start the first ~100 frames flip
direction every frame and net about -0.00001 a frame). Its rectangle is X..X+0.045 by Y-0.02..Y+0.03; the angel's is
X..X+0.06 by Y+0.03..Y+0.15; `rectIntersect` tests only the angel's edges against the heart's, so an angel box straddling
the heart does not touch it. Touch gives +10 HP capped at 30 and the heart is consumed: it neither moves nor draws until
`Reset`. At the fixed 60 fps step (D9) the fall is 0.06 u/s. `ExtraLife`, `Health5`, `Health20`, `Money` and the `Center`
and `Wander` motions are declared and empty. **Ported**, UNI-T22 then UNI-T44 (`HeartFloat`, `HeartPickup`), figures through the one space mapping (length scale 9, BIBLE V7).

### 3.5 Input (`Input.vb`, `Form1.OnKeyDown`)

Every binding of the original, with its state:

| Input | Effect | Status / ticket |
|---|---|---|
| Mouse delta | steer | ported (Steer action, UNI-T34) |
| Left button | arm grapple while held; release throws | ported (Grapple action; gamepad right trigger or A) |
| Right, wheel button | right: nothing; wheel: set an unused blank cursor | dead |
| `[` `]` | mace index -1/+1 (1..12) | ported (ChainShorter / ChainLonger actions; gamepad shoulders) |
| Minus / Equals | `gameSpeed` -/+0.01 per frame held (min 0, polled even paused); only displayed, never applied | ported (UNI-T52; GameSpeedDown/GameSpeedUp actions, read while held, -> `ChangeGameSpeed` -> `GameSpeedChanged`; game.json's `gameSpeedStep`; applied as the world's time scale, `TimeScale`, BIBLE D13, V20) |
| Ins/Del, Home/End, PgUp/PgDn | tune resistance, gravity, spring | ported (UNI-T51; ResistanceUp/Down, GravityDown/Up, SpringUp/Down actions -> `TuneChain`, held to chain.json's limits, BIBLE V19) |
| F | `followMouse` toggle | ported (UNI-T47; ToggleFollow action, gamepad Y, UNI-T34) |
| U | gem link skins | ported (UNI-T41; the Player map's SkinLinks -> `SkinLinks` -> `LinksSkinned`, BIBLE V25) |
| (none) | `maceMode` (SpikeBall / Magnet): no key, menu or line ever selects it | added (UNI-T43; M, the Player map's ToggleMaceMode debug key -> `ToggleMaceMode` -> `MaceModeChanged`, BIBLE D4) |
| R | `Reset` | ported (Reset action sends `ResetGame`, gamepad Back, UNI-T34), restoring everything `Reset` does through the one `GameWorld.Reset` (UNI-T48) |
| 0 | `shakeScreen(0.02)` | ported (UNI-T49; the Debug map's ShakeScreen, `CameraRig.ShakeScreen`); the draws are really random (BIBLE D19) |
| L | lighting toggle | ported (`SceneLight`, the Debug map's ToggleLighting); off is unlit rather than frozen (BIBLE D16) |
| B / N | beach / utopia background (a second `Case Keys.B` for the background toggle is unreachable) | ported (UNI-T40; the Debug map's AreaB and AreaN, read paused too, show `area.json`'s `keyB` and `keyN` through `AreaSelection`; no reset touches it); the unreachable toggle flips a flag nothing reads and is not ported (BIBLE D15) |
| E | zone (hit box) overlay, default on | ported (UNI-T56; the Debug map's ToggleZones flips `ViewOptions.Zones`, drawn by `HitZoneOverlay`; no reset touches it) |
| F1, F2, F3 | wireframe, textures, culling (D3D render states) | ported (UNI-T55 the keys and flags: the Debug map's ToggleWireframe, ToggleTextures, ToggleCulling flip `GameHud.Options`, a Core `ViewOptions`, shown in the menu, put back by every reset; UNI-T56 the drawing: `ViewRenderFeature` on the 2D renderer redraws the scene wireframe, untextured or culling back faces, BIBLE V14, D24, D25) |
| F4 | FPS counter (`bDrawFPS`, on at the start and in every `Reset`) | ported (UNI-T50; the Debug map's ToggleFps, `GameHud.ToggleFps`); off hides it and a frame counts once (BIBLE D20, D21) |
| Tab | menu panel (`bMenu`, drawn only while not paused; `Reset` closes it) | ported (UNI-T55; the Debug map's ToggleMenu, `GameHud.ToggleMenu`; see 3.6) |
| Pause | pause | ported (Pause action: Pause, P as an extra, gamepad Start -> `TogglePause` -> `PauseChanged`; BIBLE V8, UNI-T54) |
| Esc | quit (`bRunning = False`, `Finalize`, the form closes; works paused) | ported (Quit action: Escape -> Core `QuitGame` -> `QuitRequested`; `GameSession` ends the application, or stops play mode in the Editor; UNI-T54). Escape closes the scripter while it is open instead (U.CHN-T61) |
| F12 | toggle fullscreen and restart the engine (config) | ported (UNI-T55, UNI-T59): the Debug map's ConfigureVideo opens frmConfig's settings screen, whose Submit applies the window mode in place, no restart (BIBLE V21, D27) |
| J | joystick toggle; the joystick code reads axes and buttons and binds nothing | fixed: the gamepad drives the actions, its stick steers like the mouse, J switches the stick (BIBLE D14, UNI-T34) |
| Numpad + Enter / 4 6 / 8 2, X, Z | debug camera | ported, UNI-T57: the Debug map's CameraForward/Backward/Left/Right/Up/Down, Isometric, Deisometric, read held by `CameraRig` into a Core `DebugCamera` (BIBLE V1, D26; the anchor move is dead, V22) |
| ` (grave) | scripter console (`bScripter`): typed letters echo, C C / C H / C P / C S / C T shortcuts, Back, Delete, Insert recalls `lastScript`, Enter runs and remembers, Escape/F11/grave close; `getInput` skipped while open or `bScriptRunning` | ported (U.CHN-T61): the Debug map's ToggleScripter opens a Core `Scripter`, which `ScripterConsole` types into from the keyboard while open and draws on the HUD (hud.json `scripter`) in the script colour; Enter runs the line through `ScriptRunner` and `ScriptEffects` puts it into the `CameraRig`'s `DebugCamera` and the `SceneLight`'s `LightingState` (ambient colour included); the session's `InputGate` holds OnKeyDown's keys back while open and getInput's while open or a script runs. Stray keys no longer type their names and the line is drawn (BIBLE D33) |
| Arrow keys, `btnAttack`..`btnDashRight` | declared; no effect | dead |

### 3.6 Game rules, HUD and clock (`Engine.vb`, `Gui.vb`, `Root.vb`)

| Behaviour | Original | Status / ticket |
|---|---|---|
| HUD text | `Write(text, font, x, y, alpha, red, green, blue)` in a 512x448 window: `HP : ` as `|` per point at (20, 10) and `Score : ` at (10, 30), red; `Res/Grav/Spring` at (50, 50), blue; `Grappled: <hp>` at (20, 120), yellow; `FormatNumber(gameSpeed, 2)` at (Width - 50, 20) in `colorReticle` (red); `<n> fps` at (Width - 50, 4) in 255, 0, 25; all `fontMenu` (Arial 10 bold). **In the final source `Root.Write` has its `DrawText` commented out, so no text is drawn at all**; the earlier backups drew it. The rebuild follows the intent (BIBLE D3) | ported (UNI-T50): `GameHud`, a UI Toolkit panel laid out from `hud.json` in the original window's pixels, scaled to the screen's height; `TopRight` text keeps its 50 px from the right edge on any aspect |
| Clock | `updateClock`: once `Time.Clock` has moved a second past `lastClockCheck`, `timerSec` counts up and rolls into `timerMin` at 60; written `timerMin & ":0" & timerSec` below 10 s, else `timerMin & ":" & timerSec`, at (300, 5) in yellow (255, 255, 255, 0), `fontClock` (Arial Narrow 16); called only while not paused; `Reset` sets both to 0 | ported: counted and reset (`GameWorld.RoundTime`, UNI-T48); the readout (UNI-T53) is `GameHud`'s `hud-clock` from `hud.json`'s `clock`, `RoundClock.Format` of the whole seconds (`m:ss`, minutes unpadded and uncapped), hidden with the status layer while paused. Like the rest of the round it runs on game time, so it reads real seconds at game speed 1.0 (V20) |
| Status box | `status_box.png` (99x46, magenta keyed) loaded at 96x46 and drawn on a 96x48 quad at (`statOffsetX`, `statOffsetY`) = (0, 0), its v from 2/48 (the top two rows, all keyed, never show) | ported (UNI-T50): `Resources/UI/status_box.png`, keyed to alpha, top two rows cropped, point-filtered, drawn 96x48 under the HUD text |
| Menu | `renderMenu`, while `bMenu` (Tab) and not paused: `menu.jpg` (256x256, a dark blue panel) on a 255x255 quad at (10, 10), its v from 2/255 (`loadMenu`), then `Write` in `fontMenu` with 255, 255, 255, 64 at (20, 20) `IIf(bRenderWireframe, "F1 - Turn to Solid", "F1 - Turn to Wireframe")`, (20, 40) `IIf(bRenderTextures, "F2 - Use Shaded", "F2 - Use Textured")`, (20, 60) `IIf(bBackFaceCulling, "F3 - Enable Culling", "F3 - Disable Culling")` (the wrong way round, BIBLE D22), (20, 80) `IIf(bDrawFPS, "F4 - Hide FPS", "F4 - Show FPS")`, (20, 100) `"F12 - Configure Video "`, (20, 240) `"ESC - Quit "`; the lines are never drawn in the final source (D3). The keys are Form1.vb's own (the menu only lists them); `Reset` sets `bMenu = False` | ported (UNI-T55): `GameHud`'s `hud-menu` panel over the HUD layers from `hud.json`'s `menu`, `Resources/UI/menu.png` (menu.jpg with its top two rows cropped), each line `GameMenu.Line` of the screen's `ViewOptions`; Tab (`ToggleMenu`), F1-F4 flip the flags, F12 flips the window mode (V21), ESC is the seat's Quit (`QuitGame`, UNI-T54); every reset closes it and puts the flags back |
| Pause | stops everything; draws only the background and `- PAUSE -` (`fontPause`, Arial Narrow 24, at 512 / 2, 448 / 2, half see-through `colorReticle` red), none of the HUD | ported (Pause key or P, UNI-T54); the world stops (`Time.timeScale` 0) and, as in the original, the music plays on: `playMusic` is only skipped, and DirectShow's `Audio` keeps playing what it started; the HUD hides its status layer and FPS while paused and shows the red banner centred (UNI-T50); the camera shows only the backdrop's layer (the area and the brick room) while paused, so the angel, chain, mace, enemies, heart and zones are not drawn, as `oneFrameRender` skips their render calls (`CameraRig.ApplyPauseView`, U.CHN-T78) |
| Game over | HP < 0: fade the ambient light 5 per frame, then `Reset` with no click | ported (UNI-T28): `GameState`'s fade of 5 a frame, then the one `GameWorld.Reset`, no click; EditMode `LevelAndHudTests.GameOver_FadesFiveAFrame_ThenResetsItself`, `Session_GameOver_RestartsWithoutAClick` |
| `Reset` | score, timer, fade, ambient, speed, flags, camera, spring/bounce/resistance/gravity, angel HP and position, all enemies (HP 1.0, Seek, corners), the heart, music start | ported, UNI-T48: one `GameWorld.Reset` for R and game over restores score, `RoundTime`, the fade, game speed (`game.json`), pause, the chain's spring/resistance/gravity, angel HP 30 and home, a fresh wave (HP 1.0, Seek, real random points, BIBLE D1) and the world's hearts (y 0.4, sway left, `floatSPD` 0.001; `turnAround` kept); its `WorldReset` recentres the camera, jumps the music (`rndMusicPos`) and turns lighting on (the time of day runs on, the stray `b = True` is not ported, BIBLE D16). The render flags wait on their own tickets; `Bounce` is only read by commented-out code (BIBLE V15) |
| `renderTime`, `renderDate`, `levelText` | never called, or only move a matrix (no text drawn) | dead |
| `Score` | += damage * 1000 | ported |
| `SetupLights` | never called | dead |
| Window and options | 640x480 borderless form; registry options; default 540x448/512x448, 16-bit | window shape ported (UNI-T38: 640x480, 4:3, snapped back once a border drag settles, not mid-drag; a maximized window is left alone); options screen ported (UNI-T59: frmConfig's Display Mode, Resolution, Default, Submit and the Sound sliders, in the profile; BIBLE V21, D27, D28) |

### 3.7 Lighting, camera, background (`Lights.vb`, `Camera.vb`, `Background.vb`, `Cube.vb`, `Text.vb`)

| Behaviour | Original | Status / ticket |
|---|---|---|
| Ambient + material tint | `AmbientColor` (white) and an r/g/b material colour set each frame | ported: `LightingState` tint times `GameState.Ambient`, on a URP 2D global light (`SceneLight`); a script's `CHANGE LIGHT AMBIENT` colour (`LightingState.Ambient`, white again on every reset as Root.vb's Reset sets it) multiplies in (U.CHN-T61) |
| Day cycle | Day, Dusk, Night, Morning with `dayLength` 1e10 s, so it never advances in play | ported (`LightingState`) at the original's 1e10 s day, so it stays Day unless a script moves it (U.CHN-T82, BIBLE D6); UNI-T46's 1440 s day is a data option, spike U.CHN-T91 |
| Camera | perspective 45 degrees, z -1, 4:3, rebuilt each unpaused frame from `cam` and `target` | orthographic, showing exactly the perspective's view of the play plane (+/-0.552 x +/-0.414, 4:3, bars on other screen shapes; `CameraFramingMode.Original`, U.CHN-T86), where it had fitted the 16 x 9 arena (now an option); the perspective kept as data (`camera.json`, `CameraRig.Perspective`), `cam`/`target` as `DebugCamera` on top of the fit | deliberate deviation (BIBLE V1); debug camera ported, UNI-T57 |
| Background | `setupVertex` quad 1.2 (`bgWidth`) across from its left edge by 0.83 (`bgHeight` 0.415 either side), translated to x -0.56 (so x -0.56..0.64), `texBackground` stretched over it (every area loaded at 512 x 512), `beach.png` from the start; drawn paused too | ported (UNI-T40): `AreaBackdrop` stretches the current area over `area.json`'s quad (`Backdrop.Quad`, mapped by `space.json`), whatever the picture's size |
| Brick room | `Cube.vb` cube 67x55x38 around the camera (eye z -1), textured `brick.jpg`, drawn before the background, so its far wall shows wherever the background does not reach | ported (UNI-T40): its far wall as the eye sees it, projected on the play plane (`Backdrop.FarWall`, BIBLE V24), behind the backdrop; the camera clears white as `oneFrameRender` does |
| Screen shake | alternates a random offset and a restore | ported (UNI-T49): `ScreenShake` offsets the rig's framing right and down by two draws of 0..0.02 screen each, the next press restores; the original's draws were always 0 (BIBLE D19) |
| Terrain lines (`Terrains`) | loader dereferences an unset `DataRow`; never called, and `bTerrain` starts False with no key | dead code, not ported (UNI-T56, BIBLE §7) |
| Zones (`Zone.vb`) | `loadZone`: `maxZone` 5, six line-strip boxes Left 0, Right 0.05, Top 0, Bottom -0.05, drawn with `texZone` (`gfx/gui/event.png`, 2x2 of 0, 128, 255); `renderZone`: while `bZone` (default True, E) moves box n to `Enemy(n).X + enemySize / 2, Enemy(n).Y` and box 0 to `rectMace.Left, rectMace.Top`; never called by `oneFrameRender` | ported (UNI-T56): `HitZones` (Core) boxes every enemy and each following mace, `HitZoneOverlay` draws them as `LineRenderer` strips from `hud.json`'s `zone`; the uncalled overlay, the six-box limit and the lost enemy 0 are fixed (BIBLE D23) |

### 3.8 Sound (`Sound.vb`)

DirectSound `SecondaryBuffer`s are loaded for `sword_swing_1..7`, `sword_hit_1..3`, `defend`, `footstep_1..2`,
`death_1..4` (11 copies each) and `music\music.wma` plays through `AudioVideoPlayback` with a random start
(`rndMusicPos`, at init and on `Reset`). `playSFX` is **never called**, and `bSound` and `bMusic` both default to
`False`, so the shipped game is silent. Volumes: music 0, sfx -1000, ambient -1500 (unused). The rebuild
plays swing, hit, death and catch sounds and loops music (more than the original) from the original's own clips
(15 WAVs plus `music.ogg`, converted from the WMA; `throw.wav` is the one new clip, U.CHN-T71, and plays only when `audio.json`'s `throwSound` is on, off by default as the original has no throw sound, U.CHN-T80): UNI-T20. `footstep` is unused in both; `defend` is unused in the original and is the rebuild's catch sound. Volumes and mutes are the shared profile's since UNI-T36, with these as the stock ones; frmConfig's options screen (UNI-T59) sets them, and the ambient volume in Chain's own `game.ambientVolume`.

### 3.9 Script language (`Script.vb`)

`Command Type Item Parameter` split on spaces. Commands: `CHANGE` (`CAMERA|CAM` X/Y with `+n`/`-n` arithmetic,
`LIGHT` AMBIENT, RED/R, GREEN/G, BLUE/B, `TIME` DAYPERIOD, `SCRIPT` COLOR and RUNNING, `HERO`/`ENEMY` stubs), `MAKE`
and `MOVE` (accepted, do nothing), `RUN` (`NIGHT`, `HEAVEN`). There is a generic `Make` that grows parallel arrays;
no caller. Ported as Core (UNI-T60): `ScriptLine` splits and checks a line as `divideScript` and `isScriptOkay` do,
`ScriptRunner` runs it against a `ScriptView` (the ambient and tint it reads) and reports `ScriptEffect`s
(`CameraMoved`/`CameraPlaced` in the original's screen units, `AmbientChanged`, `TintChanged`, `DayPeriodChanged`,
`ScriptColourChanged`, `ScriptRunningChanged`) for presentation to apply; it holds `scriptColor` (0, 0, 255) and
`bScriptRunning`, throttles each script by its increment, and ignores invalid lines. The HERO/ENEMY handlers are the
original's empty stubs, except `HERO AI STOP` (clears `bScriptRunning`); `Make` is not ported (nothing calls it).
Fixed: LIGHT was never accepted (BIBLE D29), the arithmetic (D30), short lines and non-numbers (D31), RUN HEAVEN's
second line (D32). The console that types and runs them is `Scripter` and `ScripterConsole` (U.CHN-T61, section 3.5's grave key).

### 3.10 Assets

| Group | Files | Used by the original | Status |
|---|---|---|---|
| `gfx\angel` | `angel_left1-4`, `angel_right1-4` (+ `angel_l`, `angel_r`, `angel_dev` unused) | flap frames | UNI-T19 |
| `gfx\chain` | `link`, `aqua`, `emerald`, `teardrop`, `tablet`, `pearl`; `mace\mace`, `mace2`, `magnet` | link, U skins, mace/grapple; magnet shipped but never loaded or used | UNI-T19, UNI-T41 (the five gems imported under `Art/Chain`, magenta baked to alpha as UNI-T19's link.png, listed by name in chain.json's `skins.files`), UNI-T43 (mace.png and magnet.png wired as the mace skins by chain.json's `maceSkins.files`) |
| `gfx\enemy` | `Shadow\enemy1-7` (`enemy.PNG` duplicates 1) | 7-frame loop | UNI-T19 |
| `gfx\item` | `heart` | pickup | UNI-T19, UNI-T22 |
| `gfx\area` | `beach`, `utopia` used; `01`, `02`, `512`, `landscape`, `landscape2` unused (the `Copy of landscape.jpg` files are duplicates); `brick.jpg` | background, brick room | ported (UNI-T19 imported the area art, UNI-T40 `brick.jpg` under `Art/Room`); every picture listed by name in `area.json`'s `areas`, only beach and utopia shown |
| `gfx\gui` | `status_box`, `menu.jpg` used; `load640_1-12` (loading); `ap_bar`, `event` (its 0, 128, 255 is the zones' colour, `hud.json` `zone`), `terrain`, `talkbox`, `load.PNG`, `menu.PNG` unused | HUD, menu, loading | UNI-T50, UNI-T55, UNI-T56, UNI-T58 |
| `gfx\cursor` | 9 cursors loaded, none drawn | unused | dead |
| `sfx` | 17 WAV, `music.wma` | see 3.8 | UNI-T20 |
| `battle.ico` | window icon | ported as-is, UNI-T38 |

## 4. Observed quirks worth keeping in mind

- Every enemy spawns on top of the others at a corner; flocking is what spreads them (UNI-T21 before UNI-T23).
- `checkThrowImpact` hits the thrown enemy with itself, so the `Thrown` state never survives its first frame: as run, a
  throw is a short toss that leaves the enemy stunned (`Hurt`) until the next press. Without that self-hit, a thrown
  enemy would never slow and could only end by leaving the play space or dying (U.CHN-T89, BIBLE D5).
- `getRndNum` is integer valued, so "random" positions are only -1, 0, 1.
- `battle.ico` (every copy checked: `Chain Game`, `Chain Game - Copy`, `Battle 2`, both `Backup` folders) is a single
  32x32 frame that is fully transparent -- `(0,0,0,0)` on all 1024 pixels, never drawn. It was never finished, like
  the window's `*** UNDER DEVELOPMENT ***` title. UNI-T38 still imports and assigns it (`Assets/Unity.Rea.Chain/Art/Icon/battle.png`,
  a point-filtered, alpha-preserving 256x256 conversion) rather than inventing new art: the data governs, even
  where the data is blank.
- Damage uses `abs(dX + dY/2)` of the *enemy's new* velocity (mace velocity / mass), so heavier enemies take less.
  This is now the shipped default (`MaceDamageFormula.Original`, U.CHN-T45, BIBLE D11).
- No step is time based: the original runs faster on a faster machine. The rebuild fixes the step at a frame rate
  close to the original's own (`PhysicsStepRate`, U.CHN-T45, BIBLE D9), matching the felt speed at any host frame
  rate, not only exactly 60 fps.
- The HUD text, clock, date and level text are all drawn through a `Write` that does nothing in the final source.

## 5. Standards audit (Unity best practice and the shared standards)

Checked against `AGENTS.md` (the owner's rebuild standard) and the reference repos Unity.SpaceGod and Unity.GridGame.

| Area | Finding | Ticket |
|---|---|---|
| Assembly definitions | Done: Core (`noEngineReferences`), Runtime, Editor, EditMode, PlayMode and the online tests are separate asmdefs with one-way references (BIBLE §5) | UNI-T30, done |
| Engine-free Core | Done: `ChainSimulation`, `AngelFlight`, `EnemyBrain`, `GameState` and every rule live in `Unity.Rea.Chain.Core` | UNI-T30, done |
| Tests outside Unity | Done: `Tests/Core.Tests`, run by `run-core-tests.ps1` | UNI-T31 |
| Commands and events | Done: one `GameWorld` in Core, stepped only by `GameSession`; players send `GameCommand`s with a `PlayerId`, presentation plays `GameEvent`s (BIBLE §5.3) | UNI-T32 |
| Data-driven content | Done: JSON data files in `StreamingAssets/Settings` holding the original's figures, validated on load and read by Core's `GameSettings` through `ISettingsSource`, one mapping (`space.json`) to the port's units (BIBLE §6) | UNI-T33 |
| Input System | Done: one `InputActionAsset` built in code (`ChainControls`), keyboard, mouse and gamepad bindings, read per seat by `SeatInput` into that seat's commands; nothing polls `Keyboard.current` or `Mouse.current` | UNI-T34 |
| URP | Done: a URP pipeline with the 2D renderer (`Assets/Unity.Rea.Chain/Rendering`, built by `Editor/RenderPipelineBuilder`) is assigned in GraphicsSettings and every quality level beside the committed global settings and volume profile; a headless import leaves them unchanged | UNI-T18 |
| Unity.Shared profiles and settings | Done: `com.mindattic.unity.shared` referenced; volumes, mutes and reduce motion in the profile's common block, resolution, window mode and tuning overrides in its game block, through Core's `IPreferencesStore`; music and SFX play at the profile's volumes (BIBLE §6) | UNI-T36 |
| UI | Done: `GameHud` is a UI Toolkit tree laid out from `hud.json` (UNI-T50 onward); no IMGUI remains | UNI-T37, done |
| Project settings | Done: 640x480 windowed default, borderless by default as Form1.vb's form (`WindowFrameBehaviour`, game.json `windowFrame`; BIBLE V12, U.CHN-T76), titled `Unity.Rea.Chain` (`productName`, replacing the original's unfinished `*** UNDER DEVELOPMENT ***`), snapped back to 4:3 once a border drag settles and left alone while maximized (`WindowAspectLock`/`WindowAspectLockBehaviour`; BIBLE V12), the Standalone icon from `battle.ico`, a global cursor policy (`CursorPolicy`: hidden and confined from the first frame; BIBLE V18), `applicationIdentifier` (`com.mindattic.unityreachain`), company/product name and version set, all applied in code (`Editor/ProjectSettingsBuilder.cs`) | UNI-T38 |
| Build, test and deploy scripts | Done: `build.bat`, `test.bat`, `run.bat`, `play.bat` over `tools/*.ps1`, plus `run-core-tests.ps1` | UNI-T39, done |
| README | Rewritten: status, layout, build and test (UNI-T29) | done |
| BIBLE | `docs/BIBLE.md` written: rules, architecture, data, fixed defects, deviations (UNI-T29) | done |
| Built in code | `SceneBuilder`/`PlayerBuilder` generate the scene (good); keep it that way | ok |
| Multiplayer seam | Done: the Unity.Multiplayer kit, `ChainPlayer`, `Core/Net` snapshots, a client drawing the authority's world (BIBLE §5.4, V26) | UNI-T26, UNI-T73, done |
| Asset and meta hygiene | Done: `.meta` files committed (UNI-T17); art imported and wired (UNI-T19); audio imported (UNI-T20) | done |

## 6. Build order

Each ticket requires the one above it (ticket links), and the queue priority descends the same way, so
building them top to bottom reaches full parity. Existing open tickets are placed where they belong rather
than refiled. Every row has its own story.

| # | Ticket | Story | What it builds |
|---|---|---|---|
| 1 | UNI-T29 | UNI-US-F1 | docs/BIBLE.md and a current README |
| 2 | UNI-T30 | UNI-US-F2 | engine-free `Unity.Rea.Chain.Core` assembly, Runtime reduced to adapters |
| 3 | UNI-T31 | UNI-US-F3 | `dotnet test` project for Core, pure tests moved out of EditMode |
| 4 | UNI-T32 | UNI-US-F4 | commands with a player id producing events; presentation only reads |
| 5 | UNI-T33 | UNI-US-F5 | tuning and content as validated data files and a settings registry |
| 6 | UNI-T34 | UNI-US-F6 | Input System action asset, gamepad, J toggle |
| 7 | UNI-T18 | UNI-US-F7 | URP 2D pipeline, renderer and global settings committed |
| 8 | UNI-T36 | UNI-US-F8 | Unity.Shared profiles and settings |
| 9 | UNI-T37 | UNI-US-F9 | UI Toolkit HUD root replacing IMGUI |
| 10 | UNI-T38 | UNI-US-F10 | window, icon, cursor and project metadata |
| 11 | UNI-T39 | UNI-US-F11 | build, test and deploy scripts |
| 12 | UNI-T19 | UNI-US-A1 | the original's sprites imported and wired |
| 13 | UNI-T20 | UNI-US-A2 | the original's sound and music imported (WMA converted) |
| 14 | UNI-T40 | UNI-US-A3 | beach and utopia backgrounds, B/N keys, brick room |
| 15 | UNI-T41 | UNI-US-A4 | U-key gem link skins |
| 16 | UNI-T43 | UNI-US-A5 | magnet mace skin |
| 17 | UNI-T45 | UNI-US-G1 | settle every physics and combat deviation to the original's behaviour |
| 18 | UNI-T21 | UNI-US-G2 | 14 enemies spawned at the corners, respawned on reset |
| 19 | UNI-T23 | UNI-US-G3 | enemy flocking separation |
| 20 | UNI-T35 | (fix of UNI-T23) | flocking uses the original's value |
| 21 | UNI-T22 | UNI-US-G4 | the heart pickup |
| 22 | UNI-T44 | (fix of UNI-T22) | heart matches Item.vb exactly |
| 23 | UNI-T28 | UNI-US-G5 | game over fade and automatic reset |
| 24 | UNI-T46 | UNI-US-G6 | ambient light, material tint, day cycle, L toggle |
| 25 | UNI-T47 | UNI-US-G7 | F key follow-the-mouse toggle |
| 26 | UNI-T48 | UNI-US-G8 | R key and the full Reset, random music start |
| 27 | UNI-T49 | UNI-US-G9 | 0 key screen shake |
| 28 | UNI-T50 | UNI-US-D1 | HUD layout, status box, readouts, FPS counter |
| 29 | UNI-T51 | UNI-US-D2 | live resistance, gravity and spring keys |
| 30 | UNI-T52 | UNI-US-D3 | game speed keys and readout |
| 31 | UNI-T53 | UNI-US-D4 | m:ss round clock |
| 32 | UNI-T54 | UNI-US-D5 | Pause key and Escape to quit |
| 33 | UNI-T55 | UNI-US-D6 | Tab menu panel |
| 34 | UNI-T56 | UNI-US-D7 | hit box overlay and F1-F3 render toggles |
| 35 | UNI-T57 | UNI-US-D8 | numpad and X/Z debug camera, perspective data |
| 36 | UNI-T58 | UNI-US-D9 | random loading picture on start |
| 37 | UNI-T59 | UNI-US-D10 | resolution, window mode and volume settings screen |
| 38 | UNI-T60 | UNI-US-C1 | script language in Core |
| 39 | UNI-T61 | UNI-US-C2 | scripter console and its key handling |
| 40 | UNI-T26 | UNI-US-V1 | Unity.Multiplayer kit scaffolding |
| 41 | UNI-T62 | UNI-US-V2 | built player played by a scripted run, captured |
| 42 | U.CHN-T63 | U.CHN-US-V3 | final parity pass; every story done; repo finished (section 8) |

Rows 20 and 22 were filed by the owner's standard sweep (UNI-T35, UNI-T44) after the first flocking and
heart versions guessed values; they are slotted straight after the tickets they correct. UNI-T24's note in
chain-physics.md (game over freezes until a click) is superseded by UNI-T28.

## 7. Scripted capture (U.CHN-T62)

Row 41's "done, verified by play" is a capture-enabled Windows player (`MindAttic ▸ Capture ▸ Build Windows
Player (Capture Enabled)`, `Builds/Capture/Unity.Rea.Chain.exe`) run with `-demo -capture video` (`tools/play.ps1 -Capture video`, muted). `-demo`
(`DemoBootstrap`) adds a `DemoSplashShot` to the Splash scene and a `ScriptedPlaythrough` to Main once it
loads; both call the same public API real input and the EditMode tests already use
(`GameSession.Submit`, `GameWorld.SpawnEnemy`, `AngelFlight.Teleport`, `GrappleController.SetButtonHeld`),
so every beat below happens for real, not by chance. `PlaythroughActions` is the shared, lower-level form of
the same moves, driven directly by `ScriptedPlaythroughTests` (Assets/Unity.Rea.Chain/Tests/PlayMode) so the
path is asserted on every test run, not just captured once by eye.

Screenshots and one short highlight clip from the run are committed under `docs/demo/` and cross-referenced
onto the closed ticket that built each beat (`tickets ticket link U.CHN-T62 related <ticket> --note
"docs/demo/<file>"`), rather than piling them all onto U.CHN-T62 itself:

| Stop | Beat | Screenshot | Built by |
|---|---|---|---|
| 01 | Launch and the loading picture | `docs/demo/01-loading-picture.png` | U.CHN-T58 |
| 02 | Main loaded | `docs/demo/02-main-scene.png` | U.CHN-T16 |
| 03 | Fly and swing | `docs/demo/03-fly-and-swing.png` | U.CHN-T4, U.CHN-T5 |
| 04 | Grapple catch | `docs/demo/04-grapple-catch.png` | U.CHN-T7 |
| 05 | Grapple throw | `docs/demo/05-grapple-throw.png` | U.CHN-T7 |
| 06 | A kill | `docs/demo/06-kill.png` | U.CHN-T8 |
| 07 | Taking hits | `docs/demo/07-take-hits.png` | U.CHN-T32 |
| 08 | Heart pickup | `docs/demo/08-heart-pickup.png` | U.CHN-T22, U.CHN-T44 |
| 09 | Game over fade | `docs/demo/09-game-over-fade.png` | U.CHN-T28 |
| 10 | Automatic reset | `docs/demo/10-auto-reset.png` | U.CHN-T28 |
| 11 | Pause | `docs/demo/11-pause.png` | U.CHN-T54 |
| 12 | Tab menu | `docs/demo/12-menu.png` | U.CHN-T55 |
| 13 | Scripter command | `docs/demo/13-scripter-command.png` | U.CHN-T61 |
| 14 | F12 settings screen | `docs/demo/14-settings-screen.png` | U.CHN-T59 |
| - | Kill-and-hit highlight clip | `docs/demo/highlight.mp4` | U.CHN-T8, U.CHN-T32 |

Refreshed by U.CHN-T75 (2026-10-26): all fourteen screenshots and the clip were re-captured, muted, from a fresh
capture build that includes U.CHN-T62's review fixes, so they show the corrected run. Stop 01 no longer races the
splash: `-demo` holds the Splash scene (`SplashScreen.Hold`) until the shot of the picture is taken, then releases
it; a normal launch never sets it. The media now go through Git LFS (`.gitattributes` covers `docs/demo/*.png` and
`*.mp4`); only the refreshed files are LFS objects, history was not rewritten. The first attempt aborted because
MindAttic.Capture's free-RAM valve closed (other agents' Unity jobs) and was rerun with the host above its floor.

## 8. Final parity pass (U.CHN-T63)

Every row of sections 3.1 to 3.10 was read against the code on main and the tests that cover it, and each ends in
one of four states. Nothing is **missing** and nothing is **partial**.

| State | Rows | Where the evidence is |
|---|---|---|
| Ported | every Chain, Angel, Enemy, Item, Input, Game rules, Lighting and Script row, including the two that were stale (Count and spawn, Game over) | the ticket in each row's last column, whose Ref names its test or commit; the whole path is also asserted end to end by `ScriptedPlaythroughTests` (U.CHN-T62) |
| Fixed defect | the rows the original's source breaks (magnet unreachable, no HUD text, silent game, never-applied game speed, joystick, the shake key, FPS, zones, F2/F3, frmConfig, script LIGHT and arithmetic, and the rest) | BIBLE §7, D1 to D33, each `fixed` or `settled` |
| Deliberate deviation | contact rectangles as physics circles, the orthographic camera fit, the window title (V12), music as OGG, online play, the added catch sound | BIBLE §8, V1 to V26, each `kept`, `settled` or `matches` (V4 and V12 settled by U.CHN-T76) |
| Dead | `angelAI.Hurt`; the `Retreat`, `Wander`, `Limbo`, `Death` AI states; right and wheel mouse buttons; the arrow keys and `btnAttack`..`btnDashRight`; `ExtraLife`, `Health5`, `Health20`, `Money`, the `Center` and `Wander` motions; `renderTime`, `renderDate`, `levelText`; `SetupLights`; `Terrains`; the nine cursors; `bRicochet`; `Make`; `Character` | recorded in BIBLE §7's "Not restored, on purpose": each has no body, no caller or no key, and restoring it would invent behaviour |

Stale rows corrected by this pass: 3.3 Count and spawn (was `missing, UNI-T21`), 3.6 Game over (was `missing,
UNI-T28`), 3.8 Sound (said no clips shipped) and six rows of section 5 (Core,
asmdefs, UI, scripts, multiplayer, assets). Sections 5 and 6 are now all done: 41 of the 42 build-order rows have their ticket closed, and row 42 is U.CHN-T63,
which closes once this lands. Every user story is `done` in the ticket database (USER_STORIES.md is its generated
view); the test behind each is the Ref of the ticket that implements it (`tickets ticket show <ID>`), for example
F3 `run-core-tests.ps1`, G5 `LevelAndHudTests.GameOver_FadesFiveAFrame_ThenResetsItself`, D1 `HudTests`,
C1 `ScriptTests`, C2 `ScripterConsoleTests`, V1 `ChainOnlineTests`, V2 `ScriptedPlaythroughTests`.

**Method and its limit.** This pass read each row against the code and the tests on main and ran `dotnet build` on
`Tests/Core.Tests` (0 errors). It did not run the Unity build or the test suites, and did not re-capture: the
host had about 1 GB free RAM with other Editors open and the worktree has no Library. It is therefore a code-and-test
reading, not a running-build pass; the orchestrator's full suite on main is the run, and U.CHN-T75 is the re-capture.
Open after this pass: U.CHN-T75 (demo media); U.CHN-T76 has since settled V4 and V12.
