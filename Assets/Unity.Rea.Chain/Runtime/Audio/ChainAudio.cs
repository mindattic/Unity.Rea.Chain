using System;
using MindAttic.Chain.Core;
using UnityEngine;

namespace MindAttic.Chain
{
    /// <summary>The gameplay sounds <see cref="ChainAudio"/> plays.</summary>
    public enum ChainSound
    {
        /// <summary>The mace whipping past the swing speed (Sound.vb's sword_swing_1..7).</summary>
        Swing,

        /// <summary>The mace hitting an enemy (sword_hit_1..3).</summary>
        Impact,

        /// <summary>The armed mace catching a grapple target (defend.wav in the original set).</summary>
        GrappleCatch,

        /// <summary>Releasing the grapple and throwing what it held.</summary>
        GrappleThrow,

        /// <summary>An enemy killed by the mace (death_1..4).</summary>
        EnemyDeath,
    }

    /// <summary>
    /// Sound effects for the mace (Sound.vb's DirectSound buffers, ported to an
    /// <see cref="AudioSource"/>): a swing whoosh when the mace link's speed relative to the hand crosses
    /// <see cref="SwingSpeed"/> (<see cref="SwingSoundTrigger"/>), an impact for every enemy
    /// the mace hits (louder for faster hits) plus a death sound for a
    /// lethal one, and a catch/throw sound for the grapple. Hits, catches and throws are the world's
    /// events for this chain's player (<see cref="EnemyHit"/>, <see cref="Grabbed"/>,
    /// <see cref="Thrown"/>), played as they come from the session. Each sound has a
    /// set of clips; a random one plays each time, never the same twice in a row. Leave a set
    /// empty to mute that sound. Put this on the chain object next to the other mace components.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(120)] // after the authority's step, so this step's mace velocity is final
    public sealed class ChainAudio : MonoBehaviour
    {
        [SerializeField] private ChainController chain;
        [Tooltip("Plays the one-shots. Defaults to an AudioSource on this object (added if missing).")]
        [SerializeField] private AudioSource sfxSource;

        [Header("Swing")]
        [SerializeField] private AudioClip[] swingClips = Array.Empty<AudioClip>();

        [Header("Impact")]
        [SerializeField] private AudioClip[] impactClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] deathClips = Array.Empty<AudioClip>();

        [Header("Grapple")]
        [SerializeField] private AudioClip[] catchClips = Array.Empty<AudioClip>();
        [SerializeField] private AudioClip[] throwClips = Array.Empty<AudioClip>();

        // audio.json's swing figures, read on first use; the properties override them at run time. maceIndex is used
        // only when the chain has no MaceController. The volume and mute are the player profile's (PlayerProfile).
        private bool _tuned;
        private int maceIndex;
        private float swingSpeed;
        private float swingRearmSpeed;
        private float swingCooldown;
        private float swingRepeatInterval;
        private float fullVolumeSpeed;
        private float minSpeedVolume;
        private bool throwSound;

        private readonly ClipPicker[] _pickers = new ClipPicker[Enum.GetValues(typeof(ChainSound)).Length];
        private SwingSoundTrigger _swing;
        private Vector2 _previousAnchor;
        private bool _hasAnchor;
        private ChainSimulation _measured;
        private GameSession _subscribed;

        /// <summary>Raised for every sound played (also when its clip set is empty or muted), with the chosen clip (may be null) and volume.</summary>
        public event Action<ChainSound, AudioClip, float> Played;

        public ChainController Chain
        {
            get => chain;
            set
            {
                chain = value;
                Swing.Reset();
                _hasAnchor = false;
                if (isActiveAndEnabled)
                {
                    Subscribe();
                }
            }
        }

        public AudioSource SfxSource
        {
            get => sfxSource;
            set => sfxSource = value;
        }

        /// <summary>The profile's sound-effect volume (0-1, stock Sound.vb's -10 dB); setting it changes the profile's for the run (<see cref="PlayerProfile.Save"/> keeps it).</summary>
        public float Volume
        {
            get => PlayerProfile.Preferences.SfxVolume;
            set
            {
                PlayerProfile.Preferences.SfxVolume = value;
                PlayerProfile.NotifyChanged();
            }
        }

        /// <summary>The profile's sound-effect mute.</summary>
        public bool Muted
        {
            get => PlayerProfile.Preferences.MuteSfx;
            set
            {
                PlayerProfile.Preferences.MuteSfx = value;
                PlayerProfile.NotifyChanged();
            }
        }

        public float SwingSpeed
        {
            get => Tuned().swingSpeed;
            set => Tuned().swingSpeed = Mathf.Max(0f, value);
        }

        public float SwingRearmSpeed
        {
            get => Tuned().swingRearmSpeed;
            set => Tuned().swingRearmSpeed = Mathf.Max(0f, value);
        }

        public float SwingRepeatInterval
        {
            get => Tuned().swingRepeatInterval;
            set => Tuned().swingRepeatInterval = Mathf.Max(0f, value);
        }

        public float FullVolumeSpeed
        {
            get => Tuned().fullVolumeSpeed;
            set => Tuned().fullVolumeSpeed = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Whether a grapple release plays <see cref="ChainSound.GrappleThrow"/>: audio.json's <c>throwSound</c>, off by
        /// default since the original has no throw sound (U.CHN-T80, BIBLE V10).
        /// </summary>
        public bool ThrowSound
        {
            get => Tuned().throwSound;
            set => Tuned().throwSound = value;
        }

        /// <summary>The mace link: the chain's <see cref="MaceController"/> index if it has one, else this component's.</summary>
        public int MaceIndex
        {
            get
            {
                Tuned();
                if (chain == null)
                {
                    return maceIndex;
                }

                var mace = chain.GetComponent<MaceController>();
                return mace != null ? mace.MaceIndex : Mathf.Clamp(maceIndex, 1, Mathf.Max(1, chain.LinkCount - 1));
            }
            set => Tuned().maceIndex = Mathf.Max(1, value);
        }

        /// <summary>The mace's speed relative to the hand measured by the last <see cref="Step"/>, world units per second.</summary>
        public float LastSwingSpeed { get; private set; }

        public SwingSoundTrigger Swing => _swing ?? (_swing = NewSwing());

        private SwingSoundTrigger NewSwing()
        {
            Tuned();
            return new SwingSoundTrigger(swingSpeed, swingRearmSpeed, swingCooldown, swingRepeatInterval);
        }

        /// <summary>Reads audio.json's swing figures the first time they are needed.</summary>
        private ChainAudio Tuned()
        {
            if (!_tuned)
            {
                var settings = SettingsStore.Current;
                var audio = settings.AudioTuning;
                maceIndex = settings.Player.MaceIndex;
                swingSpeed = audio.Swing.SwingSpeed;
                swingRearmSpeed = audio.Swing.SwingRearmSpeed;
                swingCooldown = audio.Swing.SwingCooldown;
                swingRepeatInterval = audio.Swing.SwingRepeatInterval;
                fullVolumeSpeed = audio.Swing.FullVolumeSpeed;
                minSpeedVolume = audio.Swing.MinSpeedVolume;
                throwSound = audio.Swing.ThrowSound;
                _tuned = true;
            }

            return this;
        }

        /// <summary>The clip set for <paramref name="sound"/>; assign to replace it.</summary>
        public AudioClip[] GetClips(ChainSound sound)
        {
            switch (sound)
            {
                case ChainSound.Swing: return swingClips;
                case ChainSound.Impact: return impactClips;
                case ChainSound.GrappleCatch: return catchClips;
                case ChainSound.GrappleThrow: return throwClips;
                case ChainSound.EnemyDeath: return deathClips;
                default: throw new ArgumentOutOfRangeException(nameof(sound), sound, null);
            }
        }

        public void SetClips(ChainSound sound, AudioClip[] clips)
        {
            clips = clips ?? Array.Empty<AudioClip>();
            switch (sound)
            {
                case ChainSound.Swing: swingClips = clips; break;
                case ChainSound.Impact: impactClips = clips; break;
                case ChainSound.GrappleCatch: catchClips = clips; break;
                case ChainSound.GrappleThrow: throwClips = clips; break;
                case ChainSound.EnemyDeath: deathClips = clips; break;
                default: throw new ArgumentOutOfRangeException(nameof(sound), sound, null);
            }
        }

        /// <summary>
        /// Plays a random clip of <paramref name="sound"/> at <paramref name="relativeVolume"/> x
        /// <see cref="Volume"/>. Returns the clip chosen (null if the set is empty).
        /// </summary>
        public AudioClip Play(ChainSound sound, float relativeVolume = 1f)
        {
            var clips = GetClips(sound);
            var picker = _pickers[(int)sound] ?? (_pickers[(int)sound] = new ClipPicker());
            var index = picker.Next(clips != null ? clips.Length : 0);
            var clip = index >= 0 ? clips[index] : null;
            var finalVolume = Mathf.Clamp01(relativeVolume) * PlayerProfile.Preferences.SfxGain;

            if (clip != null && finalVolume > 0f && sfxSource != null && Application.isPlaying)
            {
                sfxSource.PlayOneShot(clip, finalVolume);
            }

            Played?.Invoke(sound, clip, finalVolume);
            return clip;
        }

        /// <summary>
        /// One audio step: plays a swing when the mace link's speed crosses the swing speed.
        /// Called from <c>FixedUpdate</c>; public so tests can drive it. Returns true if a swing played.
        /// </summary>
        public bool Step(float deltaTime)
        {
            if (chain == null || chain.Simulation == null)
            {
                return false;
            }

            var swing = Swing;
            Tuned();
            swing.SwingSpeed = swingSpeed;
            swing.RearmSpeed = swingRearmSpeed;
            swing.RepeatInterval = swingRepeatInterval;

            var speed = MeasureSwingSpeed(deltaTime);
            if (!swing.Step(speed, deltaTime))
            {
                return false;
            }

            Play(ChainSound.Swing, SpeedVolume(speed));
            return true;
        }

        /// <summary>
        /// The mace link's speed relative to the hand (link 0): flying steadily with a trailing
        /// mace is not a swing, whipping the mace around the hand is.
        /// </summary>
        private float MeasureSwingSpeed(float deltaTime)
        {
            // A new chain (a reset puts the angel home, or a retune rebuilds it): the hand jumped, it did not swing, so
            // there is no previous anchor to measure from (else every reset played a whoosh off the teleport).
            var simulation = chain.Simulation;
            if (simulation != _measured)
            {
                _measured = simulation;
                _hasAnchor = false;
            }

            var anchor = chain.GetLinkPosition(0);
            var anchorVelocity = _hasAnchor && deltaTime > 0f ? (anchor - _previousAnchor) / deltaTime : Vector2.zero;
            _previousAnchor = anchor;
            _hasAnchor = true;

            return LastSwingSpeed = (chain.GetLinkVelocity(MaceIndex) - anchorVelocity).magnitude;
        }

        private float SpeedVolume(float speed) =>
            SwingSoundTrigger.VolumeForSpeed(speed, Tuned().swingSpeed, fullVolumeSpeed, minSpeedVolume);

        /// <summary>Called when the chain's player is seated: listen to that player's events.</summary>
        public void Bind(ChainController boundChain)
        {
            chain = boundChain;
            Subscribe();
        }

        /// <summary>Plays the sound for one world event of this chain's player. Hooked to <see cref="GameSession.EventRaised"/>; public so tests can drive it.</summary>
        public void Play(GameEvent gameEvent)
        {
            if (chain == null)
            {
                return;
            }

            var player = chain.Player;
            switch (gameEvent)
            {
                case EnemyHit hit when hit.Player == player:
                    Play(ChainSound.Impact, SpeedVolume(hit.MaceVelocity.Magnitude));
                    if (hit.Killed)
                    {
                        Play(ChainSound.EnemyDeath);
                    }

                    break;

                case Grabbed grabbed when grabbed.Player == player:
                    Play(ChainSound.GrappleCatch);
                    break;

                case Thrown thrown when thrown.Player == player && ThrowSound:
                    Play(ChainSound.GrappleThrow, SpeedVolume(thrown.Velocity.Magnitude));
                    break;
            }
        }

        private void Subscribe()
        {
            var session = chain != null ? chain.Session : null;
            if (_subscribed == session)
            {
                return;
            }

            Unsubscribe();
            if (session != null)
            {
                session.EventRaised += Play;
                _subscribed = session;
            }
        }

        private void Unsubscribe()
        {
            if (_subscribed != null)
            {
                _subscribed.EventRaised -= Play;
                _subscribed = null;
            }
        }

        private void Reset()
        {
            chain = GetComponent<ChainController>();
            sfxSource = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (chain == null)
            {
                chain = GetComponent<ChainController>();
            }

            if (sfxSource == null)
            {
                sfxSource = GetComponent<AudioSource>();
                if (sfxSource == null)
                {
                    sfxSource = gameObject.AddComponent<AudioSource>();
                    sfxSource.playOnAwake = false;
                    sfxSource.spatialBlend = 0f;
                }
            }
        }

        private void OnEnable() => Subscribe();

        private void OnDisable()
        {
            Unsubscribe();
            Swing.Reset();
            _hasAnchor = false;
        }

        private void FixedUpdate() => Step(Time.fixedDeltaTime);
    }
}
