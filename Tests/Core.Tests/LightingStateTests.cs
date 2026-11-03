using System.Collections.Generic;
using MindAttic.Chain.Core;
using NUnit.Framework;

namespace MindAttic.Chain.Tests
{
    public class LightingStateTests
    {
        private const float Step = 1f / 60f;

        private static void Run(LightingState state, float seconds)
        {
            var steps = (int)(seconds / Step + 0.5f);
            for (var i = 0; i < steps; i++)
            {
                state.Step(Step);
            }
        }

        [Test]
        public void StartsAtDay_WhiteTint_LightingOn()
        {
            var state = new LightingState();
            Assert.AreEqual(DayPeriod.Day, state.Period);
            Assert.AreEqual(Rgb.White, state.Tint, "Root.vb r = g = b = 255");
            Assert.IsTrue(state.Enabled, "Root.vb bLighting = True");
        }

        [Test]
        public void TwentyFourMinuteDay_Is1440Seconds_SplitAsLightsVb()
        {
            var settings = LightingState.Settings.TwentyFourMinuteDay;
            Assert.AreEqual(1440f, settings.DayLength, "a 24-hour day at renderTime's one hour a minute");
            Assert.AreEqual(480f, settings.DayIncrement, 1e-3f, "dayLength / 3");
            Assert.AreEqual(240f, settings.DuskIncrement, 1e-3f, "dayIncrement / 2");
            Assert.AreEqual(480f, settings.NightIncrement, 1e-3f, "dayLength / 3");
            Assert.AreEqual(240f, settings.MorningIncrement, 1e-3f, "nightIncrement / 2");
            Assert.AreEqual(1e10f, LightingState.Settings.Default.DayLength, "the original's figure is the default (U.CHN-T82)");
        }

        [Test]
        public void TheCycleAdvances_DayDuskNightMorningDay()
        {
            var state = new LightingState(LightingState.Settings.TwentyFourMinuteDay);
            var seen = new List<DayPeriod>();
            state.PeriodChanged += seen.Add;

            Run(state, 1441f);

            Assert.That(seen, Is.EqualTo(
                new[] { DayPeriod.Dusk, DayPeriod.Night, DayPeriod.Morning, DayPeriod.Day }));
            Assert.AreEqual(1, state.DaysPassed, "the date moves on as Night ends");
            Assert.AreEqual(Rgb.White, state.Tint, "morning brings the tint back to white");
        }

        [Test]
        public void Spike_TwentyFourMinuteDay_LightsHalfOfPlayInASingleChannel()
        {
            // U.CHN-T91 (BIBLE D6): the evidence behind keeping the original's still day. Over one 1440 s cycle the
            // tint is a bare red (Dusk) or bare blue (Night) for all but the 4.25 s ramps, so roughly half of lit play
            // shows only one colour channel, and the zone overlay's 0, 128, 255 goes black under the red Dusk.
            var state = new LightingState(LightingState.Settings.TwentyFourMinuteDay);
            var steps = (int)(1440f / Step + 0.5f);
            var singleChannel = 0;
            var zoneBlackAtDusk = false;
            for (var i = 0; i < steps; i++)
            {
                state.Step(Step);
                var zeros = (state.Red == 0f ? 1 : 0) + (state.Green == 0f ? 1 : 0) + (state.Blue == 0f ? 1 : 0);
                if (zeros == 2)
                {
                    singleChannel++;
                }

                if (state.Period == DayPeriod.Dusk && zeros == 2)
                {
                    var tint = state.Tint;
                    zoneBlackAtDusk = 0f * tint.R + (128f / 255f) * tint.G + 1f * tint.B == 0f;
                }
            }

            var share = singleChannel * Step / 1440f;
            Assert.That(share, Is.InRange(0.45f, 0.5f), "Dusk (~236 s red) plus Night (~476 s blue) of a 1440 s day");
            Assert.IsTrue(zoneBlackAtDusk, "the zone colour 0, 128, 255 has no red, so a red Dusk lights it black");
        }

        [Test]
        public void TheOriginalDayLength_NeverLeavesDay()
        {
            var state = new LightingState();
            Run(state, 3600f);
            Assert.AreEqual(DayPeriod.Day, state.Period, "D6: with 1e10 s the cycle is frozen");
        }

        [Test]
        public void Dusk_FadesGreenAndBlue_OneUnitAFrame_ToARedDusk()
        {
            var state = new LightingState(LightingState.Settings.TwentyFourMinuteDay);
            Run(state, 480.5f);
            Assert.AreEqual(DayPeriod.Dusk, state.Period);

            var green = state.Green;
            state.Step(Step);
            Assert.AreEqual(green - 1f, state.Green, 1e-3f, "Lights.vb: g -= 1 a frame");

            Run(state, 10f);
            Assert.AreEqual(255f, state.Red);
            Assert.AreEqual(0f, state.Green);
            Assert.AreEqual(0f, state.Blue);
        }

        [Test]
        public void Night_IsBlue_Morning_IsWhite()
        {
            var state = new LightingState(LightingState.Settings.TwentyFourMinuteDay);
            Run(state, 480.5f + 240.5f + 10f);
            Assert.AreEqual(DayPeriod.Night, state.Period);
            Assert.AreEqual(new Rgb(0f, 0f, 1f), state.Tint);

            Run(state, 480f + 10f);
            Assert.AreEqual(DayPeriod.Morning, state.Period);
            Assert.AreEqual(Rgb.White, state.Tint);
        }

        [Test]
        public void Dusk_WaitsForBlueToReach128_EvenPastItsTime()
        {
            var state = new LightingState(new LightingState.Settings { DayLength = 6f, TintRate = 60f });
            Run(state, 2.1f); // Day is 2 s
            Assert.AreEqual(DayPeriod.Dusk, state.Period);

            Run(state, 1.5f); // past Dusk's 1 s, but blue is still above 128 (127 frames to fall)
            Assert.AreEqual(DayPeriod.Dusk, state.Period);
            Assert.Greater(state.Blue, LightingState.DuskBlue);

            Run(state, 1f);
            Assert.AreEqual(DayPeriod.Night, state.Period);
        }

        [Test]
        public void LightColour_IsAmbientTimesTint()
        {
            var state = new LightingState();
            state.SetPeriod(DayPeriod.Night);
            Run(state, 5f);

            var lit = state.LightColour(0.5f);
            Assert.AreEqual(0f, lit.R);
            Assert.AreEqual(0f, lit.G);
            Assert.AreEqual(0.5f, lit.B, 1e-4f);
        }

        [Test]
        public void LightingOff_IsUnlit_AndHoldsTheCycle()
        {
            var state = new LightingState();
            state.SetPeriod(DayPeriod.Dusk);
            Run(state, 1f);
            var tint = state.Tint;

            Assert.IsFalse(state.ToggleEnabled(), "L turns lighting off");
            Assert.AreEqual(Rgb.White, state.LightColour(1f), "off is unlit, not frozen on the last colour (D16)");
            Assert.AreEqual(new Rgb(0.25f, 0.25f, 0.25f), state.LightColour(0.25f), "the game-over fade still shows");

            Run(state, 600f);
            Assert.AreEqual(DayPeriod.Dusk, state.Period, "renderLight is skipped while off");
            Assert.AreEqual(tint, state.Tint);

            Assert.IsTrue(state.ToggleEnabled());
        }

        [Test]
        public void NewRound_TurnsLightingBackOn_AndKeepsTheTimeOfDay()
        {
            var state = new LightingState(LightingState.Settings.TwentyFourMinuteDay);
            Run(state, 490f);
            state.ToggleEnabled();

            state.NewRound();

            Assert.IsTrue(state.Enabled, "Root.vb Reset: bLighting = True");
            Assert.AreEqual(DayPeriod.Dusk, state.Period, "Reset left dayPeriod alone");
            Assert.AreEqual(0f, state.Blue, "the stray b = True is not ported (D16)");
        }
    }
}
