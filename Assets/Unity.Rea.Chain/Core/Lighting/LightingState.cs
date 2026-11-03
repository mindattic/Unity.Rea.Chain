using System;

namespace MindAttic.Chain.Core
{
    /// <summary>The four periods of Lights.vb's day (<c>periodOfDay</c>).</summary>
    public enum DayPeriod
    {
        Day,
        Dusk,
        Night,
        Morning,
    }

    /// <summary>One of the material tint's channels (Root.vb's <c>r</c>, <c>g</c>, <c>b</c>).</summary>
    public enum TintChannel
    {
        Red,
        Green,
        Blue,
    }

    /// <summary>A light colour, each channel 0-1.</summary>
    public readonly struct Rgb : IEquatable<Rgb>
    {
        public static readonly Rgb White = new Rgb(1f, 1f, 1f);

        public Rgb(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
        }

        public float R { get; }
        public float G { get; }
        public float B { get; }

        public bool Equals(Rgb other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object obj) => obj is Rgb other && Equals(other);
        public override int GetHashCode() => (R.GetHashCode() * 397 ^ G.GetHashCode()) * 397 ^ B.GetHashCode();
        public override string ToString() => $"({R:0.###}, {G:0.###}, {B:0.###})";
    }

    /// <summary>
    /// Lights.vb's <c>renderLight</c> as a pure rule: the material tint (<c>r</c>, <c>g</c>, <c>b</c>,
    /// Root.vb, 255 each) that the Day, Dusk, Night, Morning cycle walks, one unit a frame:
    /// <list type="bullet">
    /// <item>Day: the tint holds; after <see cref="Settings.DayIncrement"/> it turns to Dusk.</item>
    /// <item>Dusk: green and blue fall to 0 (a red dusk); once blue is down to 128 and
    /// <see cref="Settings.DuskIncrement"/> has passed, Night.</item>
    /// <item>Night: red and green fall, blue rises to 255 (a blue night); once blue is full and
    /// <see cref="Settings.NightIncrement"/> has passed, the date moves on a day and it is Morning.</item>
    /// <item>Morning: red and green rise back to 255 (white); once green is full and
    /// <see cref="Settings.MorningIncrement"/> has passed, Day again.</item>
    /// </list>
    /// The scene is lit by the ambient colour (the game-over fade, <see cref="GameState.Ambient"/>)
    /// times this tint, as D3D lit a material's ambient by the render state's (see <see cref="LightColour"/>).
    /// <see cref="Enabled"/> is <c>bLighting</c>, which L toggles and a new round turns back on.
    ///
    /// The day length is the original's (BIBLE D6, settled by U.CHN-T82): <c>dayLength</c> 1e10 s, so the cycle
    /// stays in Day all session; only a script (RUN NIGHT, CHANGE TIME DAYPERIOD) moves the period on. The
    /// 24-minute day of UNI-T46 is <see cref="Settings.TwentyFourMinuteDay"/>, a data option only
    /// (spike U.CHN-T91 judged it plays worse: about half of lit play in a bare red or blue tint). One fix (BIBLE D16): with lighting off the original
    /// just stopped updating the device, freezing whatever colour was last set; here off means unlit
    /// (no tint), the cycle holding where it is until lighting comes back on, as renderLight skipped it.
    /// </summary>
    public sealed class LightingState
    {
        public struct Settings
        {
            /// <summary>
            /// Seconds in one full Day, Dusk, Night, Morning cycle (Lights.vb's <c>dayLength</c>). The default
            /// is the original's 1e10 s, which never leaves Day (D6). 1440 s, a 24-hour day at
            /// <c>renderTime</c>'s one hour a minute, is the option (<see cref="TwentyFourMinuteDay"/>).
            /// </summary>
            public float DayLength;

            /// <summary>Tint units (of 255) a channel moves per second: Lights.vb steps 1 a frame, 60 at the 60 fps step (D9).</summary>
            public float TintRate;

            /// <summary>Day lasts a third of the day length (Lights.vb's <c>dayIncrement</c>).</summary>
            public float DayIncrement => DayLength / 3f;
            /// <summary>Dusk lasts half a Day (<c>duskIncrement</c>).</summary>
            public float DuskIncrement => DayIncrement / 2f;
            /// <summary>Night lasts a third of the day length (<c>nightIncrement</c>).</summary>
            public float NightIncrement => DayLength / 3f;
            /// <summary>Morning lasts half a Night (<c>morningIncrement</c>).</summary>
            public float MorningIncrement => NightIncrement / 2f;

            /// <summary>The original's figures (the default): a 1e10 s day that never moves on.</summary>
            public static Settings Default => new Settings
            {
                DayLength = 1e10f,
                TintRate = 60f,
            };

            /// <summary>UNI-T46's option, never the default: a 24-minute day (8 min day, 4 dusk, 8 night, 4 morning).</summary>
            public static Settings TwentyFourMinuteDay => new Settings
            {
                DayLength = 1440f,
                TintRate = 60f,
            };
        }

        /// <summary>A tint channel at full (Root.vb: <c>r = g = b = 255</c>).</summary>
        public const float FullTint = 255f;
        /// <summary>Dusk ends only once blue is no brighter than this (Lights.vb: <c>b &lt;= 128</c>).</summary>
        public const float DuskBlue = 128f;

        public LightingState() : this(Settings.Default)
        {
        }

        public LightingState(Settings settings)
        {
            Config = settings;
            Red = Green = Blue = FullTint;
        }

        public Settings Config;

        /// <summary>The tint channels, 0-255 (Root.vb's <c>r</c>, <c>g</c>, <c>b</c>).</summary>
        public float Red { get; private set; }
        public float Green { get; private set; }
        public float Blue { get; private set; }

        /// <summary>The tint as 0-1.</summary>
        public Rgb Tint => new Rgb(Red / FullTint, Green / FullTint, Blue / FullTint);

        /// <summary>Lights.vb's <c>dayPeriod</c>; the game starts at Day.</summary>
        public DayPeriod Period { get; private set; } = DayPeriod.Day;

        /// <summary>Seconds of lit play so far (the clock the periods are timed on).</summary>
        public float Clock { get; private set; }

        /// <summary>When the current period began, on <see cref="Clock"/> (Lights.vb's <c>lastTimeCheck</c>).</summary>
        public float PeriodStart { get; private set; }

        /// <summary>Days the date has moved on (Lights.vb: <c>theDate.AddDays(1.0)</c> as Night ends).</summary>
        public int DaysPassed { get; private set; }

        /// <summary>Root.vb's <c>bLighting</c>: on by default, L toggles it.</summary>
        public bool Enabled { get; private set; } = true;

        /// <summary>Raised with the new period whenever the cycle moves on (or is set).</summary>
        public event Action<DayPeriod> PeriodChanged;

        /// <summary>
        /// One frame of <c>renderLight</c>, <paramref name="deltaTime"/> seconds long. Does nothing while
        /// lighting is off. Call once per fixed step while play runs (pause skipped renderLight).
        /// </summary>
        public void Step(float deltaTime)
        {
            if (!Enabled || !(deltaTime > 0f))
            {
                return;
            }

            Clock += deltaTime;
            var step = Calc.Max(0f, Config.TintRate) * deltaTime;
            var elapsed = Clock - PeriodStart;

            switch (Period)
            {
                case DayPeriod.Day:
                    if (elapsed > Config.DayIncrement)
                    {
                        MoveTo(DayPeriod.Dusk);
                    }

                    break;

                case DayPeriod.Dusk:
                    Green = Down(Green, step);
                    Blue = Down(Blue, step);
                    if (Blue <= DuskBlue && elapsed > Config.DuskIncrement)
                    {
                        MoveTo(DayPeriod.Night);
                    }

                    break;

                case DayPeriod.Night:
                    Red = Down(Red, step);
                    Green = Down(Green, step);
                    Blue = Up(Blue, step);
                    if (Blue >= FullTint && elapsed > Config.NightIncrement)
                    {
                        DaysPassed++;
                        MoveTo(DayPeriod.Morning);
                    }

                    break;

                case DayPeriod.Morning:
                    Red = Up(Red, step);
                    Green = Up(Green, step);
                    if (Green >= FullTint && elapsed > Config.MorningIncrement)
                    {
                        MoveTo(DayPeriod.Day);
                    }

                    break;
            }
        }

        /// <summary>
        /// Lights.vb's <c>AmbientColor</c> as a script sets it (<c>CHANGE LIGHT AMBIENT</c>, U.CHN-T61): white until then,
        /// and white again each new round (Root.vb's Reset: <c>AmbientColor = Color.White</c>). The game-over fade, which
        /// the original also wrote into it, is the ambient level <see cref="LightColour"/> takes.
        /// </summary>
        public Rgb24 Ambient { get; private set; } = Rgb24.White;

        /// <summary>Script.vb's <c>CHANGE LIGHT AMBIENT</c>: the ambient colour the scene is lit with from now on.</summary>
        public void SetAmbient(Rgb24 colour) => Ambient = colour;

        /// <summary>
        /// The colour the scene is lit with: the ambient level (0-1, the game-over fade) times the ambient colour
        /// (<see cref="Ambient"/>, white unless a script set it) times the tint while lighting is on, or the bare
        /// ambient while it is off.
        /// </summary>
        public Rgb LightColour(float ambientLevel)
        {
            var level = Calc.Clamp01(ambientLevel);
            var tint = Enabled ? Tint : Rgb.White;
            var ambient = Ambient.ToRgb();
            return new Rgb(tint.R * ambient.R * level, tint.G * ambient.G * level, tint.B * ambient.B * level);
        }

        /// <summary>L: lighting on or off (Form1.vb <c>bLighting = Not bLighting</c>). Returns the new state.</summary>
        public bool ToggleEnabled() => SetEnabled(!Enabled);

        public bool SetEnabled(bool enabled)
        {
            Enabled = enabled;
            return Enabled;
        }

        /// <summary>
        /// Jumps to <paramref name="period"/> with its timer restarted, leaving the tint to walk there
        /// (Script.vb's <c>TIME DAYPERIOD</c>). The tint is not snapped: the original only set the period.
        /// </summary>
        public void SetPeriod(DayPeriod period) => MoveTo(period);

        /// <summary>
        /// Sets one tint channel, held to 0-255 (Script.vb's <c>CHANGE LIGHT RED|GREEN|BLUE</c>; Root.vb's channels are
        /// bytes). The cycle walks on from there.
        /// </summary>
        public void SetTint(TintChannel channel, float value)
        {
            var v = Calc.Clamp(value, 0f, FullTint);
            switch (channel)
            {
                case TintChannel.Red: Red = v; break;
                case TintChannel.Green: Green = v; break;
                default: Blue = v; break;
            }
        }

        /// <summary>
        /// A new round (Root.vb's <c>Reset</c>): lighting back on and the ambient colour white again. The time
        /// of day carries on, as the original's Reset left <c>dayPeriod</c> and the tint alone; its <c>b = True</c>
        /// line (a stray that set blue to 255 mid-cycle) is not ported (D16).
        /// </summary>
        public void NewRound()
        {
            Enabled = true;
            Ambient = Rgb24.White;
        }

        private void MoveTo(DayPeriod period)
        {
            Period = period;
            PeriodStart = Clock;
            PeriodChanged?.Invoke(period);
        }

        private static float Down(float channel, float step) => Calc.Max(0f, channel - step);

        private static float Up(float channel, float step) => Calc.Min(FullTint, channel + step);
    }
}
