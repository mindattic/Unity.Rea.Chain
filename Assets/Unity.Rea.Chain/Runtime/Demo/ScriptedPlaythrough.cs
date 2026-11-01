using System.Collections;
using MindAttic.Capture.Unity;
using MindAttic.Chain.Core;
using UnityEngine;

namespace MindAttic.Chain
{
    /// <summary>
    /// U.CHN-T62: started by <see cref="DemoBootstrap"/> once Main has loaded under <c>-demo</c> (the same flag
    /// Unity.SpaceGod and Unity.GridGame use). Plays the game's whole story once, through the real
    /// <see cref="GameSession"/> API every other player goes through: launch and the loading picture (played
    /// out before this even exists, DemoBootstrap's own screenshot), fly and swing, grapple and throw, a kill, a
    /// few hits, a heart pickup, a game over fade and its automatic reset, pause, the Tab menu, the scripter
    /// console and the F12 settings screen. With <c>-capture screen</c> or <c>video</c> (parsed by
    /// MindAttic.Capture, not here) each named stop is also a screenshot, the kill and the hits that follow
    /// are cut into one short "highlight" clip (<see cref="CaptureUnity.Engine"/>'s own <c>ClipAsync</c>), and
    /// the whole run is taped if requested; without <c>-capture</c> the run still plays, it is just not
    /// recorded. Quits once it is done, as Unity.SpaceGod's CaptureRunner does.
    /// </summary>
    public sealed class ScriptedPlaythrough : MonoBehaviour
    {
        private const float StepPause = 0.3f;

        // Set by anything that went wrong; the run then quits with exit code 1 so a script can gate on it.
        private static bool _failed;
        private System.Threading.Tasks.Task<MindAttic.Capture.CaptureResult> _clip;

        private IEnumerator Start()
        {
            var session = FindAnyObjectByType<GameSession>();
            var angel = FindAnyObjectByType<AngelController>();
            var chain = FindAnyObjectByType<ChainController>();
            var mace = chain != null ? chain.GetComponent<MaceController>() : null;
            var grapple = chain != null ? chain.GetComponent<GrappleController>() : null;
            var hud = FindAnyObjectByType<GameHud>();
            var scripter = FindAnyObjectByType<ScripterConsole>();
            var heart = FindAnyObjectByType<HeartPickup>();

            if (session == null || angel == null || chain == null || mace == null || grapple == null || hud == null)
            {
                Debug.LogError("[Unity.Rea.Chain] ScriptedPlaythrough: Main is missing a piece it needs; the run stops here.");
                Application.Quit(1);
                yield break;
            }

            var capturing = CaptureUnity.Args != null && CaptureUnity.Args.Any;
            if (capturing)
            {
                var wait = 0f;
                while (!CaptureUnity.Engine.Enabled && wait < 15f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }

                capturing = CaptureUnity.Engine.Enabled;
                if (!capturing)
                {
                    Debug.LogError("[Unity.Rea.Chain] MindAttic.Capture did not start; the run will not be captured.");
                    _failed = true;
                }
            }

            // Let the first few physics steps settle (the flock spawning, the chain hanging from the hand) before
            // the first shot, as MainSceneSmokeTests does.
            for (var i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            yield return Shot(capturing, "02-main-scene");

            var player = angel.Seat;

            // Fly and swing: a few seconds of circling mouse deltas wind the chain up like a flail.
            const int swingSteps = 60;
            for (var i = 0; i < swingSteps; i++)
            {
                var angle = (i / (float)swingSteps) * Mathf.PI * 4f;
                var delta = new Vec2(Mathf.Cos(angle) * 6f, Mathf.Sin(angle) * 6f);
                session.Submit(new SteerAngel(player, delta, Time.fixedDeltaTime));
                yield return new WaitForFixedUpdate();
            }

            yield return Shot(capturing, "03-fly-and-swing");

            // Grapple and throw: an enemy right on the mace, caught while armed, then let go.
            PlaythroughActions.SpawnEnemyAt(session, chain.GetLinkPosition(mace.MaceIndex), 999f);
            grapple.SetButtonHeld(true);
            yield return new WaitForSeconds(StepPause);
            yield return Shot(capturing, "04-grapple-catch");
            grapple.SetButtonHeld(false);
            yield return new WaitForSeconds(StepPause);
            yield return Shot(capturing, "05-grapple-throw");

            // A kill, then a couple of hits: the busiest stretch of the run, so it is also the one short
            // highlight clip (a fire-and-forget ClipAsync -- it exports on its own while the tour moves on).
            if (capturing)
            {
                _clip = CaptureUnity.Engine.ClipAsync(System.TimeSpan.FromSeconds(6f), "highlight");
            }

            // A kill: a one-hit-point enemy put right where the mace already is, mid-swing.
            PlaythroughActions.SpawnEnemyAt(session, chain.GetLinkPosition(mace.MaceIndex), 0.01f);
            yield return new WaitForSeconds(StepPause);
            yield return Shot(capturing, "06-kill");

            // A couple of hits: an enemy put on the angel, twice, a beat apart (checkAngelHit's own cooldown).
            PlaythroughActions.SpawnEnemyAt(session, PlaythroughActions.Body(angel), 999f);
            yield return new WaitForSeconds(StepPause);
            PlaythroughActions.SpawnEnemyAt(session, PlaythroughActions.Body(angel), 999f);
            yield return new WaitForSeconds(0.3f);
            yield return Shot(capturing, "07-take-hits");

            // Take the 999-HP enemies off the angel so they cannot end the round before the heart beat.
            session.World.ClearEnemies();

            // A heart: teleport the angel onto it (PlaythroughActions.HeartTeleportTarget), same spot
            // GameWorld.CheckHearts (the next automatic step) finds them touching.
            if (heart != null)
            {
                angel.Flight?.Teleport(PlaythroughActions.HeartTeleportTarget(heart.Rules));
                yield return new WaitForSeconds(StepPause);
                yield return Shot(capturing, "08-heart-pickup");
            }

            // Game over: keep the angel in contact until HP runs out, then watch the fade and the automatic reset.
            var deadline = Time.unscaledTime + 30f;
            while (!session.State.IsGameOver && Time.unscaledTime < deadline)
            {
                PlaythroughActions.SpawnEnemyAt(session, PlaythroughActions.Body(angel), 999f);
                yield return new WaitForSeconds(StepPause);
            }

            yield return Shot(capturing, "09-game-over-fade");

            deadline = Time.unscaledTime + 20f;
            while (session.State.IsGameOver && Time.unscaledTime < deadline)
            {
                yield return null;
            }

            yield return Shot(capturing, "10-auto-reset");

            // Pause.
            session.Submit(new TogglePause(player));
            yield return null;
            yield return Shot(capturing, "11-pause");
            session.Submit(new TogglePause(player));
            yield return null;

            // The Tab menu.
            hud.ToggleMenu();
            yield return null;
            yield return Shot(capturing, "12-menu");
            hud.ToggleMenu();
            yield return null;

            // The scripter console: open it, type a real command, run it, close it.
            if (scripter != null)
            {
                scripter.Press(ScripterKey.Oemtilde);
                scripter.Press(ScripterKey.C);
                scripter.Press(ScripterKey.C);
                scripter.Press(ScripterKey.X);
                scripter.Press(ScripterKey.Space);
                scripter.Press(ScripterKey.D1);
                yield return null;
                yield return Shot(capturing, "13-scripter-command");
                scripter.Press(ScripterKey.Enter); // runs the line and closes the console
                yield return null;
            }

            // The F12 settings screen.
            hud.OpenConfig();
            yield return null;
            yield return Shot(capturing, "14-settings-screen");
            hud.CloseConfig();
            yield return null;

            if (_clip != null)
            {
                var wait = 0f;
                while (!_clip.IsCompleted && wait < 30f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (!_clip.IsCompleted || !_clip.Result.Succeeded)
                {
                    Debug.LogError("[Unity.Rea.Chain] The highlight clip did not export.");
                    _failed = true;
                }
            }

            if (capturing)
            {
                Debug.Log("[Unity.Rea.Chain] ScriptedPlaythrough captured to " + CaptureUnity.Engine.SessionFolder);
            }

            Application.Quit(_failed ? 1 : 0);
        }

        private static IEnumerator Shot(bool capturing, string name)
        {
            if (!capturing)
            {
                yield break;
            }

            var shot = CaptureUnity.Engine.ScreenshotAsync(name);
            while (!shot.IsCompleted)
            {
                yield return null;
            }

            var result = shot.Result;
            if (result.Succeeded)
            {
                Debug.Log("[Unity.Rea.Chain] Captured " + result.PrimaryFile);
            }
            else
            {
                Debug.LogError("[Unity.Rea.Chain] Screenshot " + name + " failed: " + result.Error);
                _failed = true;
            }
        }
    }
}
