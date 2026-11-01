using System;
using System.Collections.Generic;

namespace MindAttic.Chain.Core
{
    /// <summary>
    /// The whole game's state and rules in one place, held by one authority. Players change it only by
    /// <see cref="Submit"/>ting <see cref="GameCommand"/>s (each carrying the sender's
    /// <see cref="PlayerId"/>); the authority advances it with <see cref="Step"/>, once per fixed step.
    /// Everything that happens comes out as <see cref="GameEvent"/>s, in order
    /// (<see cref="TakeEvents"/>), for presentation to play. Nothing else mutates it: presentation reads
    /// <see cref="State"/>, <see cref="Players"/> and <see cref="Enemies"/>.
    ///
    /// One <see cref="Step"/> runs the original's frame (Engine.vb oneFrameRender) in the order the port's
    /// components ran it: each chain swings from its angel's hand (Chain.vb), each enemy seeks and flocks
    /// (Enemy.vb, checkFlocking), each grapple arms, catches, carries or throws, each mace hits what its
    /// swept circle touched (checkMaceImpact), a thrown enemy then knocks the enemies it touches
    /// (checkThrowImpact, after the mace so a hit lands before a knock could take its target out of
    /// <see cref="EnemyEntity.CanBeHit"/> first, U.CHN-T106), enemies touching an angel cost HP
    /// (checkAngelHit), each heart falls and sways and heals the angel that touches it (Item.vb renderItem,
    /// checkItemCollect), and a finished game fades out and resets itself (GameOver, Root.vb Reset). While
    /// paused nothing moves.
    ///
    /// <see cref="Reset"/> is the one place a round starts over, whether a player sent
    /// <see cref="ResetGame"/> (the R key) or the game-over fade finished: everything Root.vb's Reset
    /// restores that the world holds is restored there, and the presentation that holds the rest (the
    /// camera, the music, the light) follows its <see cref="WorldReset"/> event.
    /// </summary>
    public sealed class GameWorld
    {
        /// <summary>The enemies a reset spawns (Enemy.vb loadEnemy).</summary>
        /// <summary>
        /// The original's spawn draw: X and Y each from <see cref="Min"/>..<see cref="Max"/> (getRndNum(-1, 1)),
        /// a value inside <see cref="GapMin"/>..<see cref="GapMax"/> redrawn, so each lands in Min..GapMin or
        /// GapMax..Max. World units; <see cref="XOffset"/> moves X from the quad's left edge to its centre.
        /// Unset (Max not above Min) means no range.
        /// </summary>
        public struct SpawnRange
        {
            public float Min;
            public float Max;
            public float GapMin;
            public float GapMax;
            public float XOffset;

            public bool IsSet => Max > Min;

            /// <summary>One axis's value: uniform over Min..GapMin and GapMax..Max.</summary>
            public float Draw(Random random)
            {
                var low = Calc.Max(0f, Calc.Min(GapMin, Max) - Min);
                var high = Calc.Max(0f, Max - Calc.Max(GapMax, Min));
                var u = (float)random.NextDouble() * (low + high);
                return u < low ? Min + u : Calc.Max(GapMax, Min) + (u - low);
            }
        }

        public struct EnemySetup
        {
            /// <summary>Enemies per wave (Enemy.vb: maxEnemy 13, so 14).</summary>
            public int Count;
            /// <summary>Starting HP for every wave after the first (Root.vb Reset: 1.0).</summary>
            public float Hp;
            /// <summary>
            /// Starting HP for the very first wave only (Enemy.vb loadEnemy: 0.2, weaker than a post-Reset
            /// wave's <see cref="Hp"/>; UNI-T70).
            /// </summary>
            public float LoadHp;
            /// <summary>Random mass range (Enemy.vb: 1-2). Knockback is divided by mass.</summary>
            public float MassMin;
            public float MassMax;
            /// <summary>Collision circle radius, world units.</summary>
            public float Radius;
            /// <summary>Per-neighbour flocking push, units/s per axis (Engine.vb checkFlocking). 0 turns it off.</summary>
            public float FlockSpeed;
            public EnemyBrain.Settings Brain;
            /// <summary>
            /// How many enemies may Seek at once: all 14 by default, as the original (BIBLE D18, U.CHN-T81). A
            /// lower cap is UNI-T65's hang-back, a data option only: the rest hang back in Cautious or stalk in
            /// Align (<see cref="EnemyBrain.Settings"/>), and the nearest takes a slot as soon as one opens.
            /// </summary>
            public int SeekCap;
            /// <summary>Where Seek closes on, from the angel's position (Enemy.vb's offset aim, in the enemy centre's anchor).</summary>
            public Vec2 SeekAim;
            /// <summary>What flocking and a throw's knock test: Enemy.vb's rectangles (the default, U.CHN-T88) or the collision circles.</summary>
            public HitShape Shape;
            /// <summary>Enemy.vb's hit rectangle, from the enemy's centre.</summary>
            public HitBox Box;
            /// <summary>Where a wave spawns (Enemy.vb loadEnemy, Root.vb Reset); unset, enemies spawn anywhere in the arena.</summary>
            public SpawnRange Spawn;

            public static EnemySetup Default => new EnemySetup
            {
                Count = 14,
                Hp = 1f,
                LoadHp = 1f,
                MassMin = 1f,
                MassMax = 2f,
                Radius = 0.3f,
                FlockSpeed = EnemyBrain.DefaultFlockSpeed,
                Brain = EnemyBrain.Settings.Default,
                SeekCap = 14,
                // A scale-less fixture: circles, as its radius is; the shipped data is rectangles (U.CHN-T88).
                Shape = HitShape.Circle,
                Box = new HitBox(-0.025f * 9f, 0.04f * 9f, 0.05f * 9f, -0.03f * 9f),
            };
        }

        public struct Settings
        {
            public GameState.Settings Session;
            public EnemySetup Enemies;
            /// <summary>Every heart the world holds (Item.vb's Health10), in world units, per original frame.</summary>
            public HeartFloat.Settings Heart;
            /// <summary>The game speed every round starts at (Root.vb Reset: <c>gameSpeed = 1.0</c>).</summary>
            public float GameSpeed;
            /// <summary>
            /// How fast a held Minus or Equals moves the game speed, per second (Input.vb's 0.01 a frame, game.json's
            /// <c>gameSpeedStep</c>, at 60 fps: 0.6).
            /// </summary>
            public float GameSpeedRate;

            /// <summary>
            /// The original's own frame rate (BIBLE D9), steps a second: <see cref="Step"/> banks real time in a
            /// fixed-step accumulator and runs whole <c>1 / PhysicsStepRate</c>-second sub-steps from it, carrying
            /// the remainder forward, so the chain's discrete integration keeps the original's feel at any host
            /// frame rate, not only at an exact multiple of it. 60 (space.json's <c>framesPerSecond</c>, the same
            /// figure every per-frame data file is converted at) by default; 0 falls back to sub-steps sized to
            /// each call's own <c>deltaTime</c> instead of a fixed length (the coarser speed-only rule used
            /// before a data file set the rate).
            /// </summary>
            public float PhysicsStepRate;

            public static Settings Default => new Settings
            {
                Session = GameState.Settings.Default,
                Enemies = EnemySetup.Default,
                Heart = HeartFloat.Settings.Default,
                GameSpeed = 1f,
                GameSpeedRate = 0.6f,
                PhysicsStepRate = 60f,
            };
        }

        /// <summary>Where enemies spawn when there is no arena.</summary>
        public static readonly Rect2 DefaultArena = new Rect2(-8f, -4.5f, 16f, 9f);

        private readonly List<PlayerEntity> _players = new List<PlayerEntity>();
        private readonly List<EnemyEntity> _enemies = new List<EnemyEntity>();
        private readonly List<HeartFloat> _hearts = new List<HeartFloat>();
        private readonly List<GameEvent> _events = new List<GameEvent>();
        private readonly List<EnemyEntity> _touched = new List<EnemyEntity>();
        private readonly List<float> _touchedAt = new List<float>();
        /// <summary>
        /// Enemies the mace has already hit this call to <see cref="Step"/>, cleared once per call (U.CHN-T103).
        /// A slow host frame can bank more than one <see cref="Settings.PhysicsStepRate"/> sub-step into one call
        /// (BIBLE D9); the original never ran two of its own frames for one real one, so it could never land two
        /// mace hits where a caller only sees one <see cref="Step"/>. A knock small enough to already be at or
        /// under <see cref="EnemyBrain.Settings.RecoverSpeed"/> lets the enemy recover from Hurt in the very next
        /// sub-step (correct on its own, BIBLE D11's velocity-divided-by-mass knock, GameSettings' "one step of
        /// still"), and a mace still overlapping it would otherwise hit it again in that same bundled call.
        /// </summary>
        private readonly HashSet<EnemyId> _hitThisStep = new HashSet<EnemyId>();
        private readonly Random _random;
        // A separate stream for cosmetic-only randomness (an enemy's starting animation frame, UNI-T19)
        // so it never shifts the gameplay-affecting draws (positions, masses) that _random makes:
        // a replay or a seeded test pinning those stays exact regardless of how many enemies spawn.
        private readonly Random _visualRandom;
        private Vec2[] _snapshot = new Vec2[0];
        private int _nextEnemyId = 1;
        // Enemy.vb's loadEnemy (the game's first wave, weaker) runs once; every wave after it is Root.vb's
        // Reset (UNI-T70). Set the first time SpawnWave runs, whether that is this world's first Reset or,
        // in a test, a direct SpawnWave call.
        private bool _spawnedFirstWave;

        public GameWorld() : this(Settings.Default)
        {
        }

        public GameWorld(Settings settings, Random random = null)
        {
            Config = settings;
            _random = random ?? new Random();
            _visualRandom = new Random(_random.Next());
            State = new GameState(settings.Session);
            GameSpeed = Calc.Max(0f, settings.GameSpeed);
        }

        public Settings Config;

        /// <summary>HP, score, pause, game over and the fade: the session rules.</summary>
        public GameState State { get; }

        /// <summary>The play space: where enemies spawn and, for players that keep their chain in it, the walls links bounce off.</summary>
        public Rect2? Arena { get; set; }

        /// <summary>Seconds of play stepped so far, in game time (paused time does not count; the game speed scales it).</summary>
        public float Time { get; private set; }

        /// <summary>
        /// Seconds of play this round, in game time: Gui.vb's <c>timerMin:timerSec</c> clock, which <see cref="Reset"/>
        /// sets back to 0:00. The HUD shows it as m:ss (<see cref="RoundClock"/>, UNI-T53).
        /// </summary>
        public float RoundTime { get; private set; }

        /// <summary>
        /// Root.vb's <c>gameSpeed</c>: <see cref="Settings.GameSpeed"/> (1.0) at the start of every round, moved by
        /// Minus and Equals (<see cref="ChangeGameSpeed"/>), never below 0 (Input.vb). The original only drew it;
        /// here it is the time scale every <see cref="Step"/> runs at (<see cref="TimeScale"/>, BIBLE D13, V20):
        /// 0.5 plays at half speed, 2.0 at double, 0 stands still.
        /// </summary>
        public float GameSpeed
        {
            get => _gameSpeed;
            internal set => _gameSpeed = Calc.Max(0f, value);
        }

        private float _gameSpeed;

        /// <summary>
        /// Game seconds banked by <see cref="Step"/> that have not yet filled a whole
        /// <see cref="Settings.PhysicsStepRate"/> sub-step (BIBLE D9, U.CHN-T45): a true fixed-step
        /// accumulator, not a per-call rounding of how many sub-steps to split that call's own
        /// <c>deltaTime</c> into. A host frame rate that is not an exact multiple or divisor of
        /// <c>PhysicsStepRate</c> (144 Hz, 90 Hz, 50 Hz, ...) still advances in exactly
        /// <c>1 / PhysicsStepRate</c>-second sub-steps on average, carrying the remainder forward
        /// instead of running one sub-step sized to that call's own <c>deltaTime</c>.
        /// </summary>
        private float _stepAccumulator;

        /// <summary>The hearts in the world (Item.vb's items 0..maxItem: the one Health10 heart).</summary>
        public IReadOnlyList<HeartFloat> Hearts => _hearts;

        public IReadOnlyList<PlayerEntity> Players => _players;

        /// <summary>The enemies in the world. A killed or cleared enemy leaves this list at once.</summary>
        public IReadOnlyList<EnemyEntity> Enemies => _enemies;

        /// <summary>Events produced since the last <see cref="TakeEvents"/>, oldest first.</summary>
        public IReadOnlyList<GameEvent> PendingEvents => _events;

        // --- Players -------------------------------------------------------------------------

        /// <summary>Seats a player. Throws if <paramref name="id"/> is already in the world.</summary>
        public PlayerEntity AddPlayer(PlayerId id, PlayerSetup setup)
        {
            if (FindPlayer(id) != null)
            {
                throw new ArgumentException($"Player {id} is already in the world.", nameof(id));
            }

            var player = new PlayerEntity(id, setup);
            _players.Add(player);
            return player;
        }

        /// <summary>Takes a player out of the world. Whatever their grapple held is let go. Returns false if they were not in it.</summary>
        public bool RemovePlayer(PlayerId id)
        {
            var player = FindPlayer(id);
            if (player == null)
            {
                return false;
            }

            player.Grapple.SetHeld(false, Vec2.Zero);
            return _players.Remove(player);
        }

        /// <summary>The player with <paramref name="id"/>, or null.</summary>
        public PlayerEntity FindPlayer(PlayerId id)
        {
            foreach (var player in _players)
            {
                if (player.Id == id)
                {
                    return player;
                }
            }

            return null;
        }

        /// <summary>Retunes a seated player (the authority's settings, not a player command). Returns false if they are not seated.</summary>
        public bool Configure(PlayerId id, PlayerSetup setup)
        {
            var player = FindPlayer(id);
            player?.Configure(setup);
            return player != null;
        }

        // --- Enemies -------------------------------------------------------------------------

        /// <summary>The enemy with <paramref name="id"/> still in the world, or null.</summary>
        public EnemyEntity FindEnemy(EnemyId id)
        {
            foreach (var enemy in _enemies)
            {
                if (enemy.Id == id)
                {
                    return enemy;
                }
            }

            return null;
        }

        /// <summary>
        /// Puts an enemy in the world at <paramref name="position"/>, set up from
        /// <see cref="Settings.Enemies"/> with a random mass in its range unless <paramref name="mass"/>
        /// fixes it, and <paramref name="hp"/> overriding the setup's HP.
        /// </summary>
        public EnemyEntity SpawnEnemy(Vec2 position, float? mass = null, float? hp = null)
        {
            var setup = Config.Enemies;
            var lo = Calc.Min(setup.MassMin, setup.MassMax);
            var hi = Calc.Max(setup.MassMin, setup.MassMax);
            var rolled = mass ?? (hi - lo < 1e-6f ? lo : lo + (float)_random.NextDouble() * (hi - lo));
            // Enemy.vb's loadEnemy: Enemy(n).Frame = getRndNum(0, 6). Drawn from _visualRandom, not
            // _random: purely cosmetic, so it never shifts the position/mass sequence (see field doc).
            var brain = new EnemyBrain(hp ?? setup.Hp, rolled, setup.Brain, _visualRandom.Next(EnemyBrain.FrameCount));
            if (SeekerCount() >= setup.SeekCap)
            {
                brain.Wait((SeekPoint(position) - position).Magnitude);
            }

            var enemy = new EnemyEntity(new EnemyId(_nextEnemyId++), brain, position, Calc.Max(0f, setup.Radius));
            _enemies.Add(enemy);
            Emit(new EnemySpawned(enemy.Id, position));
            return enemy;
        }

        /// <summary>
        /// Spawns <see cref="EnemySetup.Count"/> enemies at random points in the arena: the first wave this
        /// world ever spawns at <see cref="EnemySetup.LoadHp"/> (Enemy.vb loadEnemy), every one after it at
        /// <see cref="EnemySetup.Hp"/> (Root.vb Reset; UNI-T70).
        /// </summary>
        public void SpawnWave()
        {
            var hp = _spawnedFirstWave ? Config.Enemies.Hp : Config.Enemies.LoadHp;
            _spawnedFirstWave = true;
            var spawn = Config.Enemies.Spawn;
            if (spawn.IsSet)
            {
                for (var i = 0; i < Config.Enemies.Count; i++)
                {
                    var x = spawn.Draw(_random) + spawn.XOffset;
                    var y = spawn.Draw(_random);
                    SpawnEnemy(new Vec2(x, y), hp: hp);
                }

                return;
            }

            foreach (var point in RandomPositions(Arena ?? DefaultArena, Config.Enemies.Count, _random))
            {
                SpawnEnemy(point, hp: hp);
            }
        }

        /// <summary>Takes every enemy out of the world; grapples holding one let go.</summary>
        public void ClearEnemies()
        {
            foreach (var player in _players)
            {
                if (player.Grapple.State == GrappleState.Holding)
                {
                    player.Grapple.Drop();
                }
            }

            foreach (var enemy in _enemies)
            {
                enemy.InWorld = false;
                Emit(new EnemyRemoved(enemy.Id));
            }

            _enemies.Clear();
        }

        /// <summary>How many enemies are seeking now (held against <see cref="EnemySetup.SeekCap"/>).</summary>
        public int SeekerCount()
        {
            var seekers = 0;
            foreach (var enemy in _enemies)
            {
                if (enemy.State == EnemyState.Seek)
                {
                    seekers++;
                }
            }

            return seekers;
        }

        /// <summary>
        /// The Seek cap (UNI-T65): while fewer than the cap seek, the waiting enemy nearest its angel takes
        /// a slot; while more do (a hurt enemy recovered into Seek, or the cap was lowered), the seeker
        /// farthest from its angel goes back to waiting.
        /// </summary>
        private void AllocateSeekSlots()
        {
            var cap = Calc.Max(0, Config.Enemies.SeekCap);
            var seekers = SeekerCount();

            while (seekers < cap)
            {
                var next = Extreme(waiting: true, nearest: true);
                if (next == null)
                {
                    break;
                }

                next.Brain.Promote();
                seekers++;
            }

            while (seekers > cap)
            {
                var last = Extreme(waiting: false, nearest: false);
                last.Brain.Wait(DistanceToSeekPoint(last));
                seekers--;
            }
        }

        /// <summary>The waiting (or seeking) enemy nearest to (or farthest from) the angel it homes on.</summary>
        private EnemyEntity Extreme(bool waiting, bool nearest)
        {
            EnemyEntity best = null;
            var bestDistance = 0f;
            foreach (var enemy in _enemies)
            {
                if (waiting ? !enemy.Brain.IsWaiting : enemy.State != EnemyState.Seek)
                {
                    continue;
                }

                var distance = DistanceToSeekPoint(enemy);
                if (best == null || (nearest ? distance < bestDistance : distance > bestDistance))
                {
                    best = enemy;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private float DistanceToSeekPoint(EnemyEntity enemy) => (SeekPoint(enemy.Position) - enemy.Position).Magnitude;

        /// <summary>Random points inside <paramref name="area"/>, <paramref name="count"/> of them.</summary>
        public static Vec2[] RandomPositions(Rect2 area, int count, Random random)
        {
            var points = new Vec2[Calc.Max(0, count)];
            for (var i = 0; i < points.Length; i++)
            {
                points[i] = new Vec2(
                    area.XMin + (float)random.NextDouble() * area.Width,
                    area.YMin + (float)random.NextDouble() * area.Height);
            }

            return points;
        }

        // --- Hearts --------------------------------------------------------------------------

        /// <summary>
        /// Puts a heart in the world, tuned from <see cref="Settings.Heart"/>: at its start, swaying left at
        /// loadItem's <c>floatSPD</c> (Item.vb loadItem). A reset puts it back at Reset's figures.
        /// </summary>
        public HeartFloat AddHeart()
        {
            var heart = new HeartFloat(Config.Heart);
            _hearts.Add(heart);
            return heart;
        }

        /// <summary>
        /// Engine.vb checkItemCollect: each heart not yet picked up that a seated angel touches is consumed
        /// and heals that player (<see cref="CollectHeart"/>). Only while play runs; every step runs it.
        /// Returns the HP gained.
        /// </summary>
        public int CheckHearts()
        {
            if (!State.IsPlaying)
            {
                return 0;
            }

            var gained = 0;
            foreach (var heart in _hearts)
            {
                foreach (var player in _players)
                {
                    var angel = player.AngelPosition;
                    if (heart.TryCollect(angel.X, angel.Y))
                    {
                        gained += CollectHeart(player.Id, heart.HealAmount);
                        break;
                    }
                }
            }

            return gained;
        }

        // --- The session ---------------------------------------------------------------------

        /// <summary>
        /// Starts the round over: Root.vb's Reset, in one place, for the R key (<see cref="ResetGame"/>) and
        /// the end of the game-over fade alike. Score 0, the clock at 0:00, the fade back at full light
        /// (Reset's <c>fadeOut = 255</c> and white ambient), game speed 1.0, unpaused; each angel at full HP
        /// and home at its start, flying, its chain collapsed on its hand with the chain's spring, resistance
        /// and gravity back at the data files' figures (whatever live tuning changed them) and its grapple
        /// idle; every enemy replaced by a fresh wave, seeking, each at a real random point in the arena (the
        /// original's integer random put them all in the corners, BIBLE D1); every heart back at its start,
        /// swaying left at Reset's <c>floatSPD</c>, drawn again. The chain length, the follow flag and the
        /// heart's <c>turnAround</c> are kept, as Root.vb's Reset touches none of them. Then
        /// <see cref="WorldReset"/> has presentation recentre the camera (Reset's cam and target at the
        /// origin), jump the music to a random point (<c>rndMusicPos</c>) and switch lighting back on
        /// (<c>bLighting = True</c>; the time of day runs on). The authority calls this once to begin: that
        /// first wave spawns weaker, at Enemy.vb loadEnemy's HP, not Reset's (<see cref="SpawnWave"/>,
        /// UNI-T70).
        /// </summary>
        public void Reset()
        {
            ClearEnemies();
            State.Reset();
            RoundTime = 0f;
            GameSpeed = Config.GameSpeed;
            _stepAccumulator = 0f;
            foreach (var player in _players)
            {
                player.Restart();
            }

            foreach (var heart in _hearts)
            {
                heart.Reset();
            }

            Emit(new WorldReset());
            SpawnWave();
        }

        /// <summary>
        /// <paramref name="player"/>'s angel touched a heart worth <paramref name="amount"/> HP
        /// (Engine.vb checkItemCollect). Heals up to max HP. Returns the HP gained.
        /// </summary>
        public int CollectHeart(PlayerId player, int amount)
        {
            if (!State.IsPlaying || FindPlayer(player) == null)
            {
                return 0;
            }

            var gained = State.Heal(amount);
            if (gained > 0)
            {
                Emit(new AngelHealed(player, State.Hp, gained));
            }

            return gained;
        }

        // --- Commands ------------------------------------------------------------------------

        /// <summary>
        /// Applies a player's command. Returns false when it changed nothing: the sender is not seated,
        /// the game is paused (steering and the chain length wait then; pause, reset, quit, follow, the
        /// gem skins, the mace skin, the live-tuning keys and the game speed still work, as in Form1.vb's key handler and
        /// Input.vb's polling), or the command had no effect (a tuning knob already at its limit, the game
        /// speed already 0, the gems already on). Steering also waits while the game speed is 0, as time stands still.
        /// </summary>
        public bool Submit(GameCommand command)
        {
            switch (command)
            {
                case null:
                    throw new ArgumentNullException(nameof(command));

                case Pause pause:
                    return SetPaused(pause.Player, pause.Paused);

                case TogglePause toggle:
                    return SetPaused(toggle.Player, !State.IsPaused);

                case ResetGame _:
                    Reset();
                    return true;

                case QuitGame quit:
                    Emit(new QuitRequested(quit.Player));
                    return true;
            }

            var player = FindPlayer(command.Player);
            if (player == null)
            {
                return false;
            }

            switch (command)
            {
                case SteerAngel steer:
                    if (State.IsPaused || GameSpeed <= 0f)
                    {
                        return false;
                    }

                    // The mouse moves the angel as far at any speed; its hover and wing beat run on game time.
                    player.Flight.Step(steer.MouseDelta, Calc.Max(0f, steer.DeltaTime) * GameSpeed);
                    return true;

                case ArmGrapple _:
                    if (player.ButtonHeld)
                    {
                        return false;
                    }

                    player.ButtonHeld = true;
                    player.PressedSinceStep = true;
                    return true;

                case ReleaseGrapple _:
                    if (!player.ButtonHeld)
                    {
                        return false;
                    }

                    player.ButtonHeld = false;
                    return true;

                case ToggleFollow _:
                    var changed = ChainFollow.Apply(player.Chain, new ToggleFollowCommand(player.Id.Value));
                    Emit(new FollowChanged(player.Id, changed.IsFollowing));
                    return true;

                case SkinLinks _:
                    if (!player.PutOnGems())
                    {
                        return false;
                    }

                    Emit(new LinksSkinned(player.Id));
                    return true;

                case ToggleMaceMode _:
                    Emit(new MaceModeChanged(player.Id, player.NextMaceMode()));
                    return true;

                case TuneChain tune:
                    if (!player.Tune(tune.Knob, tune.Steps))
                    {
                        return false;
                    }

                    Emit(new ChainTuned(player.Id, tune.Knob, ChainTuning.Read(player.Chain.Config, tune.Knob)));
                    return true;

                case ChangeGameSpeed change:
                    var speed = TimeScale.Change(GameSpeed, change.Direction, Config.GameSpeedRate, change.HeldSeconds);
                    if (speed == GameSpeed)
                    {
                        return false;
                    }

                    GameSpeed = speed;
                    Emit(new GameSpeedChanged(player.Id, GameSpeed));
                    return true;

                case ChangeChainLength change:
                    if (State.IsPaused || !player.SetMaceIndex(player.MaceIndex + change.Links))
                    {
                        return false;
                    }

                    Emit(new ChainLengthChanged(player.Id, player.MaceIndex));
                    return true;

                default:
                    throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command));
            }
        }

        private bool SetPaused(PlayerId by, bool paused)
        {
            var before = State.IsPaused;
            if (State.SetPaused(paused) == before)
            {
                return false;
            }

            Emit(new PauseChanged(by, State.IsPaused));
            return true;
        }

        // --- The step ------------------------------------------------------------------------

        /// <summary>
        /// One real step of <paramref name="deltaTime"/> seconds, run at the <see cref="GameSpeed"/>: it banks
        /// <paramref name="deltaTime"/> x the speed of game time in <see cref="_stepAccumulator"/> and then runs
        /// as many whole <c>1 / <see cref="Settings.PhysicsStepRate"/></c> sub-steps as that has filled (BIBLE D9,
        /// U.CHN-T45), up to <see cref="TimeScale.MaxSubsteps"/>, carrying any leftover to the next call -- a true
        /// fixed-step accumulator, so the chain's discrete per-frame integration keeps the original's own frame
        /// length (and so its feel) at any host frame rate, not only at an exact multiple of it.
        /// <see cref="Settings.PhysicsStepRate"/> at or below 0 falls back to sub-steps sized to this call's own
        /// <paramref name="deltaTime"/> instead of a fixed length (the coarser rule used before a data file set
        /// the rate). Everything the original did once a frame (the mace's sweep, the grapple, the
        /// hurt check, the game-over fade) runs once a sub-step. A game-over reset ends the step there and clears
        /// the accumulator: the new round starts on the next one, at its own speed. Does nothing while paused or
        /// at speed 0.
        /// </summary>
        public void Step(float deltaTime)
        {
            if (State.IsPaused || deltaTime <= 0f || GameSpeed <= 0f)
            {
                return;
            }

            var fixedStep = Config.PhysicsStepRate > 0f ? 1f / Config.PhysicsStepRate : deltaTime;
            _stepAccumulator += deltaTime * GameSpeed;

            // A long stall (a breakpoint, a loading hitch) should not queue an unbounded backlog of catch-up steps.
            var maxBanked = fixedStep * TimeScale.MaxSubsteps;
            if (_stepAccumulator > maxBanked)
            {
                _stepAccumulator = maxBanked;
            }

            // Cleared once a call, not once a sub-step (U.CHN-T103): see _hitThisStep.
            _hitThisStep.Clear();

            var steps = 0;
            while (_stepAccumulator >= fixedStep && steps < TimeScale.MaxSubsteps)
            {
                if (StepOnce(fixedStep))
                {
                    _stepAccumulator = 0f;
                    return;
                }

                _stepAccumulator -= fixedStep;
                steps++;
            }
        }

        /// <summary>
        /// One sub-step of <paramref name="deltaTime"/> seconds of game time: the original's frame. Returns true if
        /// the game-over fade finished and the world reset itself.
        /// </summary>
        private bool StepOnce(float deltaTime)
        {
            Time += deltaTime;
            RoundTime += deltaTime;

            // Item.vb renderItem: the hearts move first; checkItemCollect runs after the contacts.
            foreach (var heart in _hearts)
            {
                heart.Advance(deltaTime);
            }

            foreach (var player in _players)
            {
                StepChain(player, deltaTime);
            }

            StepEnemies(deltaTime);

            foreach (var player in _players)
            {
                StepGrapple(player);
            }

            foreach (var player in _players)
            {
                StepMace(player);
            }

            // Engine.vb's frame order (RECONSTRUCTION.md 3): checkFlocking, checkMaceImpact, checkThrowImpact,
            // checkAngelHit -- a thrown enemy's knock runs after the mace, not before it (U.CHN-T106).
            CheckThrowImpact();

            foreach (var player in _players)
            {
                CheckContact(player);
            }

            CheckHearts();
            return StepGameOver();
        }

        /// <summary>Link 0 follows the angel's hand; the rest swing on their springs.</summary>
        private void StepChain(PlayerEntity player, float deltaTime)
        {
            var chain = player.Chain;
            chain.Bounds = player.Setup.KeepChainInArena ? Arena : null;
            chain.SetAnchor(player.Flight.HandSocket);
            chain.Step(deltaTime);
        }

        /// <summary>
        /// The Seek cap is kept first (<see cref="AllocateSeekSlots"/>); then every free enemy moves by its brain's velocity plus the flocking push from each live, ungrabbed
        /// enemy it overlaps; a thrown one knocks every hittable enemy it touches. Overlaps are judged on
        /// where everyone stood at the start of the step, so the order enemies move in does not matter.
        /// </summary>
        private void StepEnemies(float deltaTime)
        {
            AllocateSeekSlots();

            var count = _enemies.Count;
            if (_snapshot.Length < count)
            {
                _snapshot = new Vec2[count];
            }

            for (var i = 0; i < count; i++)
            {
                _snapshot[i] = _enemies[i].Position;
            }

            var flockSpeed = Config.Enemies.FlockSpeed;
            var rectangles = Config.Enemies.Shape == HitShape.Rectangle;
            var boxes = Config.Enemies.Box;
            for (var i = 0; i < count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.IsAlive)
                {
                    continue;
                }

                if (enemy.State == EnemyState.Grabbed)
                {
                    // The grapple carries it; renderEnemy still animates it (U.CHN-T84).
                    enemy.Brain.Animate(deltaTime);
                    continue;
                }

                var here = _snapshot[i];
                var velocity = enemy.Brain.Step(here, SeekPoint(here), deltaTime);
                var push = Vec2.Zero;

                for (var j = 0; j < count; j++)
                {
                    var other = _enemies[j];
                    if (j == i)
                    {
                        continue;
                    }

                    // Rectangles: checkFlocking visits the ordered pair (d, c) and pushes both when d's edges are inside
                    // c's, so this enemy is pushed once for each order that intersects; flockSpeed already carries the two
                    // visits of a symmetric overlap, so one intersecting order is half of it.
                    float visits;
                    if (rectangles)
                    {
                        var mine = boxes.At(here);
                        var theirs = boxes.At(_snapshot[j]);
                        visits = (HeartFloat.RectIntersect(mine, theirs) ? 0.5f : 0f) + (HeartFloat.RectIntersect(theirs, mine) ? 0.5f : 0f);
                    }
                    else
                    {
                        visits = Touching(here, enemy.Radius, _snapshot[j], other.Radius) ? 1f : 0f;
                    }

                    if (visits > 0f && flockSpeed > 0f && other.IsAlive && other.State != EnemyState.Grabbed)
                    {
                        var tieBreak = enemy.Id.Value < other.Id.Value ? 1 : -1;
                        push += EnemyBrain.FlockPush(here, _snapshot[j], flockSpeed, deltaTime, tieBreak) * visits;
                    }
                }

                enemy.Position = here + velocity * deltaTime + push;
            }
        }

        /// <summary>
        /// Engine.vb checkThrowImpact: a thrown enemy knocks every live, hittable enemy its rectangle touches as it
        /// flies, at this frame's (post-flocking, post-mace) positions -- run after <see cref="StepMace"/> so a mace
        /// hit lands first on an enemy the throw would otherwise have already knocked out of <see cref="EnemyEntity
        /// .CanBeHit"/> this same frame (U.CHN-T106: checkMaceImpact precedes checkThrowImpact, RECONSTRUCTION.md 3).
        /// The thrown enemy's own self-intersection (BIBLE D5) is skipped, not knocked a second time by itself.
        /// </summary>
        private void CheckThrowImpact()
        {
            var rectangles = Config.Enemies.Shape == HitShape.Rectangle;
            var boxes = Config.Enemies.Box;
            var count = _enemies.Count;
            for (var i = 0; i < count; i++)
            {
                var enemy = _enemies[i];
                if (!enemy.IsAlive || !enemy.Brain.IsThrown)
                {
                    continue;
                }

                for (var j = 0; j < count; j++)
                {
                    if (j == i)
                    {
                        continue;
                    }

                    var other = _enemies[j];
                    if (!other.CanBeHit)
                    {
                        continue;
                    }

                    var touching = rectangles
                        ? HeartFloat.RectIntersect(boxes.At(enemy.Position), boxes.At(other.Position))
                        : Touching(enemy.Position, enemy.Radius, other.Position, other.Radius);
                    if (touching)
                    {
                        other.Brain.Knock(enemy.Brain.Velocity);
                        Emit(new EnemyKnocked(enemy.Id, other.Id));
                    }
                }
            }
        }

        /// <summary>The point enemies close on: the nearest angel's position plus Seek's own offset (Enemy.vb's aim).</summary>
        private Vec2 SeekPoint(Vec2 from)
        {
            var best = from;
            var bestDistance = float.PositiveInfinity;
            var aimed = false;
            foreach (var player in _players)
            {
                var distance = (player.AngelPosition - from).SqrMagnitude;
                if (distance < bestDistance)
                {
                    best = player.AngelPosition;
                    bestDistance = distance;
                    aimed = true;
                }
            }

            return aimed ? best + Config.Enemies.SeekAim : best;
        }

        /// <summary>
        /// Arms or releases from the button, catches the nearest grabbable enemy inside the catch radius
        /// while armed, and carries what it holds with the mace.
        /// </summary>
        private void StepGrapple(PlayerEntity player)
        {
            var grapple = player.Grapple;
            var before = grapple.State;

            // A press and release that both landed between two steps still counts as a press: arm for
            // this step so a quick tap can catch what the mace is touching.
            var held = player.ButtonHeld || player.PressedSinceStep;
            player.PressedSinceStep = false;

            if (grapple.SetHeld(held, player.MaceVelocity) is EnemyEntity thrown)
            {
                Emit(new Thrown(player.Id, thrown.Id, player.MaceVelocity * grapple.ThrowMultiplier));
            }

            // The catch is the mace's touch, which is not kept while the chain is not following (BIBLE D17).
            if (grapple.State == GrappleState.Armed && ChainFollow.MaceHits(player.IsFollowing))
            {
                var target = NearestGrabbable(player.MacePosition, player.Setup.CatchRadius);
                if (target != null && grapple.TryCatch(target))
                {
                    Emit(new Grabbed(player.Id, target.Id));
                }
            }

            grapple.Carry(player.MacePosition);
            EmitGrappleChange(player, before);
        }

        private EnemyEntity NearestGrabbable(Vec2 center, float radius)
        {
            EnemyEntity nearest = null;
            var nearestDistance = float.PositiveInfinity;
            foreach (var enemy in _enemies)
            {
                if (!enemy.CanBeHit || !enemy.Overlaps(center, radius))
                {
                    continue;
                }

                var distance = (enemy.Position - center).SqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearest = enemy;
                    nearestDistance = distance;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Engine.vb checkMaceImpact on the mace's swept circle: every enemy it touched since the last
        /// step, nearest the start first, is caught instead if the grapple is armed with an empty grip,
        /// else knocked back at the mace's velocity / its mass, damaged by mace speed x damage per speed
        /// and scored; an enemy held in the grapple takes the same damage.
        /// </summary>
        private void StepMace(PlayerEntity player)
        {
            // Not following (F key): Chain.vb stops keeping rectMace on the mace, so it neither hits nor
            // catches (BIBLE D17). Forget the last position so turning it back on does not sweep from there.
            if (!ChainFollow.MaceHits(player.IsFollowing))
            {
                player.PreviousMaceIndex = -1;
                return;
            }

            var index = player.MaceIndex;
            var position = player.MacePosition;

            // First step, or the chain was shortened/lengthened: no sweep from a different link.
            var from = index == player.PreviousMaceIndex ? player.PreviousMacePosition : position;
            player.PreviousMaceIndex = index;
            player.PreviousMacePosition = position;

            CollectTouched(from, position, player.Setup);
            if (_touched.Count == 0)
            {
                return;
            }

            var grapple = player.Grapple;
            var before = grapple.State;
            var velocity = player.MaceVelocity;
            var held = player.HeldEnemy;

            foreach (var enemy in _touched.ToArray())
            {
                if (!enemy.CanBeHit)
                {
                    continue;
                }

                if (grapple.State == GrappleState.Armed && grapple.TryCatch(enemy))
                {
                    grapple.Carry(position);
                    held = enemy;
                    Emit(new Grabbed(player.Id, enemy.Id));
                    continue;
                }

                // One sub-step's recovery from Hurt (CanBeHit above) can fall inside the same bundled Step
                // call as the hit that caused it (U.CHN-T103): the mace has not swept anywhere new, so this
                // is still the one touch this call found, not a second one.
                if (!_hitThisStep.Add(enemy.Id))
                {
                    continue;
                }

                var damage = enemy.Brain.Hit(velocity, player.Setup.DamagePerSpeed, player.Setup.DamageFormula, player.Setup.VelocityToOriginalScale);
                var points = State.AddHitScore(damage);
                var killed = !enemy.IsAlive;
                Emit(new EnemyHit(player.Id, enemy.Id, damage, velocity, points, killed));
                if (killed)
                {
                    Kill(enemy, player.Id);
                }

                if (held != null && held != enemy && held.InWorld && held.Brain.Damage(damage))
                {
                    Kill(held, player.Id);
                    held = null;
                }
            }

            EmitGrappleChange(player, before);
        }

        /// <summary>
        /// Enemies the mace touches moving from <paramref name="from"/> to <paramref name="to"/>, nearest the start first:
        /// Chain.vb's rectMace against Enemy.vb's rectangle through <c>rectIntersect</c>, the rectangle swept along the move
        /// (BIBLE D12), or, with the circle option, the swept hit circle against the enemy's circle.
        /// </summary>
        private void CollectTouched(Vec2 from, Vec2 to, PlayerSetup setup)
        {
            _touched.Clear();
            _touchedAt.Clear();
            var moved = (to - from).SqrMagnitude > 1e-10f;
            var rectangles = setup.MaceShape == HitShape.Rectangle;
            var enemyBox = Config.Enemies.Box;
            var radius = setup.HitRadius;
            var side = Calc.Min(setup.MaceBox.MinSide, enemyBox.MinSide);

            foreach (var enemy in _enemies)
            {
                float fraction;
                if (rectangles)
                {
                    var target = enemyBox.At(enemy.Position);
                    if (moved && Sweep.RectCast(setup.MaceBox.At(from), to - from, target, side, out fraction))
                    {
                        Insert(enemy, fraction);
                    }
                    else if (HeartFloat.RectIntersect(setup.MaceBox.At(to), target))
                    {
                        // Whatever the mace rests in now comes after everything the sweep met.
                        Insert(enemy, 2f);
                    }
                }
                else if (moved && Sweep.CircleCast(from, to, radius, enemy.Position, enemy.Radius, out fraction))
                {
                    Insert(enemy, fraction);
                }
                else if (enemy.Overlaps(to, radius))
                {
                    Insert(enemy, 2f);
                }
            }
        }

        private void Insert(EnemyEntity enemy, float fraction)
        {
            var at = _touched.Count;
            while (at > 0 && _touchedAt[at - 1] > fraction)
            {
                at--;
            }

            _touched.Insert(at, enemy);
            _touchedAt.Insert(at, fraction);
        }

        private void Kill(EnemyEntity enemy, PlayerId by)
        {
            foreach (var player in _players)
            {
                if (player.HeldEnemy == enemy)
                {
                    var before = player.Grapple.State;
                    player.Grapple.Drop();
                    EmitGrappleChange(player, before);
                }
            }

            enemy.InWorld = false;
            _enemies.Remove(enemy);
            Emit(new EnemyKilled(by, enemy.Id, enemy.Position));
        }

        /// <summary>
        /// Engine.vb checkAngelHit: any live enemy touching the angel costs HP, at most once per hurt delay; a held one
        /// too, as checkAngelHit tests every enemy whatever its AI (U.CHN-T83). A dead one does not: the original kept
        /// testing its last rectangle, an invisible hit box where it died (BIBLE D34).
        /// </summary>
        private void CheckContact(PlayerEntity player)
        {
            var rectangles = player.Setup.ContactShape == HitShape.Rectangle;
            var radius = player.Setup.ContactRadius;
            if (!State.IsPlaying || (!rectangles && radius <= 0f))
            {
                return;
            }

            var angel = player.AngelPosition;
            var angelRect = player.Setup.AngelBox.At(angel);
            var enemyBox = Config.Enemies.Box;
            foreach (var enemy in _enemies)
            {
                // checkAngelHit: rectIntersect(Angel.Rect, Enemy.Rect), only the angel's edges inside the enemy's.
                var touching = rectangles
                    ? HeartFloat.RectIntersect(angelRect, enemyBox.At(enemy.Position))
                    : enemy.Overlaps(angel, radius);
                if (!enemy.IsAlive || !touching)
                {
                    continue;
                }

                if (State.TryHurt(Time))
                {
                    Emit(new AngelHurt(player.Id, State.Hp));
                    if (State.IsGameOver)
                    {
                        Emit(new GameOver());
                    }
                }

                return;
            }
        }

        /// <summary>Engine.vb GameOver: the light fades 5 a step, then the game resets itself.</summary>
        private bool StepGameOver()
        {
            if (State.IsGameOver && State.AdvanceGameOver())
            {
                Reset();
                return true;
            }

            return false;
        }

        // --- Events --------------------------------------------------------------------------

        /// <summary>Hands over every event produced since the last call, oldest first, and forgets them.</summary>
        public GameEvent[] TakeEvents()
        {
            var events = _events.ToArray();
            _events.Clear();
            return events;
        }

        private void Emit(GameEvent gameEvent) => _events.Add(gameEvent);

        private void EmitGrappleChange(PlayerEntity player, GrappleState before)
        {
            if (player.Grapple.State != before)
            {
                Emit(new GrappleChanged(player.Id, player.Grapple.State));
            }
        }

        private static bool Touching(Vec2 a, float ra, Vec2 b, float rb)
        {
            var reach = ra + rb;
            return (a - b).SqrMagnitude < reach * reach;
        }
    }
}
