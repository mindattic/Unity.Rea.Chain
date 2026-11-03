using System;
using System.Collections.Generic;

namespace MindAttic.Chain.Core
{
    /// <summary>How the game's window is shown (a player build's Screen.fullScreenMode).</summary>
    public enum WindowMode
    {
        /// <summary>A window with a frame, the size of <see cref="PlayerPreferences.Resolution"/>.</summary>
        Windowed,

        /// <summary>A borderless window covering the screen (Unity's FullScreenWindow).</summary>
        Borderless,

        /// <summary>Exclusive full screen at <see cref="PlayerPreferences.Resolution"/>.</summary>
        Fullscreen,

        /// <summary>A maximised framed window (Windows' MaximizedWindow).</summary>
        Maximized,
    }

    /// <summary>A screen size in pixels.</summary>
    public readonly struct DisplayResolution : IEquatable<DisplayResolution>
    {
        /// <summary>The smallest size kept (anything smaller is a typo, not a window).</summary>
        public const int Min = 320;

        /// <summary>The largest size kept.</summary>
        public const int Max = 16384;

        public DisplayResolution(int width, int height)
        {
            Width = Math.Max(Min, Math.Min(Max, width));
            Height = Math.Max(Min, Math.Min(Max, height));
        }

        public int Width { get; }
        public int Height { get; }

        public bool Equals(DisplayResolution other) => Width == other.Width && Height == other.Height;
        public override bool Equals(object obj) => obj is DisplayResolution other && Equals(other);
        public override int GetHashCode() => Width * 397 ^ Height;
        public override string ToString() => $"{Width}x{Height}";
    }

    /// <summary>
    /// What one player chose for themselves, as opposed to the game's tuning (<see cref="GameSettings"/>): the shared
    /// MindAttic profile's common block (music and sound-effect volume and mute, reduce motion) and Chain's own
    /// block of it (resolution, window mode and tuning overrides over the stock data files). Read and written
    /// through an <see cref="IPreferencesStore"/>; in Unity that is the player's profile settings.json
    /// (<c>Profiles.Current.Settings</c>, the Unity.Shared package).
    ///
    /// The stock volumes are the original's (audio.json: Sound.vb's full music and -10 dB sound effects), not the
    /// shared package's: a profile this game has not yet given them to takes them on <see cref="Load"/>, and from
    /// then on the profile's own figures are used.
    /// </summary>
    public sealed class PlayerPreferences
    {
        /// <summary>Field names in the profile's <c>"game"</c> block, as written in settings.json.</summary>
        public static class GameKeys
        {
            /// <summary>True once the profile's common volumes were set to Chain's stock ones.</summary>
            public const string StockVolumes = "stockVolumes";

            /// <summary><c>{"width": 1920, "height": 1080}</c>; missing keeps the screen's own.</summary>
            public const string Resolution = "resolution";

            /// <summary><c>"windowed"</c>, <c>"borderless"</c>, <c>"fullscreen"</c> or <c>"maximized"</c>; missing keeps the build's own.</summary>
            public const string WindowMode = "windowMode";

            /// <summary><c>{"chain.json": {"springK": 0.3, "port": {"hitRadius": 0.4}}}</c>: keys laid over the stock data files.</summary>
            public const string Tuning = "tuning";

            /// <summary>frmConfig.vb's Ambient Volume slider, a 0-1 gain (the shared common block has only music and sound effects); missing takes audio.json's.</summary>
            public const string AmbientVolume = "ambientVolume";

            public const string Width = "width";
            public const string Height = "height";
        }

        private float _musicVolume = 1f;
        private float _sfxVolume = 1f;
        private float? _ambientVolume;

        /// <summary>
        /// Ambient volume, a 0-1 gain (Sound.vb's ambientVolume, frmConfig.vb's Ambient Volume slider), kept in Chain's
        /// own block of the profile. Nothing plays on it yet, as nothing did in the original.
        /// </summary>
        public float AmbientVolume
        {
            get => _ambientVolume ?? 1f;
            set => _ambientVolume = Clamp01(value);
        }

        /// <summary>Whether <see cref="AmbientVolume"/> was set (read from the profile, or given the stock figure).</summary>
        public bool HasAmbientVolume => _ambientVolume.HasValue;

        /// <summary>Music volume, a 0-1 gain.</summary>
        public float MusicVolume
        {
            get => _musicVolume;
            set => _musicVolume = Clamp01(value);
        }

        /// <summary>Sound-effect volume, a 0-1 gain.</summary>
        public float SfxVolume
        {
            get => _sfxVolume;
            set => _sfxVolume = Clamp01(value);
        }

        public bool MuteMusic { get; set; }
        public bool MuteSfx { get; set; }

        /// <summary>The player asked for less motion.</summary>
        public bool ReduceMotion { get; set; }

        /// <summary>The window size the player chose, or null to keep the screen's own.</summary>
        public DisplayResolution? Resolution { get; set; }

        /// <summary>The window mode the player chose, or null to keep the build's own.</summary>
        public WindowMode? WindowMode { get; set; }

        /// <summary>Data file name ("chain.json") to the JSON object of keys the profile changes in it (see <see cref="TuningOverrideSource"/>).</summary>
        public Dictionary<string, string> TuningOverrides { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Whether the volumes are this game's (see <see cref="Load"/>), rather than whatever the profile held before.</summary>
        public bool StockVolumesApplied { get; set; }

        /// <summary>The music's gain as played: 0 when muted.</summary>
        public float MusicGain => MuteMusic ? 0f : MusicVolume;

        /// <summary>The sound effects' gain as played: 0 when muted.</summary>
        public float SfxGain => MuteSfx ? 0f : SfxVolume;

        /// <summary>Fresh preferences with the stock volumes (<paramref name="stock"/>: audio.json's, as gains).</summary>
        public static PlayerPreferences Stock(AudioTuning stock) => new PlayerPreferences
        {
            MusicVolume = stock.MusicVolume,
            SfxVolume = stock.SfxVolume,
            AmbientVolume = stock.AmbientVolume,
            StockVolumesApplied = true,
        };

        /// <summary>
        /// Reads the player's preferences from <paramref name="store"/>. A profile that has never had this game's stock
        /// volumes (<paramref name="stock"/>, audio.json's) takes them now, in memory; they are written with the next
        /// <see cref="IPreferencesStore.Write"/>, so a profile nobody changes keeps following the stock figures.
        /// </summary>
        public static PlayerPreferences Load(IPreferencesStore store, AudioTuning stock)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            var preferences = store.Read() ?? new PlayerPreferences();
            if (!preferences.StockVolumesApplied)
            {
                // A channel the store reports muted and at zero is a session mute (the store silences it, e.g.
                // Profiles.SessionMuted): keep it silent, and leave the profile unstamped so a later normal launch
                // still gets the stock volumes.
                var sessionSilenced = (preferences.MuteMusic && preferences.MusicVolume <= 0f)
                    || (preferences.MuteSfx && preferences.SfxVolume <= 0f);
                if (!sessionSilenced)
                {
                    preferences.MusicVolume = stock.MusicVolume;
                    preferences.SfxVolume = stock.SfxVolume;
                    preferences.StockVolumesApplied = true;
                }
            }

            if (!preferences.HasAmbientVolume)
            {
                preferences.AmbientVolume = stock.AmbientVolume;
            }

            return preferences;
        }

        /// <summary>The window mode named <paramref name="text"/> in settings.json, or null for one it does not know.</summary>
        public static WindowMode? ParseWindowMode(string text)
        {
            switch (text?.Trim().ToLowerInvariant())
            {
                case "windowed": return Core.WindowMode.Windowed;
                case "borderless": return Core.WindowMode.Borderless;
                case "fullscreen": return Core.WindowMode.Fullscreen;
                case "maximized": return Core.WindowMode.Maximized;
                default: return null;
            }
        }

        /// <summary>The name settings.json uses for <paramref name="mode"/>.</summary>
        public static string FormatWindowMode(WindowMode mode) => mode.ToString().ToLowerInvariant();

        /// <summary>A copy sharing nothing with this one.</summary>
        public PlayerPreferences Clone()
        {
            var copy = new PlayerPreferences
            {
                MusicVolume = MusicVolume,
                SfxVolume = SfxVolume,
                MuteMusic = MuteMusic,
                MuteSfx = MuteSfx,
                ReduceMotion = ReduceMotion,
                Resolution = Resolution,
                WindowMode = WindowMode,
                StockVolumesApplied = StockVolumesApplied,
                _ambientVolume = _ambientVolume,
            };
            foreach (var pair in TuningOverrides)
            {
                copy.TuningOverrides[pair.Key] = pair.Value;
            }

            return copy;
        }

        private static float Clamp01(float v) => float.IsNaN(v) ? 0f : Math.Max(0f, Math.Min(1f, v));
    }

    /// <summary>
    /// Where a player's <see cref="PlayerPreferences"/> are kept. Core never knows it is a profile's settings.json;
    /// the Unity side's store reads and writes <c>Profiles.Current.Settings</c>, tests use <see cref="MemoryPreferencesStore"/>.
    /// </summary>
    public interface IPreferencesStore
    {
        /// <summary>A description for log messages (a profile's settings path, "memory").</summary>
        string Describe { get; }

        /// <summary>The preferences as stored (never null; a store with nothing yet gives defaults).</summary>
        PlayerPreferences Read();

        /// <summary>Stores <paramref name="preferences"/>, keeping anything else the store holds. False if it could not be written.</summary>
        bool Write(PlayerPreferences preferences);
    }

    /// <summary>Preferences held in memory (tests, and a fallback when the profile cannot be read).</summary>
    public sealed class MemoryPreferencesStore : IPreferencesStore
    {
        private PlayerPreferences _stored;

        public MemoryPreferencesStore(PlayerPreferences stored = null)
        {
            _stored = stored?.Clone();
        }

        public string Describe => "memory";

        /// <summary>How many times <see cref="Write"/> was called.</summary>
        public int Writes { get; private set; }

        public PlayerPreferences Read() => _stored?.Clone() ?? new PlayerPreferences();

        public bool Write(PlayerPreferences preferences)
        {
            _stored = (preferences ?? throw new ArgumentNullException(nameof(preferences))).Clone();
            Writes++;
            return true;
        }
    }
}
