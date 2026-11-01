using System.Collections.Generic;
using System.Linq;
using MindAttic.Chain;
using MindAttic.Chain.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace MindAttic.Chain.Tests
{
    public class LevelComponentTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }

            _objects.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
        }

        private LevelBounds MakeLevel(Vector2 center, Vector2 size)
        {
            var go = Track(new GameObject("Level"));
            go.transform.position = center;
            var level = go.AddComponent<LevelBounds>();
            level.Size = size;
            return level;
        }

        [Test]
        public void LevelBounds_IsCenteredOnTransformPlusOffset()
        {
            var level = MakeLevel(new Vector2(10f, 5f), new Vector2(20f, 10f));
            level.Offset = new Vector2(1f, -1f);

            Assert.AreEqual(new Rect(1f, -1f, 20f, 10f), level.Bounds);
            Assert.AreEqual(new Rect(2f, 0f, 18f, 8f), level.Inset(new Vector2(1f, 1f)));
            Assert.IsTrue(level.Contains(new Vector2(11f, 4f)));
            Assert.AreEqual(new Vector2(21f, -1f), level.Clamp(new Vector2(99f, -99f)));
        }

        [Test]
        public void AngelController_ClampsToLevelInsetByMargin()
        {
            var level = MakeLevel(Vector2.zero, new Vector2(30f, 12f));
            var angel = Track(new GameObject("Angel")).AddComponent<AngelController>();
            angel.LevelBounds = level;

            // Default margin is (0.5, 0.6).
            var b = angel.Bounds;
            Assert.AreEqual(-14.5f, b.xMin, 1e-4f);
            Assert.AreEqual(14.5f, b.xMax, 1e-4f);
            Assert.AreEqual(-5.4f, b.yMin, 1e-4f);
            Assert.AreEqual(5.4f, b.yMax, 1e-4f);
        }

        [Test]
        public void Chain_BouncesLinksOffTheLevel_UnlessKeepInLevelIsOff()
        {
            using (var rig = new TestRig(levelSize: new Vector2(4f, 2f)))
            {
                // The bounce is a data option, off by default as Chain.vb's checkBounds is commented out (U.CHN-T76).
                rig.Chain.KeepInLevel = true;
                rig.MoveAngelTo(new Vector2(0f, 0.9f));
                // The longest chain (12 links) hangs about two units at chain.json's figures, past the floor.
                while (rig.Mace.Lengthen())
                {
                }

                for (var i = 0; i < 200; i++)
                {
                    rig.Step();
                }

                for (var i = 1; i < rig.Chain.LinkCount; i++)
                {
                    Assert.GreaterOrEqual(rig.Chain.GetLinkPosition(i).y, -1f - 1e-4f, $"link {i} fell through the floor");
                }

                rig.Chain.KeepInLevel = false;
                for (var i = 0; i < 200; i++)
                {
                    rig.Step();
                }

                Assert.Less(rig.Mace.MacePosition.y, -1f, "with bounds off the chain hangs past the floor");
            }
        }

        private CameraRig MakeCamera(float aspect)
        {
            var go = Track(new GameObject("Camera"));
            go.transform.position = new Vector3(0f, 0f, -10f);
            var cam = go.AddComponent<Camera>();
            cam.orthographic = true;
            cam.aspect = aspect;
            return go.AddComponent<CameraRig>();
        }

        [Test]
        public void CameraRig_Original_ShowsCameraVbsFixedView_At4By3()
        {
            // U.CHN-T86: Camera.vb's 45 degree lens from z -1 shows tan(22.5) of the plane either side of the origin,
            // 4:3, whatever the level; the level's arena does not widen it.
            var level = MakeLevel(new Vector2(3f, 2f), new Vector2(16f, 9f));
            var rig = MakeCamera(16f / 9f);
            rig.Level = level;
            rig.Mode = CameraFramingMode.Original;

            rig.Snap();

            var tuning = SettingsStore.Current.CameraTuning;
            Assert.AreEqual(tuning.OriginalHalfHeight, rig.Camera.orthographicSize, 1e-4f);
            Assert.AreEqual(9f * Mathf.Tan(Mathf.PI / 8f), rig.Camera.orthographicSize, 1e-3f);
            Assert.AreEqual(new Vector3(0f, 0f, -10f), rig.transform.position, "centred on the origin, z kept");
            var pixels = rig.Camera.pixelRect;
            if (pixels.height > 0f && Screen.height > 0)
            {
                Assert.AreEqual(4f / 3f, pixels.width / pixels.height, 0.02f, "shown 4:3, with bars on any other screen");
            }

            rig.Mode = CameraFramingMode.FitLevel;
            rig.Snap();
            Assert.AreEqual(new Rect(0f, 0f, 1f, 1f), rig.Camera.rect, "the other modes use the whole screen");
        }

        [Test]
        public void CameraRig_FitLevel_ShowsWholeLevelCentered()
        {
            var level = MakeLevel(new Vector2(3f, 2f), new Vector2(16f, 9f));
            var rig = MakeCamera(4f / 3f);
            rig.Level = level;
            rig.Mode = CameraFramingMode.FitLevel;

            rig.Snap();

            Assert.AreEqual(6f, rig.Camera.orthographicSize, 1e-4f);
            Assert.AreEqual(new Vector3(3f, 2f, -10f), rig.transform.position, "z is kept");
            var view = rig.ViewRect;
            Assert.LessOrEqual(view.xMin, level.Bounds.xMin + 1e-4f);
            Assert.GreaterOrEqual(view.xMax, level.Bounds.xMax - 1e-4f);
        }

        [Test]
        public void CameraRig_Follow_TracksTarget_ButNeverShowsPastTheLevel()
        {
            var level = MakeLevel(Vector2.zero, new Vector2(40f, 20f));
            var rig = MakeCamera(16f / 9f);
            var target = Track(new GameObject("Target")).transform;
            rig.Level = level;
            rig.Target = target;
            rig.Mode = CameraFramingMode.Follow;
            rig.FollowSize = 4.5f;
            rig.DeadZoneHalf = new Vector2(1f, 1f);
            rig.Sharpness = 0f;

            target.position = new Vector3(5f, 0.5f, 0f);
            rig.Frame(1f / 60f);
            Assert.AreEqual(4f, rig.transform.position.x, 1e-4f, "dragged to keep the target at the dead-zone edge");
            Assert.AreEqual(0f, rig.transform.position.y, 1e-4f, "inside the dead zone vertically");

            target.position = new Vector3(100f, 100f, 0f);
            rig.Frame(1f / 60f);
            var view = rig.ViewRect;
            Assert.AreEqual(level.Bounds.xMax, view.xMax, 1e-3f);
            Assert.AreEqual(level.Bounds.yMax, view.yMax, 1e-3f);
        }

        [Test]
        public void CameraRig_Follow_SmoothsTowardTheGoal()
        {
            var level = MakeLevel(Vector2.zero, new Vector2(40f, 20f));
            var rig = MakeCamera(16f / 9f);
            var target = Track(new GameObject("Target")).transform;
            rig.Level = level;
            rig.Target = target;
            rig.Mode = CameraFramingMode.Follow;
            rig.DeadZoneHalf = Vector2.zero;
            rig.Sharpness = 5f;

            target.position = new Vector3(6f, 0f, 0f);
            rig.Frame(0.1f);
            var x = rig.transform.position.x;
            Assert.Greater(x, 0f);
            Assert.Less(x, 6f, "eases in rather than snapping");
        }

        [Test]
        public void CameraRig_SessionReset_RecentresIt()
        {
            using (var testRig = new TestRig())
            {
                var rig = MakeCamera(16f / 9f);
                rig.Level = testRig.Level;
                rig.Target = testRig.Angel.transform;
                rig.Mode = CameraFramingMode.Follow;
                rig.Session = testRig.Session;
                rig.transform.position = new Vector3(6f, -3f, -10f); // a shake or a debug pan

                testRig.Session.Submit(new ResetGame(testRig.Id));

                Assert.AreEqual(new Vector3(0f, 0f, -10f), rig.transform.position, "Root.vb's Reset: cam and target at the origin");
                rig.Session = null;
            }
        }

        [Test]
        public void CameraRig_ShakeScreen_JumpsThenRestores_OnTopOfTheFraming()
        {
            var level = MakeLevel(new Vector2(2f, 1f), new Vector2(16f, 9f));
            var rig = MakeCamera(16f / 9f);
            rig.Level = level;
            rig.Mode = CameraFramingMode.FitLevel;
            rig.Snap();
            var amount = rig.Shake.Amount;
            Assert.Greater(amount, 0f, "camera.json's shakeAmount");

            rig.ShakeScreen(); // shake starts False: the first press restores
            Assert.AreEqual(Vector2.zero, rig.ShakeOffset);
            Assert.AreEqual(new Vector3(2f, 1f, -10f), rig.transform.position);

            var offset = rig.ShakeScreen();
            Assert.GreaterOrEqual(offset.x, 0f, "right");
            Assert.LessOrEqual(offset.x, amount * 2f);
            Assert.LessOrEqual(offset.y, 0f, "down");
            Assert.GreaterOrEqual(offset.y, -amount * 2f);
            Assert.AreEqual(2f + offset.x, rig.transform.position.x, 1e-5f);
            Assert.AreEqual(1f + offset.y, rig.transform.position.y, 1e-5f);

            rig.Frame(0.1f); // framing again keeps the shake and does not drift the base view
            Assert.AreEqual(2f + offset.x, rig.transform.position.x, 1e-5f);
            Assert.AreEqual(1f + offset.y, rig.transform.position.y, 1e-5f);

            rig.ShakeScreen();
            Assert.AreEqual(Vector2.zero, rig.ShakeOffset);
            Assert.AreEqual(new Vector3(2f, 1f, -10f), rig.transform.position, "the next press puts the target back on the camera");
        }

        [Test]
        public void CameraRig_Recenter_DropsTheShake()
        {
            var rig = MakeCamera(16f / 9f);
            rig.ShakeScreen();
            rig.ShakeScreen();
            rig.Recenter();

            Assert.AreEqual(Vector2.zero, rig.ShakeOffset);
            Assert.AreEqual(new Vector3(0f, 0f, -10f), rig.transform.position);
            Assert.IsFalse(rig.Shake.Armed, "Reset leaves Root.vb's shake flag alone");
        }

        [Test]
        public void CameraRig_Perspective_IsCameraVbsCameraAsData()
        {
            var rig = MakeCamera(16f / 9f);
            var lens = rig.Perspective;

            Assert.AreEqual(Mathf.PI / 4f, lens.Fov, 1e-5f, "45 degrees");
            Assert.AreEqual(4f / 3f, lens.Aspect, 1e-5f);
            Assert.Less(lens.EyeZ, 0f, "the eye at z -1, in front of the z 0 plane");
        }

        [Test]
        public void CameraRig_NumpadMoves_PanAndZoomTheFramedView_UNI_T57()
        {
            var level = MakeLevel(new Vector2(2f, 1f), new Vector2(16f, 9f));
            var rig = MakeCamera(16f / 9f);
            rig.Level = level;
            rig.Mode = CameraFramingMode.FitLevel;
            rig.Snap();
            var size = rig.Camera.orthographicSize;
            var step = rig.Perspective.StepSpeed;

            rig.Strafe(+1, 1f / 60f); // numpad 6, one frame
            Assert.AreEqual(2f + step / 60f, rig.transform.position.x, 1e-4f, "strafe_left: cam.X += camSPD");
            Assert.AreEqual(Quaternion.identity, rig.transform.rotation, "eye and target together: still straight on");
            rig.Frame(0.1f);
            Assert.AreEqual(2f + step / 60f, rig.LookPoint.x, 1e-4f, "kept on top of the framing");

            rig.Lift(-1, 1f / 60f); // numpad 2
            Assert.AreEqual(1f - rig.Perspective.LiftSpeed / 60f, rig.LookPoint.y, 1e-4f, "move_down");

            rig.MoveForward(+1, 10f / 60f); // numpad plus, ten frames: the eye 0.88 of the way
            Assert.AreEqual(size * 0.88f, rig.Camera.orthographicSize, 1e-3f, "nearer the plane the view shows less");
            Assert.AreEqual(-10f, rig.transform.position.z, 1e-4f, "the camera stays at its depth");
        }

        [Test]
        public void CameraRig_XKey_SlantsTheView_AndShakeScreensRestoreStraightensIt()
        {
            var rig = MakeCamera(16f / 9f);
            rig.Mode = CameraFramingMode.FitLevel;
            rig.Snap();

            rig.Isometric(+1, 1f / 60f); // X, one frame: cam.X and cam.Y += 0.25
            var forward = rig.transform.forward;
            Assert.Less(forward.x, 0f, "the eye moved up and right, so it looks back left");
            Assert.Less(forward.y, 0f, "and down");
            Assert.AreEqual(forward.x, forward.y, 1e-4f, "on x and y alike");
            Assert.AreEqual(Vector2.zero, rig.LookPoint, "still looking at the target");
            var ahead = rig.transform.position + forward * 10f;
            Assert.AreEqual(0f, ahead.x, 1e-3f);
            Assert.AreEqual(0f, ahead.y, 1e-3f);
            Assert.AreEqual(0f, ahead.z, 1e-3f);

            rig.ShakeScreen(); // shake starts False: the restore, target = cam
            Assert.AreEqual(Quaternion.identity, rig.transform.rotation, "straight again");
            var eye = rig.View.Eye.ToUnity();
            Assert.AreEqual(eye, rig.LookPoint, "now looking at the plane under the eye");

            rig.Recenter();
            Assert.AreEqual(new Vector3(0f, 0f, -10f), rig.transform.position, "Root.vb's Reset");
            Assert.IsFalse(rig.View.IsMoved);
        }

        [Test]
        public void CameraRig_WhilePaused_TheViewWaitsForTheGame()
        {
            using (var testRig = new TestRig())
            {
                var rig = MakeCamera(16f / 9f);
                rig.Mode = CameraFramingMode.FitLevel;
                rig.Session = testRig.Session;
                rig.Snap();
                var before = rig.transform.position;

                testRig.Session.Submit(new Pause(testRig.Id, true));
                rig.Strafe(+1, 1f);
                Assert.AreEqual(before, rig.transform.position, "Engine.vb rebuilds the camera only while not paused");
                Assert.Greater(rig.View.Eye.X, 0f, "but Input.vb still took the key");

                testRig.Session.Submit(new Pause(testRig.Id, false));
                rig.Strafe(0, 0f);
                Assert.Greater(rig.transform.position.x, before.x, "shown once the game runs");
                rig.Session = null;
            }
        }

        [Test]
        public void ChainControls_NumpadXAndZ_AreTheDebugCamera()
        {
            using (var controls = new ChainControls())
            {
                var keys = new[]
                {
                    (controls.CameraForward, "<Keyboard>/numpadPlus"),
                    (controls.CameraBackward, "<Keyboard>/numpadEnter"),
                    (controls.CameraLeft, "<Keyboard>/numpad4"),
                    (controls.CameraRight, "<Keyboard>/numpad6"),
                    (controls.CameraUp, "<Keyboard>/numpad8"),
                    (controls.CameraDown, "<Keyboard>/numpad2"),
                    (controls.Isometric, "<Keyboard>/x"),
                    (controls.Deisometric, "<Keyboard>/z"),
                };

                foreach (var (action, path) in keys)
                {
                    Assert.AreEqual(path, action.bindings[0].path, action.name);
                    Assert.AreSame(controls.Debug, action.actionMap, action.name);
                }
            }
        }

        [Test]
        public void ChainControls_ZeroKey_IsShakeScreen()
        {
            using (var controls = new ChainControls())
            {
                Assert.AreEqual("<Keyboard>/digit0", controls.ShakeScreen.bindings[0].path);
                Assert.AreSame(controls.Debug, controls.ShakeScreen.actionMap);
            }
        }

        [Test]
        public void ChainControls_F4_IsToggleFps()
        {
            using (var controls = new ChainControls())
            {
                Assert.AreEqual("<Keyboard>/f4", controls.ToggleFps.bindings[0].path);
                Assert.AreSame(controls.Debug, controls.ToggleFps.actionMap);
            }
        }

        [Test]
        public void ChainControls_TabAndTheMenusFKeys_AreDebugToggles()
        {
            using (var controls = new ChainControls())
            {
                var keys = new[]
                {
                    (controls.ToggleMenu, "<Keyboard>/tab"),
                    (controls.ToggleWireframe, "<Keyboard>/f1"),
                    (controls.ToggleTextures, "<Keyboard>/f2"),
                    (controls.ToggleCulling, "<Keyboard>/f3"),
                    (controls.ConfigureVideo, "<Keyboard>/f12"),
                    (controls.ToggleZones, "<Keyboard>/e"),
                };
                foreach (var (action, path) in keys)
                {
                    Assert.AreEqual(1, action.bindings.Count, action.name);
                    Assert.AreEqual(path, action.bindings[0].path, action.name);
                    Assert.AreSame(controls.Debug, action.actionMap, action.name);
                }
            }
        }
    }

    public class HudTextTests
    {
        [Test]
        public void Hp_IsOneBarPerHitPoint()
        {
            Assert.AreEqual("HP : |||", HudText.Hp(3));
            Assert.AreEqual("HP : ", HudText.Hp(0));
            Assert.AreEqual("HP : ", HudText.Hp(-4));
        }

        [Test]
        public void Score_And_Grappled_UseInvariantFormatting()
        {
            Assert.AreEqual("Score : 12345", HudText.Score(12345));
            Assert.AreEqual("Grappled: 0.75", HudText.Grappled(0.75f));
            Assert.AreEqual("Grappled: 2", HudText.Grappled(2f));
            Assert.AreEqual("Grappled: 0", HudText.Grappled(-1f));
        }

        [Test]
        public void Readout_GameSpeed_AndFps_AreRenderDisplaysAndRenderFpssWording()
        {
            Assert.AreEqual("Res 0.25\nGrav -0.0025\nSpring 0.1", HudText.Readout(new ChainReadout(0.25f, -0.0025f, 0.1f)));
            Assert.AreEqual("1.00", HudText.GameSpeed(1f), "FormatNumber(gameSpeed, 2)");
            Assert.AreEqual("0.97", HudText.GameSpeed(0.97f));
            Assert.AreEqual("60 fps", HudText.Fps(60));
        }

        [Test]
        public void Clock_IsUpdateClocksMSs()
        {
            Assert.AreEqual("0:00", HudText.Clock(0f));
            Assert.AreEqual("0:07", HudText.Clock(7.4f));
            Assert.AreEqual("2:15", HudText.Clock(135f));
        }
    }

    public class GameSessionTests
    {
        private TestRig _rig;
        private readonly List<GameEvent> _events = new List<GameEvent>();

        [SetUp]
        public void SetUp()
        {
            _rig = new TestRig();
            _rig.Session.EventRaised += _events.Add;
        }

        [TearDown]
        public void TearDown()
        {
            _rig.Dispose();
            _events.Clear();
        }

        private GameSession Session => _rig.Session;

        [Test]
        public void Join_SeatsTheAngel_WhichDrawsWhereTheWorldFliesIt()
        {
            Assert.AreSame(Session, _rig.Angel.Session);
            Assert.AreEqual(new PlayerId(0), _rig.Angel.Seat);
            Assert.IsNotNull(Session.World.FindPlayer(_rig.Angel.Seat));

            Assert.IsTrue(_rig.Angel.SendInput(new Vector2(100f, -50f), 0f));

            var flown = new Vector2(100f, -50f) * _rig.Player.Setup.Flight.Sensitivity; // angel.json's Ratio3D
            Assert.AreEqual(flown.x, _rig.Player.Flight.Position.X, 1e-5f);
            Assert.AreEqual(flown.y, _rig.Player.Flight.Position.Y, 1e-5f);
            Assert.AreEqual(flown + _rig.Angel.SpriteOffset + new Vector2(0f, _rig.Player.Flight.HoverOffset), (Vector2)_rig.Angel.transform.position,
                "the sprite is drawn on renderAngel's quad, off the flight position (U.CHN-T93)");
            Assert.Throws<System.ArgumentException>(() => Session.Join(_rig.Angel), "a seat is taken once");
        }

        [Test]
        public void EnemyTouchingTheAngel_CostsHp_ThroughTheHurtDelay()
        {
            // -0.2, not the Circle-era -0.5 (U.CHN-T107): angel.json's port.contactShape default has been
            // Rectangle since U.CHN-T88, and the angel's own rect (Angel.vb's X..X+0.06) sits to the right of
            // its position, not centred on it, so -0.5 world units (scaled by space.json's lengthScale 9) no
            // longer reaches the enemy's rectangle at all; -0.2 overlaps it immediately and stays put for every
            // step below, same as the stationary -0.4/-0.5 this test used back when the port's own default was
            // still the earlier Circle contact (commit 65eaed3).
            _rig.SpawnEnemy(new Vector2(-0.2f, 0f));

            _rig.Step(0.1f);
            Assert.AreEqual(29, Session.State.Hp);
            _rig.Step(0.1f);
            Assert.AreEqual(29, Session.State.Hp);

            // PhysicsStepRate's 60 fps sub-stepping (BIBLE D9, U.CHN-T45) crosses the 0.25 s delay partway
            // through this step (at game time 16/60 s), not only on a whole 0.1 s boundary.
            _rig.Step(0.1f);
            Assert.AreEqual(28, Session.State.Hp);
            _rig.Step(0.1f);
            Assert.AreEqual(28, Session.State.Hp);

            var hurt = _events.FindAll(e => e is AngelHurt);
            Assert.AreEqual(2, hurt.Count);
            Assert.AreEqual(_rig.Id, ((AngelHurt)hurt[0]).Player);
        }

        [Test]
        public void MaceHit_IsScored()
        {
            _rig.Swing();
            _rig.SpawnEnemy(_rig.Player.MacePosition.ToUnity());
            _rig.Step();

            var hit = _events.Find(e => e is EnemyHit) as EnemyHit;
            Assert.IsNotNull(hit);
            Assert.Greater(hit.Points, 0);
            Assert.AreEqual(hit.Points, Session.State.Score);
        }

        [Test]
        public void PauseKey_PausesThroughACommand_AndFreezesTheWorld()
        {
            _rig.Angel.SendInput(Vector2.zero, 0f, pausePressed: true);
            Assert.IsTrue(Session.State.IsPaused);
            var paused = _events.Find(e => e is PauseChanged) as PauseChanged;
            Assert.IsNotNull(paused);
            Assert.AreEqual(_rig.Id, paused.Player);

            var time = Session.World.Time;
            Assert.IsFalse(_rig.Angel.SendInput(new Vector2(100f, 0f), 0.1f), "the angel does not fly while paused");
            _rig.Step();
            Assert.AreEqual(time, Session.World.Time);

            _rig.Angel.SendInput(Vector2.zero, 0f, pausePressed: true);
            Assert.IsFalse(Session.State.IsPaused);
        }

        [Test]
        public void Restart_ResetsInPlace()
        {
            Session.State.AddHitScore(2f);
            _rig.MoveAngelTo(new Vector2(2f, 2f));
            Session.Submit(new Pause(_rig.Id, true));

            Session.Restart();

            Assert.AreEqual(0, Session.State.Score);
            Assert.IsTrue(Session.State.IsPlaying);
            Assert.AreEqual(Vector2.zero, _rig.Player.Flight.Position.ToUnity());
            Assert.IsTrue(_events.Exists(e => e is WorldReset));
        }

        [Test]
        public void GameOver_FadesFiveAFrame_ThenResetsItself()
        {
            var state = new GameState();
            state.AddHitScore(2f);
            Assert.IsFalse(state.AdvanceGameOver(), "nothing happens while playing");
            Assert.AreEqual(GameState.FullAmbient, state.Ambient);

            // HpOvershoot's default of 1 (BIBLE D10, U.CHN-T45) takes one hit past zero, not just to zero.
            for (var i = 0; i <= state.MaxHp; i++) { state.TryHurt(i); }
            Assert.IsTrue(state.IsGameOver);

            Assert.IsFalse(state.AdvanceGameOver());
            Assert.AreEqual(250, state.Ambient);

            var frames = 1;
            while (!state.AdvanceGameOver())
            {
                frames++;
                Assert.Less(frames, 100);
            }

            Assert.AreEqual(50, frames, "255 down by 5 while above 6, then the finished frame");
            Assert.IsTrue(state.IsGameOver, "the fade only reports done; the world resets");
            Assert.AreEqual(GameState.FadeFloor - 1, state.Ambient);
            state.Reset();
            Assert.IsFalse(state.IsGameOver);
            Assert.AreEqual(state.MaxHp, state.Hp);
            Assert.AreEqual(0, state.Score);
            Assert.AreEqual(GameState.FullAmbient, state.Ambient);
        }

        [Test]
        public void Session_GameOver_RestartsWithoutAClick()
        {
            Session.State.Config.HurtDelay = 0f;
            _rig.SpawnEnemy(new Vector2(-0.5f, 0f));
            for (var i = 0; i < 300 && !Session.State.IsGameOver; i++) { _rig.Step(); }
            Assert.IsTrue(Session.State.IsGameOver);
            Assert.IsTrue(_events.Exists(e => e is GameOver));

            for (var i = 0; i < 60 && Session.State.IsGameOver; i++) { _rig.Step(); }

            Assert.IsTrue(_events.Exists(e => e is WorldReset));
            Assert.IsTrue(Session.State.IsPlaying);
            Assert.AreEqual(Session.State.MaxHp, Session.State.Hp);
        }

        [Test]
        public void Hud_ShowsGrappledEnemyHp_OnlyWhileHeld()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            hud.Grapple = _rig.Grapple;
            Assert.IsNull(hud.GrappledHp);

            _rig.SpawnEnemy(new Vector2(5f, 5f), hp: 1f);
            _rig.Grapple.SetButtonHeld(true);
            _rig.Step(); // arms; the enemy is far from the mace, so nothing is caught yet
            Assert.IsNull(hud.GrappledHp);

            _rig.SpawnEnemy(_rig.Player.MacePosition.ToUnity(), hp: 0.75f);
            _rig.Step();
            Assert.AreEqual(0.75f, hud.GrappledHp.Value, 1e-5f);
            hud.Refresh();
            Assert.AreEqual(HudText.Grappled(0.75f), hud.GrappledText);
        }

        [Test]
        public void Hud_IsAUiToolkitTree_ShowingHpAndScore_FromTheWorldsEvents()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            Assert.IsNotNull(hud.Root, "the HUD root is built in code");
            Assert.AreEqual(PickingMode.Ignore, hud.Root.pickingMode, "the HUD lets clicks through");
            Assert.AreEqual(HudText.Hp(Session.State.Hp, Session.State.Config.HpOvershoot), hud.HpText);
            Assert.AreEqual(HudText.Score(0), hud.ScoreText);
            Assert.IsNull(hud.GrappledText, "nothing is grappled");
            Assert.IsNull(hud.BannerText, "no banner while playing");
            Assert.AreEqual(0f, hud.OverlayDim);

            Session.State.Config.HurtDelay = 0f;
            Session.State.TryHurt(0);
            hud.Refresh();
            Assert.AreEqual(HudText.Hp(Session.State.Hp, Session.State.Config.HpOvershoot), hud.HpText, "the HP line follows the world");

            var panel = hud.AddPanel(new VisualElement { name = "later-panel" });
            Assert.AreSame(hud.Root, panel.parent, "later panels go on the HUD root");
            Assert.AreEqual(hud.Root.childCount - 1, hud.Root.IndexOf(panel), "and draw over the HUD's own layers");
        }

        [Test]
        public void Hud_ShowsThePauseBanner_OverADimOverlay()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            _ = hud.Root;

            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, true)));
            Assert.AreEqual(HudText.Paused, hud.BannerText, "the pause event refreshes the HUD");
            Assert.AreEqual(hud.Layout.Port.PauseDim, hud.OverlayDim, 1e-5f);
            Assert.IsFalse(hud.StatusShown, "oneFrameRender draws none of the HUD while paused");
            Assert.IsNull(hud.FpsText, "nor the frame rate");

            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, false)));
            Assert.IsNull(hud.BannerText);
            Assert.AreEqual(0f, hud.OverlayDim);
            Assert.IsTrue(hud.StatusShown);
            Assert.IsNotNull(hud.FpsText);
        }

        [Test]
        public void Hud_LaysOutGuiVbAndRenderDisplay_FromHudJson()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            var layout = hud.Layout;
            var scale = hud.Scale;
            Assert.AreEqual(GameHud.ReferenceResolution.y / 448f, scale, 1e-5f, "the original's 448-pixel-high window fills the panel's height");

            // Gui.vb renderStatus: status_box.png, 96 x 48 at the top left.
            var box = hud.StatusBox;
            Assert.IsNotNull(box.style.backgroundImage.value.texture, "the status box art loads from Resources");
            Assert.AreEqual(0f, box.style.left.value.value);
            Assert.AreEqual(0f, box.style.top.value.value);
            Assert.AreEqual(96f * scale, box.style.width.value.value, 1e-4f);
            Assert.AreEqual(48f * scale, box.style.height.value.value, 1e-4f);

            // renderDisplay's Write calls: HP at (20, 10) red, Res/Grav/Spring at (50, 50) blue.
            var hp = hud.Root.Q<Label>("hud-hp");
            Assert.AreEqual(20f * scale, hp.style.left.value.value, 1e-4f);
            Assert.AreEqual(10f * scale, hp.style.top.value.value, 1e-4f);
            Assert.AreEqual((Color)new Color32(255, 0, 0, 255), hp.style.color.value);
            Assert.AreEqual(10f * 96f / 72f * scale, hp.style.fontSize.value.value, 1e-4f, "fontMenu, Arial 10 pt");
            Assert.AreEqual((Color)new Color32(0, 0, 255, 255), hud.Root.Q<Label>("hud-readout").style.color.value);
            Assert.AreEqual((Color)new Color32(255, 255, 0, 255), hud.Root.Q<Label>("hud-grappled").style.color.value, "grappled HP in yellow");
            Assert.AreEqual((Color)new Color32(255, 0, 0, 128), hud.Root.Q<Label>("hud-pause").style.color.value, "- PAUSE - half see-through in colorReticle");

            // The game speed sits rTarget.Width - 50 from the left: 50 in from the right edge.
            var speed = hud.Root.Q<Label>("hud-game-speed");
            Assert.AreEqual(LengthUnit.Percent, speed.style.left.value.unit);
            Assert.AreEqual(100f, speed.style.left.value.value);
            Assert.AreEqual(-50f * scale, speed.style.marginLeft.value.value, 1e-4f);

            Assert.AreEqual(HudText.GameSpeed(1f), hud.GameSpeedText);
            Assert.AreEqual("Res 0.25\nGrav -0.0025\nSpring 0.1", hud.ReadoutText, "chain.json's figures, in the original's units");
            Assert.AreEqual(100, layout.FpsSampleMs);
        }

        [Test]
        public void Hud_Fps_SamplesEvery100Ms_F4HidesIt_AndAResetShowsItAgain()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            Assert.IsTrue(hud.ShowFps, "bDrawFPS starts True");

            // A sample at 100 ms, then 6 frames counted before the next at 200 ms: 6 x (1000 / 100) = 60 fps.
            hud.TickFps(100L);
            for (var i = 1; i < 6; i++) { hud.TickFps(100L + i * 16L); }
            hud.TickFps(200L);
            Assert.AreEqual(HudText.Fps(60), hud.FpsText);

            Assert.IsFalse(hud.ToggleFps());
            Assert.IsNull(hud.FpsText, "F4 hides it");

            Session.Restart();
            Assert.IsTrue(hud.ShowFps, "Root.vb's Reset sets bDrawFPS = True");
            Assert.IsNotNull(hud.FpsText);
        }

        [Test]
        public void Hud_Menu_TabShowsMenuJpgAt10_10_WithEachLinesState_AndEachKeyActsOnIt()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            var scale = hud.Scale;
            Assert.IsFalse(hud.MenuShown, "bMenu starts False");

            // Gui.vb loadMenu: menu.jpg on a 255 x 255 quad at (10, 10), over the HUD's own layers.
            var art = hud.Root.Q<VisualElement>("hud-menu-art");
            Assert.IsNotNull(art.style.backgroundImage.value.texture, "menu.jpg loads from Resources");
            Assert.AreEqual(10f * scale, art.style.left.value.value, 1e-4f);
            Assert.AreEqual(10f * scale, art.style.top.value.value, 1e-4f);
            Assert.AreEqual(255f * scale, art.style.width.value.value, 1e-4f);
            Assert.AreEqual(255f * scale, art.style.height.value.value, 1e-4f);
            Assert.AreEqual(PickingMode.Ignore, hud.MenuPanel.pickingMode, "the menu lets the mouse steer");
            Assert.Greater(hud.Root.IndexOf(hud.MenuPanel), hud.Root.IndexOf(hud.StatusBox.parent), "drawn over the status layer");

            // renderMenu's Write calls: (20, 20) down to ESC at (20, 240), in 255, 255, 255, 64.
            var quit = hud.Root.Q<Label>("hud-menu-quit");
            Assert.AreEqual(20f * scale, quit.style.left.value.value, 1e-4f);
            Assert.AreEqual(240f * scale, quit.style.top.value.value, 1e-4f);
            Assert.AreEqual((Color)new Color32(255, 255, 64, 255), quit.style.color.value);

            Assert.IsTrue(hud.ToggleMenu(), "Tab opens it");
            Assert.IsTrue(hud.MenuShown);
            Assert.AreEqual("F1 - Turn to Wireframe", hud.MenuLineText(MenuEntry.Wireframe));
            Assert.AreEqual("F4 - Hide FPS", hud.MenuLineText(MenuEntry.Fps));
            Assert.AreEqual("F12 - Configure Video", hud.MenuLineText(MenuEntry.Video));
            Assert.AreEqual("ESC - Quit", hud.MenuLineText(MenuEntry.Quit));

            // F1-F4 act on their flag and the line follows; F4 is the HUD's own frame rate toggle.
            Assert.IsTrue(hud.Act(MenuEntry.Wireframe));
            Assert.IsTrue(hud.Options.Wireframe);
            Assert.AreEqual("F1 - Turn to Solid", hud.MenuLineText(MenuEntry.Wireframe));
            Assert.IsFalse(hud.Act(MenuEntry.Textures));
            Assert.AreEqual("F2 - Use Textured", hud.MenuLineText(MenuEntry.Textures));
            Assert.IsTrue(hud.Act(MenuEntry.Culling));
            Assert.AreEqual("F3 - Disable Culling", hud.MenuLineText(MenuEntry.Culling));
            Assert.IsFalse(hud.ToggleFps());
            Assert.IsNull(hud.FpsText, "F4 hides the frame rate");
            Assert.AreEqual("F4 - Show FPS", hud.MenuLineText(MenuEntry.Fps));

            // oneFrameRender draws renderMenu only while not paused; Tab still flips it.
            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, true)));
            Assert.IsFalse(hud.MenuShown, "hidden while paused");
            Assert.IsTrue(hud.Options.MenuOpen, "but still open");
            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, false)));
            Assert.IsTrue(hud.MenuShown);

            // ESC is the same Quit the seat's Escape sends: the world's QuitGame, raising QuitRequested.
            Assert.IsTrue(hud.Act(MenuEntry.Quit));
            Assert.AreEqual(_rig.Id, _events.OfType<QuitRequested>().Single().Player);

            // Root.vb's Reset puts every flag back and closes the menu.
            Session.Restart();
            Assert.IsFalse(hud.MenuShown, "bMenu = False");
            Assert.IsFalse(hud.Options.Wireframe);
            Assert.IsTrue(hud.Options.Textured);
            Assert.IsFalse(hud.Options.BackFaceCulling);
            Assert.IsTrue(hud.ShowFps);
        }

        [Test]
        public void Hud_Menu_F12_OpensFrmConfig_AndSubmitKeepsItInTheProfile()
        {
            var previous = PlayerProfile.Store;
            var store = new MemoryPreferencesStore(new PlayerPreferences
            {
                StockVolumesApplied = true,
                WindowMode = WindowMode.Windowed,
                MusicVolume = 0.5f,
                SfxVolume = 0.5f,
                AmbientVolume = 0.5f,
            });
            PlayerProfile.Store = store;
            try
            {
                var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
                hud.Session = Session;
                Assert.IsFalse(hud.ConfigShown, "closed until F12");
                Assert.Greater(hud.Root.IndexOf(hud.Config), hud.Root.IndexOf(hud.MenuPanel), "drawn over the menu");
                Assert.AreEqual(PickingMode.Position, hud.Config.pickingMode, "its controls take the mouse");

                // F12 opens it on the profile's settings (frm_config_Load) and holds the world still while it is open.
                Assert.IsTrue(hud.Act(MenuEntry.Video));
                Assert.IsTrue(hud.ConfigShown);
                Assert.IsTrue(Session.State.IsPaused);
                var form = hud.Config.Form;
                Assert.IsFalse(form.Fullscreen);
                var resolution = hud.Config.Q<DropdownField>("config-resolution");
                Assert.IsFalse(resolution.enabledSelf, "fra_res is disabled while windowed");
                Assert.AreEqual("640 x 480", resolution.value);
                Assert.AreEqual(0.5f, hud.Config.Q<Slider>("config-ambient").value, 1e-6f);

                // F12 again closes it and keeps nothing.
                form.SetFullscreen(true);
                Assert.IsFalse(hud.Act(MenuEntry.Video));
                Assert.IsFalse(Session.State.IsPaused, "the world goes on");
                Assert.AreEqual(0, store.Writes);
                Assert.AreEqual(WindowMode.Windowed, PlayerProfile.Preferences.WindowMode);

                // Fullscreen at 1024 x 768 with new volumes, then Submit: into the profile, applied, closed.
                hud.OpenConfig();
                form.SetFullscreen(true);
                Assert.IsTrue(resolution.enabledSelf, "fra_res is enabled in fullscreen");
                Assert.IsTrue(form.SelectResolution(2));
                Assert.AreEqual("1024 x 768", resolution.value);
                form.SetMusicVolume(0.25f);
                form.SetSfxVolume(0.75f);
                form.SetAmbientVolume(0f);
                Assert.AreEqual(0.25f, hud.Config.Q<Slider>("config-music").value, 1e-6f);
                var heard = 0;
                PlayerProfile.Changed += Heard;
                try
                {
                    hud.Config.Submit();
                }
                finally
                {
                    PlayerProfile.Changed -= Heard;
                }

                Assert.AreEqual(1, store.Writes, "Submit keeps it in the profile");
                Assert.AreEqual(1, heard, "the music and sound effects take the new volumes at once");
                var kept = store.Read();
                Assert.AreEqual(WindowMode.Fullscreen, kept.WindowMode);
                Assert.AreEqual(new DisplayResolution(1024, 768), kept.Resolution);
                Assert.AreEqual(0.25f, kept.MusicVolume, 1e-6f);
                Assert.AreEqual(0.75f, kept.SfxVolume, 1e-6f);
                Assert.AreEqual(0f, kept.AmbientVolume, 1e-6f);
                Assert.IsFalse(hud.ConfigShown, "Me.Hide()");
                Assert.IsFalse(Session.State.IsPaused);

                // Default: windowed at 640 x 480, only on the controls until Submit.
                hud.OpenConfig();
                Assert.IsNotNull(hud.Config.Q<Button>("config-default"));
                Assert.IsNotNull(hud.Config.Q<Button>("config-submit"));
                form.Default();
                Assert.IsFalse(form.Fullscreen);
                Assert.AreEqual("640 x 480", resolution.value);
                Assert.AreEqual(1, store.Writes);
                hud.CloseConfig();

                void Heard() => heard++;
            }
            finally
            {
                PlayerProfile.Store = previous;
            }
        }

        [Test]
        public void Hud_Clock_CountsTheRoundsSeconds_AtTheTop_AndAResetZeroesIt()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            var scale = hud.Scale;

            // Gui.vb updateClock: Write(timerMin & ":" & timerSec, fontClock, 300, 5, 255, 255, 255, 0).
            var clock = hud.Root.Q<Label>("hud-clock");
            Assert.AreEqual(300f * scale, clock.style.left.value.value, 1e-4f);
            Assert.AreEqual(5f * scale, clock.style.top.value.value, 1e-4f);
            Assert.AreEqual((Color)new Color32(255, 255, 0, 255), clock.style.color.value, "yellow");
            Assert.AreEqual(16f * 96f / 72f * scale, clock.style.fontSize.value.value, 1e-4f, "fontClock, Arial Narrow 16 pt");
            Assert.AreEqual("0:00", hud.ClockText);

            for (var i = 0; i < 13; i++) { _rig.Step(0.1f); }
            hud.Refresh();
            Assert.AreEqual(HudText.Clock(Session.World.RoundTime), hud.ClockText);
            Assert.AreEqual("0:01", hud.ClockText, "1.3 s of play");

            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, true)));
            Assert.IsNull(hud.ClockText, "oneFrameRender skips updateClock while paused");
            Assert.IsTrue(Session.Submit(new Pause(_rig.Id, false)));

            Session.Restart();
            hud.Refresh();
            Assert.AreEqual("0:00", hud.ClockText, "Root.vb's Reset: timerMin = 0, timerSec = 0");
        }

        [Test]
        public void Hud_GameOver_DarkensByTheFade_WithoutThePipelineLight()
        {
            var hud = _rig.Track(new GameObject("Hud")).AddComponent<GameHud>();
            hud.Session = Session;
            _ = hud.Root;
            Session.State.Config.HurtDelay = 0f;
            _rig.SpawnEnemy(new Vector2(-0.5f, 0f));
            for (var i = 0; i < 300 && !Session.State.IsGameOver; i++) { _rig.Step(); }
            Assert.IsTrue(Session.State.IsGameOver);
            _rig.Step();
            hud.Refresh();

            Assert.IsNull(hud.BannerText, "Engine.vb GameOver writes no banner; it is hud.json's option, off (U.CHN-T79)");
            Assert.Greater(hud.GameOverDim, 0f, "the fade has begun");
            Assert.AreEqual(hud.GameOverDim, hud.OverlayDim, 1e-5f, "with no scene light the HUD shows the fade");
        }
    }
}
