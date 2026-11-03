using System;

namespace MindAttic.Chain.Core
{
    /// <summary>
    /// How one player's angel, chain, mace and grapple are tuned when they join the world. Every figure
    /// is the original's, converted to world units per second (see docs/chain-physics.md).
    /// </summary>
    public struct PlayerSetup
    {
        /// <summary>Where the angel starts, and where a reset puts it back (Root.vb Reset: Angel(0) at 0,0).</summary>
        public Vec2 Start;
        public AngelFlight.Settings Flight;

        /// <summary>Links in the chain, anchor included (Chain.vb: 25).</summary>
        public int LinkCount;
        public ChainSimulation.Settings Chain;
        /// <summary>Bounce the links off the world's arena (Elastic Trail's wall bounce).</summary>
        public bool KeepChainInArena;
        /// <summary>Fraction of a link's speed into a wall kept after the bounce.</summary>
        public float WallBounce;
        /// <summary>Whether the chain starts following the hand (Chain.vb: <c>followMouse As Boolean = True</c>).</summary>
        public bool Following;
        /// <summary>Form1.vb's live-tuning keys: each knob's step and limits (chain.json's <c>tuning</c>).</summary>
        public ChainTuning Tuning;
        /// <summary>What each link is drawn with: Textures.vb's link and Form1.vb's U gems (chain.json's <c>skins</c>).</summary>
        public LinkSkins Skins;
        /// <summary>The skin the mace starts in (Chain.vb's <c>maceMode</c>, chain.json's <c>maceSkins.mode</c>, UNI-T43).</summary>
        public MaceMode MaceMode;

        /// <summary>The mace's link (Chain.vb's Mace: 6, adjustable 1-12).</summary>
        public int MaceIndex;
        public int MinMaceIndex;
        public int MaxMaceIndex;
        /// <summary>Mass of the mace link; every other link has mass 1 (Chain.vb: 1).</summary>
        public float MaceMass;
        /// <summary>Radius of the mace's hit circle, world units.</summary>
        public float HitRadius;
        /// <summary>Damage per world unit per second of mace speed at impact (<see cref="MaceDamageFormula.SpeedTimesFactor"/> only).</summary>
        public float DamagePerSpeed;
        /// <summary>Which formula a mace hit computes its damage with (chain.json's <c>port</c>, U.CHN-T45, BIBLE D11).</summary>
        public MaceDamageFormula DamageFormula;
        /// <summary>
        /// Converts a mass-divided impact velocity (world units/second) back to the original's own per-frame
        /// screen-space units, for <see cref="MaceDamageFormula.Original"/>'s <c>abs(dX + dY/2)</c> (BIBLE D11):
        /// <c>OriginalSpace.ToOriginalSpeed(1f)</c> when built from the data files. 1 (no conversion) for a
        /// <see cref="Default"/> built with no data file, since its chain figures are not the original's own
        /// screen-space numbers either.
        /// </summary>
        public float VelocityToOriginalScale;

        /// <summary>Radius around the mace that catches an enemy while the grapple is armed.</summary>
        public float CatchRadius;
        /// <summary>Launch velocity = mace velocity x this.</summary>
        public float ThrowMultiplier;

        /// <summary>Radius around the angel that counts as an enemy touching it (with <see cref="ContactShape"/> Circle).</summary>
        public float ContactRadius;

        /// <summary>What an enemy touching the angel is tested with: Angel.vb's rectangle (the default, U.CHN-T88) or the contact circle.</summary>
        public HitShape ContactShape;
        /// <summary>Angel.vb's hit rectangle, from <see cref="PlayerEntity.AngelPosition"/>.</summary>
        public HitBox AngelBox;
        /// <summary>What a mace hit is tested with: Chain.vb's rectMace swept along the move (the default, U.CHN-T88, BIBLE D12) or the swept hit circle.</summary>
        public HitShape MaceShape;
        /// <summary>Chain.vb's rectMace, from the mace link.</summary>
        public HitBox MaceBox;

        public static PlayerSetup Default => new PlayerSetup
        {
            Start = Vec2.Zero,
            Flight = AngelFlight.Settings.Default,
            LinkCount = 25,
            Chain = ChainSimulation.Settings.Default,
            KeepChainInArena = true,
            WallBounce = 0.5f,
            Following = ChainFollow.StartsFollowing,
            Tuning = ChainTuning.Default,
            Skins = LinkSkins.Plain,
            MaceMode = MaceMode.SpikeBall,
            MaceIndex = 6,
            MinMaceIndex = 1,
            MaxMaceIndex = 12,
            MaceMass = 1f,
            HitRadius = 0.25f,
            DamagePerSpeed = 0.05f,
            DamageFormula = MaceDamageFormula.Original,
            VelocityToOriginalScale = 1f,
            CatchRadius = 0.3f,
            ThrowMultiplier = 1f,
            ContactRadius = 0.35f,
            // A setup built without the data files is a scale-less fixture: circles, as its radii are. The shipped
            // data (GameSettings) is rectangles; the boxes are the original's figures at the shipped length scale 9.
            ContactShape = HitShape.Circle,
            AngelBox = new HitBox(0f, 0.06f * 9f, 0.15f * 9f, 0.03f * 9f),
            MaceShape = HitShape.Circle,
            MaceBox = new HitBox(0.01f * 9f, 0.075f * 9f, 0.0375f * 9f, -0.0275f * 9f),
        };
    }

    /// <summary>
    /// One player in the <see cref="GameWorld"/>: their angel's flight, their chain, the mace at its end
    /// and their grapple. The world steps all of it; the player's presentation reads it and sends
    /// commands, each carrying this player's <see cref="Id"/>.
    /// </summary>
    public sealed class PlayerEntity
    {
        private PlayerSetup _setup;
        private int _maceIndex;
        private bool _tuned;

        internal bool ButtonHeld;
        internal bool PressedSinceStep;
        internal Vec2 PreviousMacePosition;
        internal int PreviousMaceIndex = -1;

        internal PlayerEntity(PlayerId id, PlayerSetup setup)
        {
            Id = id;
            _setup = Sanitize(setup);
            _maceIndex = _setup.MaceIndex;
            MaceMode = _setup.MaceMode;
            Restart();
        }

        public PlayerId Id { get; }
        public PlayerSetup Setup => _setup;

        public AngelFlight Flight { get; private set; }
        public ChainSimulation Chain { get; private set; }
        public Grapple Grapple { get; private set; }

        public int MaxMaceIndex => Calc.Clamp(_setup.MaxMaceIndex, 1, Calc.Max(1, Chain.LinkCount - 1));
        public int MinMaceIndex => Calc.Clamp(_setup.MinMaceIndex, 1, MaxMaceIndex);

        /// <summary>The mace's link: the chain's live end.</summary>
        public int MaceIndex => Calc.Clamp(_maceIndex, MinMaceIndex, MaxMaceIndex);

        public Vec2 MacePosition => Chain.GetPosition(MaceIndex);
        public Vec2 MaceVelocity => Chain.GetVelocity(MaceIndex);

        /// <summary>Where the angel's body is (flight position plus the hover bob): what enemies seek and touch.</summary>
        public Vec2 AngelPosition => Flight.RenderPosition;

        /// <summary>Chain.vb's followMouse: while false the chain hangs slack and the mace neither hits nor catches.</summary>
        public bool IsFollowing => Chain.IsFollowing;

        /// <summary>The enemy the grapple holds, or null.</summary>
        public EnemyEntity HeldEnemy => Grapple.Target as EnemyEntity;

        /// <summary>
        /// Whether Form1.vb's U has put the gems on this chain (<see cref="SkinLinks"/>). Off at the start; nothing
        /// takes it off again: no reset, no retune, no second U.
        /// </summary>
        public bool HasGemSkins { get; private set; }

        /// <summary>
        /// The skin link <paramref name="link"/> is drawn with (<see cref="LinkSkins.Skin"/>), or null when it is not
        /// drawn as a link: the mace's link, which stays the mace whatever skin it holds (BIBLE V25), and the parked
        /// links past it. A gem stays on its link index, so a link the mace moves off shows its own skin again.
        /// </summary>
        public string LinkSkin(int link)
        {
            if (link < 0 || link >= Chain.ActiveLinkCount || link == MaceIndex)
            {
                return null;
            }

            return _setup.Skins.Skin(link, HasGemSkins);
        }

        /// <summary>
        /// What the mace is drawn as (Chain.vb's <c>maceMode</c>): the setup's mode at the start, then whatever the
        /// <see cref="ToggleMaceMode"/> debug key last chose. No reset or retune changes it (Root.vb's Reset never
        /// touches <c>maceMode</c>), and nothing in the rules reads it: the hit, catch and throw are the same whatever
        /// the mace wears.
        /// </summary>
        public MaceMode MaceMode { get; private set; }

        /// <summary>The debug key: the mace puts on the next skin (<see cref="MaceModes.Next"/>). Returns the new one.</summary>
        internal MaceMode NextMaceMode() => MaceMode = MaceModes.Next(MaceMode);

        /// <summary>Form1.vb's U: puts the gems on. Returns false if they were already on.</summary>
        internal bool PutOnGems()
        {
            if (HasGemSkins)
            {
                return false;
            }

            HasGemSkins = true;
            return true;
        }

        /// <summary>
        /// Retunes the player (live inspector tweaks, a resized view) without disturbing their state.
        /// A different link count rebuilds the chain at the hand.
        /// </summary>
        internal void Configure(PlayerSetup setup)
        {
            var rebuild = setup.LinkCount != _setup.LinkCount;
            var live = Chain.Config;
            _setup = Sanitize(setup);
            Flight.Config = _setup.Flight;
            Grapple.ThrowMultiplier = _setup.ThrowMultiplier;
            if (rebuild)
            {
                var following = Chain.IsFollowing;
                Chain = new ChainSimulation(_setup.LinkCount, Flight.HandSocket, _setup.Chain) { IsFollowing = following };
                PreviousMaceIndex = -1;
            }

            // The live-tuning keys' figures are play state, not setup: a retune keeps them until a reset.
            Chain.Config = _tuned
                ? _setup.Chain.WithLiveTuning(live.Resistance, live.Gravity, live.SpringConstant)
                : _setup.Chain;
            Chain.WallBounce = _setup.WallBounce;
            ApplyMace();
        }

        /// <summary>
        /// Form1.vb's live-tuning keys: moves the chain's <paramref name="knob"/> by <paramref name="steps"/> of its
        /// step, held to its limits (<see cref="PlayerSetup.Tuning"/>). Returns true if the figure moved.
        /// </summary>
        internal bool Tune(ChainKnob knob, int steps)
        {
            var before = ChainTuning.Read(Chain.Config, knob);
            var tuned = _setup.Tuning.Apply(Chain.Config, knob, steps);
            if (ChainTuning.Read(tuned, knob) == before)
            {
                return false;
            }

            Chain.Config = tuned;
            _tuned = true;
            return true;
        }

        /// <summary>Sets the mace link, clamped to the chain. Returns true if it moved.</summary>
        internal bool SetMaceIndex(int index)
        {
            var before = MaceIndex;
            _maceIndex = Calc.Clamp(index, MinMaceIndex, MaxMaceIndex);
            ApplyMace();
            return MaceIndex != before;
        }

        /// <summary>
        /// Back to the start (a reset): angel home, chain collapsed on the hand, grapple idle. The chain
        /// length, the follow flag, the gem skins and the mace's skin are kept (Root.vb's Reset touches none of them).
        /// </summary>
        internal void Restart()
        {
            var following = Chain != null ? Chain.IsFollowing : _setup.Following;
            Flight = new AngelFlight(_setup.Start, _setup.Flight);
            Chain = new ChainSimulation(_setup.LinkCount, Flight.HandSocket, _setup.Chain)
            {
                WallBounce = _setup.WallBounce,
                IsFollowing = following,
            };
            Grapple = new Grapple(_setup.ThrowMultiplier);
            _tuned = false;
            // ButtonHeld is kept: it is the player's physical button, not round state. Input.vb's getMouse reads the button
            // every frame, so one still held through a reset arms the fresh grapple on the next step; clearing it here left
            // the seat's input (which only sends a change) and the world disagreeing until the button was pressed again.
            PressedSinceStep = false;
            PreviousMaceIndex = -1;
            ApplyMace();
        }

        /// <summary>The live chain ends at the mace link, which carries the mace's mass; every other link has mass 1.</summary>
        private void ApplyMace()
        {
            var index = MaceIndex;
            Chain.ActiveLinkCount = index + 1;
            for (var i = 1; i < Chain.LinkCount; i++)
            {
                Chain.SetMass(i, i == index ? _setup.MaceMass : 1f);
            }
        }

        private static PlayerSetup Sanitize(PlayerSetup setup)
        {
            setup.LinkCount = Calc.Max(2, setup.LinkCount);
            setup.MaxMaceIndex = Calc.Max(1, setup.MaxMaceIndex);
            setup.MinMaceIndex = Calc.Clamp(setup.MinMaceIndex, 1, setup.MaxMaceIndex);
            setup.MaceMass = Calc.Max(0.01f, setup.MaceMass);
            setup.HitRadius = Calc.Max(0f, setup.HitRadius);
            setup.DamagePerSpeed = Calc.Max(0f, setup.DamagePerSpeed);
            setup.CatchRadius = Calc.Max(0f, setup.CatchRadius);
            setup.ThrowMultiplier = Calc.Max(0f, setup.ThrowMultiplier);
            setup.ContactRadius = Calc.Max(0f, setup.ContactRadius);
            setup.WallBounce = Calc.Clamp01(setup.WallBounce);
            setup.Skins = setup.Skins ?? LinkSkins.Plain;
            setup.MaceMode = Enum.IsDefined(typeof(MaceMode), setup.MaceMode) ? setup.MaceMode : MaceMode.SpikeBall;
            setup.DamageFormula = Enum.IsDefined(typeof(MaceDamageFormula), setup.DamageFormula) ? setup.DamageFormula : MaceDamageFormula.Original;
            return setup;
        }
    }
}
