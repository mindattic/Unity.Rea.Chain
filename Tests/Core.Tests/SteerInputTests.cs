using MindAttic.Chain.Core;
using NUnit.Framework;

namespace MindAttic.Chain.Tests
{
    /// <summary>
    /// Input.vb's joystick, bound at last (UNI-T34, BIBLE D14): a stick pushed past the 250-in-1000 dead zone steers
    /// like the mouse delta, at the stick speed, and Form1.vb's J turns it off and on.
    /// </summary>
    public class SteerInputTests
    {
        private static SteerInput Make(bool enabled = true) => new SteerInput(new SteerInput.Settings(enabled, 0.25f, 600f));

        [Test]
        public void Axis_InsideTheDeadZone_IsStill()
        {
            Assert.AreEqual(0f, SteerInput.Axis(0.25f, 0.25f), "joyState.X <= -250 is the first that counts; 250 itself does not");
            Assert.AreEqual(0f, SteerInput.Axis(-0.2f, 0.25f));
        }

        [Test]
        public void Axis_RisesFromTheEdgeToFull()
        {
            Assert.AreEqual(0.5f, SteerInput.Axis(0.625f, 0.25f), 1e-6f);
            Assert.AreEqual(-1f, SteerInput.Axis(-1f, 0.25f), 1e-6f);
        }

        [Test]
        public void FullStick_SteersAtTheStickSpeed()
        {
            var steer = Make();

            var delta = steer.StickDelta(new Vec2(1f, -1f), 0.5f);

            Assert.AreEqual(300f, delta.X, 1e-4f, "600 counts a second for half a second");
            Assert.AreEqual(-300f, delta.Y, 1e-4f);
        }

        [Test]
        public void Combine_AddsTheStickToTheMouse()
        {
            var steer = Make();

            var delta = steer.Combine(new Vec2(10f, 5f), new Vec2(1f, 0f), 0.1f);

            Assert.AreEqual(70f, delta.X, 1e-4f);
            Assert.AreEqual(5f, delta.Y, 1e-4f);
        }

        [Test]
        public void J_TurnsTheStickOffAndOn()
        {
            var steer = Make();

            Assert.IsFalse(steer.ToggleJoystick());
            Assert.AreEqual(Vec2.Zero, steer.StickDelta(Vec2.One, 1f), "off, the stick steers nothing");
            Assert.AreEqual(new Vec2(3f, 4f), steer.Combine(new Vec2(3f, 4f), Vec2.One, 1f), "and the mouse still does");

            Assert.IsTrue(steer.ToggleJoystick());
            Assert.AreNotEqual(Vec2.Zero, steer.StickDelta(Vec2.One, 1f));
        }

        [Test]
        public void StartsAsBJoystickSays()
        {
            Assert.IsTrue(Make().JoystickEnabled);
            Assert.IsFalse(Make(enabled: false).JoystickEnabled);
        }

        [Test]
        public void SteeredStick_FliesTheAngelThroughTheWorld()
        {
            var settings = GameSettings.Load(new DirectorySettingsSource(GameSettingsTests.ShippedFolder));
            var world = new GameWorld(settings.World);
            var p0 = new PlayerId(0);
            world.AddPlayer(p0, settings.Player);
            var start = world.FindPlayer(p0).Flight.Position;
            var steer = new SteerInput(settings.Steering);

            // A short push, well inside Angel.vb's checkBounds (angel.json boundsX -0.6..0.5, U.CHN-T86): long
            // enough to prove the stick becomes distance the way the mouse does, short enough that AngelFlight's
            // faithful bounds clamp (which a half-second full push at the shipped stickSpeed reaches, U.CHN-T104)
            // never engages and masks what this test is actually checking.
            world.Submit(new SteerAngel(p0, steer.Combine(Vec2.Zero, new Vec2(1f, 0f), 0.1f), 0.1f));

            var moved = world.FindPlayer(p0).Flight.Position - start;
            Assert.AreEqual(60f * settings.Player.Flight.Sensitivity, moved.X, 1e-4f, "the stick's counts become distance as the mouse's do");
        }
    }
}
