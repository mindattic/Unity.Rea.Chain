using System;
using System.Collections;
using MindAttic.Capture.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MindAttic.Chain
{
    /// <summary>
    /// U.CHN-T62: the same <c>-demo</c> command-line flag Unity.SpaceGod and Unity.GridGame use for a scripted,
    /// capturable run. A normal launch is unaffected: with no <c>-demo</c> on the command line this does
    /// nothing, and the game boots exactly as it does for a player, straight from Build Settings' scenes. With
    /// it, the Splash scene's loading picture (UNI-T58) gets one screenshot once it is up
    /// (<see cref="DemoSplashShot"/>, which holds the splash until it is taken), and Main gets a <see cref="ScriptedPlaythrough"/> once it has loaded.
    /// </summary>
    public static class DemoBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            if (!HasCommandLineFlag("-demo"))
            {
                return;
            }

            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Splash")
            {
                var splash = UnityEngine.Object.FindAnyObjectByType<SplashScreen>();
                if (splash != null && splash.GetComponent<DemoSplashShot>() == null)
                {
                    // Hold Main back until the shot of the picture is taken (U.CHN-T75), so it cannot land after the picture is gone.
                    splash.Hold = true;
                    splash.gameObject.AddComponent<DemoSplashShot>();
                }

                return;
            }

            if (scene.name == "Main")
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                if (UnityEngine.Object.FindAnyObjectByType<ScriptedPlaythrough>() == null)
                {
                    new GameObject("ScriptedPlaythrough").AddComponent<ScriptedPlaythrough>();
                }
            }
        }

        /// <summary>The same check Unity.SpaceGod's Bootstrap uses for <c>-demo</c>.</summary>
        public static bool HasCommandLineFlag(string flag)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One screenshot of Form1.vb Startup's loading picture (UNI-T58), once it is up, then gone with the Splash scene itself.</summary>
    internal sealed class DemoSplashShot : MonoBehaviour
    {
        private SplashScreen _splash;

        private void Release()
        {
            if (_splash != null)
            {
                _splash.Hold = false;
            }
        }

        private void OnDestroy() => Release();

        private IEnumerator Start()
        {
            _splash = GetComponent<SplashScreen>();

            // SplashScreen.Start's own "yield return null" draws the picture; it is held (Hold) until the shot is taken.
            yield return null;

            if (CaptureUnity.Args != null && CaptureUnity.Args.Any)
            {
                var wait = 0f;
                while (!CaptureUnity.Engine.Enabled && wait < 15f)
                {
                    wait += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (CaptureUnity.Engine.Enabled)
                {
                    var shot = CaptureUnity.Engine.ScreenshotAsync("01-loading-picture");
                    while (!shot.IsCompleted)
                    {
                        yield return null;
                    }

                    var result = shot.Result;
                    if (result.Succeeded)
                    {
                        Debug.Log("[Unity.Rea.Chain] Captured " + result.PrimaryFile);
                    }
                }
            }

            Release();
            Destroy(this);
        }
    }
}
