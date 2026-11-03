using System;
using System.Collections.Generic;
using MindAttic.Chain.Core;
using UnityEngine;

namespace MindAttic.Chain
{
    /// <summary>
    /// The one authority: it holds the <see cref="GameWorld"/>, is the only thing that steps it (once
    /// per physics step), and is where every player's <see cref="GameCommand"/>s go
    /// (<see cref="Submit"/>). After each command and step it hands the world's
    /// <see cref="GameEvent"/>s, in order, to whoever listens (<see cref="EventRaised"/>): the enemy
    /// views, the audio, the HUD. Presentation reads the world and plays events; nothing else changes it.
    ///
    /// Players are seated with <see cref="Join"/>: each seat is a <see cref="PlayerId"/> with its own
    /// angel and chain components, which hold that id and send commands with it. There is no
    /// current-player singleton; online, a networked player's NetEntity (Online's <c>ChainPlayer</c>, U.CHN-T26)
    /// carries its seat's commands here the same way, and a client's own session forwards its commands to it
    /// (<see cref="Forward"/>) instead of changing its own world, and its views draw the authority's world as it arrives
    /// (<see cref="Shown"/>, U.CHN-T73). The scene's hearts are put in the world (<see cref="AddHeart"/>), which
    /// moves, checks and resets them; the session freezes time while the world is paused, and ends the
    /// application when a player quits.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)] // the world steps before any view reads it
    public sealed class GameSession : MonoBehaviour
    {
        [Header("Players")]
        [Tooltip("The angel seated when the session starts (its Seat is its player id). Falls back to the scene's AngelController.")]
        [SerializeField] private AngelController angel;
        [Tooltip("The play space: where enemies spawn and the chains bounce. Falls back to the scene's LevelBounds.")]
        [SerializeField] private LevelBounds level;

        // The tuning lives in the data files (GameSettings); this overrides angel.json's port.contactRadius once set.
        private float _contactRadiusOverride = -1f;

        private GameSettings _settings;

        private readonly List<Seat> _seats = new List<Seat>();
        private GameWorld _world;
        private bool _ownsTimeScale;

        private struct Seat
        {
            public PlayerId Id;
            public AngelController Angel;
            public ChainController Chain;
        }

        /// <summary>Every event the world produced, in order, after each command and step.</summary>
        public event Action<GameEvent> EventRaised;

        /// <summary>
        /// Raised after the world resets (its <see cref="WorldReset"/> event): the R key or the end of the
        /// game-over fade. The camera, the music and the light restore their part of Root.vb's Reset here.
        /// </summary>
        public event Action Restarted;

        /// <summary>
        /// Raised when a player quits (the world's <see cref="QuitRequested"/>, Form1.vb's Escape), just before the
        /// session ends the application (<see cref="QuitApplication"/>).
        /// </summary>
        public event Action Quitting;

        /// <summary>The world this session is the authority for. Read it; change it only through <see cref="Submit"/>.</summary>
        public GameWorld World
        {
            get
            {
                if (_world == null)
                {
                    _world = new GameWorld(BuildSettings());
                    _world.Arena = ResolveLevel() != null ? level.Bounds.ToCore() : (Rect2?)null;
                }

                return _world;
            }
        }

        /// <summary>
        /// The settings registry this authority seats its players and runs its world from: the data files
        /// (<see cref="SettingsStore.Current"/>) unless set. Set it before the world is first used.
        /// </summary>
        public GameSettings Settings
        {
            get => _settings ?? (_settings = SettingsStore.Current);
            set => _settings = value;
        }

        /// <summary>
        /// What this screen's scripter holds back (U.CHN-T61): Form1.vb's normal keys while it is open, Input.vb's
        /// getInput while it is open or a script runs. Every input reader asks it before acting; the
        /// <see cref="ScripterConsole"/> sets it. It is the local screen's, not the world's, and changes nothing in the game.
        /// </summary>
        public InputGate InputGate { get; } = new InputGate();

        /// <summary>Whether <paramref name="session"/>'s gate holds Form1.vb's normal keys back (false without a session).</summary>
        public static bool KeysSuspended(GameSession session) => session != null && session.InputGate.KeysSuspended;

        /// <summary>Whether <paramref name="session"/>'s gate holds Input.vb's getInput back (false without a session).</summary>
        public static bool PlaySuspended(GameSession session) => session != null && session.InputGate.PlaySuspended;

        /// <summary>HP, score, pause and game over, for the HUD.</summary>
        public GameState State => World.State;

        public AngelController Angel
        {
            get => angel;
            set => angel = value;
        }

        public LevelBounds Level
        {
            get => level;
            set
            {
                level = value;
                if (_world != null)
                {
                    _world.Arena = value != null ? value.Bounds.ToCore() : (Rect2?)null;
                }
            }
        }

        /// <summary>The angel's contact radius: angel.json's unless overridden here (live tuning, tests).</summary>
        public float ContactRadius
        {
            get => _contactRadiusOverride >= 0f ? _contactRadiusOverride : Settings.Player.ContactRadius;
            set
            {
                _contactRadiusOverride = Mathf.Max(0f, value);
                RetuneAll();
            }
        }

        /// <summary>
        /// Online as a client (U.CHN-T26): where this screen's commands go instead of its own world, the networked player
        /// that carries them to the one authority (the host or the dedicated server). Null, as in local play, applies
        /// them here. While it is set this session's own world is not stepped: the authority's is the game.
        /// <see cref="QuitGame"/> is never forwarded.
        /// </summary>
        public Func<GameCommand, bool> Forward { get; set; }

        /// <summary>Whether this screen's commands go to a remote authority (<see cref="Forward"/> is set).</summary>
        public bool IsRemote => Forward != null;

        private WorldSnapshot _shown;

        /// <summary>
        /// Online as a client (U.CHN-T73): the remote authority's world as it last arrived (Online's <c>ChainWorldSync</c>),
        /// which this screen's views draw in place of its own frozen world: the angel, chain and mace show
        /// <see cref="ShownSeat"/>'s entry, the enemies and hearts the authority's, and the HUD its HP, score, clock,
        /// pause and game over. Null in local play, on the authority, and before the first snapshot. Only ever read:
        /// nothing changes a world with it. It is dropped as soon as the screen stops forwarding (<see cref="IsRemote"/>).
        /// </summary>
        public WorldSnapshot Shown => IsRemote ? _shown : null;

        /// <summary>The seat this screen plays as in <see cref="Shown"/>: the one its own angel, chain and mace draw.</summary>
        public PlayerId ShownSeat { get; private set; }

        /// <summary>
        /// Draws <paramref name="snapshot"/> (the remote authority's newest world) on this screen, with this screen's own
        /// angel showing <paramref name="seat"/>. Called every frame by the client's networked player; changes no world.
        /// </summary>
        public void ShowAuthority(WorldSnapshot snapshot, PlayerId seat)
        {
            _shown = snapshot;
            ShownSeat = seat;
        }

        /// <summary>Back to drawing this screen's own world (the client left, or its player went away).</summary>
        public void StopShowingAuthority() => _shown = null;

        /// <summary>This screen's own seat's angel, chain and mace as the remote authority has them; false when not shown.</summary>
        public bool TryGetShownPlayer(out WorldSnapshot.Player player)
        {
            var shown = Shown;
            if (shown != null)
            {
                return shown.TryGetPlayer(ShownSeat, out player);
            }

            player = default;
            return false;
        }

        /// <summary>Whether the game this screen shows is paused: the remote authority's (<see cref="Shown"/>), else this world's.</summary>
        public bool ShowsPaused => Shown?.IsPaused ?? State.IsPaused;

        /// <summary>Whether the game this screen shows is over: the remote authority's, else this world's.</summary>
        public bool ShowsGameOver => Shown?.IsGameOver ?? State.IsGameOver;

        /// <summary>The shown game's ambient light, 0-1 (the game-over fade): the remote authority's, else this world's.</summary>
        public float ShownAmbientLevel => Shown != null ? Shown.Ambient / (float)GameState.FullAmbient : State.AmbientLevel;

        /// <summary>
        /// While an online session runs (U.CHN-T26), Escape opens the Unity.Multiplayer kit's in-game menu, whose Leave or
        /// End game ends the session, so Form1.vb's Escape (<see cref="QuitGame"/>) does not end the application.
        /// False in local play, where Escape quits exactly as before.
        /// </summary>
        public bool HoldsQuit { get; set; }

        /// <summary>
        /// Sends a player's command to the world and plays what it produced. Returns false if it changed nothing. Online as
        /// a client (<see cref="Forward"/>) it goes to the authority instead, and true means it was sent.
        /// </summary>
        public bool Submit(GameCommand command)
        {
            if (command is QuitGame && HoldsQuit)
            {
                return false;
            }

            var forward = Forward;
            if (forward != null && !(command is QuitGame))
            {
                return forward(command);
            }

            var applied = World.Submit(command);
            Flush();
            return applied;
        }

        /// <summary>One authority step of the world (hearts included). Called from <c>FixedUpdate</c>; public so tests can drive it.</summary>
        public void Step(float deltaTime)
        {
            World.Step(deltaTime);
            Flush();
        }

        /// <summary>
        /// The authority seats an online player who has no angel on this screen (U.CHN-T26): the world gets a player
        /// <paramref name="id"/> tuned exactly as the first seated angel is (the data files' figures when there is none).
        /// Returns the world's player; one already seated as <paramref name="id"/> is returned as it is.
        /// </summary>
        public PlayerEntity AddRemotePlayer(PlayerId id)
        {
            var existing = World.FindPlayer(id);
            if (existing != null)
            {
                return existing;
            }

            PlayerSetup setup;
            if (_seats.Count > 0 && _seats[0].Angel != null)
            {
                setup = BuildSetup(_seats[0].Angel, _seats[0].Chain);
            }
            else
            {
                setup = Settings.Player;
                setup.ContactRadius = ContactRadius;
            }

            var player = World.AddPlayer(id, setup);
            Flush();
            return player;
        }

        /// <summary>
        /// Takes an online player added by <see cref="AddRemotePlayer"/> out of the world (they left). A seat with an
        /// angel on this screen is never removed this way. Returns false if there was no such remote player.
        /// </summary>
        public bool RemoveRemotePlayer(PlayerId id)
        {
            foreach (var seat in _seats)
            {
                if (seat.Id == id)
                {
                    return false;
                }
            }

            var removed = _world != null && _world.RemovePlayer(id);
            Flush();
            return removed;
        }

        /// <summary>
        /// Puts a heart in the world, shown by <paramref name="view"/> from then on (a scene heart does this
        /// itself when play starts). Returns the world's heart.
        /// </summary>
        public HeartFloat AddHeart(HeartPickup view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            var heart = World.AddHeart();
            view.Bind(this, heart);
            return heart;
        }

        /// <summary>
        /// Seats <paramref name="angel"/> and <paramref name="chain"/> (and the mace, grapple and audio
        /// components beside the chain) as player <paramref name="id"/>: the world gets a player tuned
        /// from them, and each of them is bound to this session and id.
        /// </summary>
        public PlayerEntity Join(PlayerId id, AngelController angel, ChainController chain)
        {
            if (angel == null)
            {
                throw new ArgumentNullException(nameof(angel));
            }

            var player = World.AddPlayer(id, BuildSetup(angel, chain));
            _seats.Add(new Seat { Id = id, Angel = angel, Chain = chain });
            angel.Bind(this, id);
            if (chain != null)
            {
                chain.Bind(this, id);
            }

            return player;
        }

        /// <summary>Seats <paramref name="angel"/> at its own seat with its own chain.</summary>
        public PlayerEntity Join(AngelController angel) => Join(angel.Seat, angel, angel.Chain);

        /// <summary>Re-reads a seated player's tuning from their components (live inspector tweaks).</summary>
        public void Retune(PlayerId id)
        {
            foreach (var seat in _seats)
            {
                if (seat.Id == id && seat.Angel != null)
                {
                    World.Configure(id, BuildSetup(seat.Angel, seat.Chain));
                }
            }
        }

        /// <summary>Starts over in place (Root.vb Reset): the world resets and the views follow its events.</summary>
        public void Restart()
        {
            World.Reset();
            Flush();
        }

        /// <summary>
        /// Item.vb's checkItemCollect now, outside a step (every step runs it): a heart an angel touches is
        /// consumed (gone until a reset) and heals that player +10 HP, capped at max HP. Returns the HP gained.
        /// </summary>
        public int CheckPickups()
        {
            var gained = World.CheckHearts();
            Flush();
            return gained;
        }

        /// <summary>A player's setup: the data files' (<see cref="GameSettings.Player"/>), then what the player's own components override.</summary>
        private PlayerSetup BuildSetup(AngelController angel, ChainController chain)
        {
            var setup = Settings.Player;
            setup.ContactRadius = ContactRadius;
            angel.Contribute(ref setup, Settings);
            if (chain != null)
            {
                chain.Contribute(ref setup);
            }

            return setup;
        }

        private GameWorld.Settings BuildSettings() => Settings.World;

        private LevelBounds ResolveLevel()
        {
            if (level == null)
            {
                level = LevelBounds.Find();
            }

            return level;
        }

        private void RetuneAll()
        {
            if (_world == null)
            {
                return;
            }

            foreach (var seat in _seats)
            {
                Retune(seat.Id);
            }
        }

        /// <summary>Hands the world's events to the listeners, and freezes or frees time on a pause change.</summary>
        private void Flush()
        {
            if (_world == null || _world.PendingEvents.Count == 0)
            {
                return;
            }

            foreach (var gameEvent in _world.TakeEvents())
            {
                // A reset unpauses without a PauseChanged of its own (R while paused, or with the config screen open), so
                // it must give the time scale back too, or the unpaused world stays frozen at timeScale 0.
                if (gameEvent is PauseChanged || gameEvent is WorldReset)
                {
                    ApplyTimeScale();
                }

                EventRaised?.Invoke(gameEvent);
                if (gameEvent is WorldReset)
                {
                    Restarted?.Invoke();
                }

                if (gameEvent is QuitRequested)
                {
                    Quitting?.Invoke();
                    QuitApplication();
                }
            }
        }

        /// <summary>
        /// Ends the game as Form1.vb's Escape does (<c>bRunning = False</c>, the form closes): the player quits, and
        /// in the Editor play mode stops. Outside play mode (edit-mode tests) it does nothing.
        /// </summary>
        private static void QuitApplication()
        {
            if (!Application.isPlaying)
            {
                return;
            }

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void ApplyTimeScale()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (State.IsPaused)
            {
                Time.timeScale = 0f;
                _ownsTimeScale = true;
            }
            else
            {
                ReleaseTimeScale();
            }
        }

        private void ReleaseTimeScale()
        {
            if (_ownsTimeScale)
            {
                Time.timeScale = 1f;
                _ownsTimeScale = false;
            }
        }

        private void Awake()
        {
            if (angel == null)
            {
                angel = FindAnyObjectByType<AngelController>();
            }

            ResolveLevel();
            if (angel != null && World.FindPlayer(angel.Seat) == null)
            {
                Join(angel);
            }
        }

        private void Start() => Restart();

        private void FixedUpdate()
        {
            // Online as a client the authority's world is the game; this screen's own is left as it was.
            if (!IsRemote)
            {
                Step(Time.fixedDeltaTime);
            }
        }

        private void OnDisable() => ReleaseTimeScale();

        private void OnDrawGizmosSelected()
        {
            if (angel == null)
            {
                return;
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(angel.transform.position, ContactRadius);
        }
    }
}
