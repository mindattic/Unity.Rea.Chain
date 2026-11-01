using System;
using MindAttic.Chain.Core;
using Unity.Shared.Core;
using UnityEngine;

namespace MindAttic.Chain
{
    /// <summary>
    /// The player's own preferences (<see cref="PlayerPreferences"/>: volumes, mutes, reduce motion, resolution, window
    /// mode, tuning overrides), read on first use from the shared MindAttic profile in use
    /// (<c>Profiles.Current.Settings</c>, through <see cref="ProfilePreferencesStore"/>), with the stock volumes from
    /// audio.json for a profile that has none of this game's yet. Changing a value changes it for the run; call
    /// <see cref="Save"/> to keep it in the profile. <see cref="MusicPlayer"/> and <see cref="ChainAudio"/> play at
    /// these volumes. Tests set <see cref="Store"/> to a <see cref="MemoryPreferencesStore"/> so nothing reaches disk.
    /// </summary>
    public static class PlayerProfile
    {
        private static IPreferencesStore s_store;
        private static PlayerPreferences s_preferences;

        /// <summary>Raised after <see cref="NotifyChanged"/> (a volume or mute was changed, or the store was swapped).</summary>
        public static event Action Changed;

        /// <summary>Where the preferences are read from and saved to (the current profile unless set).</summary>
        public static IPreferencesStore Store
        {
            get => s_store ?? (s_store = DefaultStore());
            set
            {
                s_store = value;
                s_preferences = null;
                NotifyChanged();
            }
        }

        /// <summary>The preferences in use, loaded on first use.</summary>
        public static PlayerPreferences Preferences => s_preferences ?? (s_preferences = Load());

        /// <summary>Tells the listeners (the music player) that a preference changed.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Writes <see cref="Preferences"/> to the profile. False (and a warning) if it could not be written.</summary>
        public static bool Save()
        {
            var store = Store;
            bool saved;
            try
            {
                saved = store.Write(Preferences);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.LogWarning($"[Chain] Could not save the player's preferences to {store.Describe}: {e.Message}");
                return false;
            }

            if (!saved)
            {
                Debug.LogWarning($"[Chain] Could not save the player's preferences to {store.Describe}.");
            }

            return saved;
        }

        /// <summary>Drops the loaded preferences (and a store a test set), so the next use reads the profile again.</summary>
        public static void Reset()
        {
            s_store = null;
            s_preferences = null;
        }

        private static PlayerPreferences Load()
        {
            var stock = SettingsStore.Stock.AudioTuning;
            var store = Store;
            PlayerPreferences preferences;
            try
            {
                preferences = PlayerPreferences.Load(store, stock);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                Debug.LogWarning($"[Chain] Could not read the player's preferences from {store.Describe}, playing with the stock ones: {e.Message}");
                preferences = PlayerPreferences.Stock(stock);
            }

            ApplySessionMute(preferences);
            return preferences;
        }

        /// <summary>
        /// U.CHN-T108 (U.CHN-T74's original guarantee): <see cref="Profiles.SessionMuted"/> zeroes
        /// <c>Profile.Settings.Common</c>'s own volumes
        /// (<see cref="ProfilePreferencesStore"/> reads them through it), but <see cref="PlayerPreferences.Load"/>'s
        /// one-time stock-volume substitution for a profile this game has not yet stamped (Core, deliberately
        /// unaware of Unity.Shared's Profiles, UNI-T36) runs after that and always wins (<c>PlayerPreferencesTests
        /// .AProfileFromBeforeThisGame_TakesTheStockVolumes</c>: the volumes take the stock figures whatever the mute
        /// flags say), undoing the zero on exactly a profile's first read. Re-applied here, after Load, so a first
        /// -ever launch under <c>-muted</c>/<c>MINDATTIC_CLI_MUTED=1</c> is silent too -- never written back, since
        /// nothing here calls <see cref="IPreferencesStore.Write"/>.
        /// </summary>
        private static void ApplySessionMute(PlayerPreferences preferences)
        {
            if (!Profiles.SessionMuted)
            {
                return;
            }

            preferences.MusicVolume = 0f;
            preferences.SfxVolume = 0f;
            preferences.MuteMusic = true;
            preferences.MuteSfx = true;
        }

        private static IPreferencesStore DefaultStore()
        {
            // Unity.Shared's bootstrap points Profiles at persistentDataPath before the first scene; this covers
            // anything that asks before it ran.
            if (!Profiles.IsConfigured)
            {
                Profiles.UseDataPath(Application.persistentDataPath);
            }

            return ProfilePreferencesStore.ForCurrentProfile();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Clear()
        {
            Reset();
            Changed = null;
        }
    }
}
