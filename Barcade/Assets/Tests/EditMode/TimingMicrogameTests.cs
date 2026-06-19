using System.Collections.Generic;
using NUnit.Framework;
using Barcade.Core;

namespace Barcade.Core.Tests
{
    /// <summary>
    /// TASK-012 — Timing (Press on cue) microgame pure logic.
    ///
    /// Marker moves from 0..1 at constant speed: position = (speed * elapsed) % 1.
    /// Target zone: [zoneMin, zoneMax].
    /// On Button.Pressed: WIN if marker within zone (latch win), LOSS if outside (latch miss).
    /// No press by end → loss.
    /// Boundary pinned:
    ///   - Marker at exactly zoneMin → WIN (inclusive).
    ///   - Marker at exactly zoneMax → WIN (inclusive).
    ///   - Marker just below zoneMin → LOSS.
    ///   - Marker just above zoneMax → LOSS.
    /// </summary>
    [TestFixture]
    public class TimingMicrogameTests
    {
        private static readonly PlayerSlot[] AllPlayers =
        {
            PlayerSlot.Rojo, PlayerSlot.Azul, PlayerSlot.Amarillo, PlayerSlot.Verde
        };

        private static IMicrogameContext MakeContext(int seed = 0, float difficulty = 0f)
            => new TimingTestContext(new SeededRandom(seed), AllPlayers, difficulty);

        private static IReadOnlyPlayerInputs AllPressed()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Pressed);
            return new TimingInputs(map);
        }

        private static IReadOnlyPlayerInputs NoInput()
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f, ButtonState.Released);
            return new TimingInputs(map);
        }

        private static IReadOnlyPlayerInputs OnlyOnePressed(PlayerSlot who)
        {
            var map = new Dictionary<PlayerSlot, InputSnapshot>();
            foreach (PlayerSlot s in AllPlayers)
                map[s] = new InputSnapshot(0f, 0f,
                    s == who ? ButtonState.Pressed : ButtonState.Released);
            return new TimingInputs(map);
        }

        // ── CommandVerb ──────────────────────────────────────────────────────────

        [Test]
        public void TimingMicrogame_CommandVerb_IsAhora()
        {
            var game = new TimingMicrogame(speed: 0.2f, zoneMin: 0.4f, zoneMax: 0.6f);
            Assert.That(game.CommandVerb, Is.EqualTo("¡AHORA!"));
        }

        // ── Win: press when marker inside zone ───────────────────────────────────

        [Test]
        public void TimingMicrogame_PressWhenMarkerInZone_AllWin()
        {
            // speed=0.5, zone=[0.4, 0.6].
            // After 1 second: position = 0.5 * 1.0 = 0.5 → inside zone → WIN.
            var game = new TimingMicrogame(speed: 0.5f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(1.0f, AllPressed()); // marker at 0.5 → inside zone

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Press in zone → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.True,  "Press in zone → win");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.True,  "Press in zone → win");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.True,  "Press in zone → win");
        }

        // ── Loss: press when marker outside zone ─────────────────────────────────

        [Test]
        public void TimingMicrogame_PressWhenMarkerOutsideZone_AllLose()
        {
            // speed=0.5, zone=[0.4, 0.6].
            // After 0.1 seconds: position = 0.5 * 0.1 = 0.05 → outside zone → MISS.
            var game = new TimingMicrogame(speed: 0.5f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.1f, AllPressed()); // marker at 0.05 → outside zone

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "Press outside zone → loss");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Press outside zone → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Press outside zone → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Press outside zone → loss");
        }

        // ── No press → all lose ──────────────────────────────────────────────────

        [Test]
        public void TimingMicrogame_NoPress_AllLose()
        {
            var game = new TimingMicrogame(speed: 0.5f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.5f, NoInput());
            game.Tick(0.5f, NoInput());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.False, "No press → loss");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "No press → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "No press → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "No press → loss");
        }

        // ── Boundary: marker exactly at zoneMin → WIN ────────────────────────────

        [Test]
        public void TimingMicrogame_MarkerAtExactZoneMin_IsWin()
        {
            // speed=1, zone=[0.4, 0.6].
            // After exactly 0.4 seconds: position = 1.0 * 0.4 = 0.4 → exactly zoneMin → WIN.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.4f, AllPressed()); // marker at exactly 0.4

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Marker exactly at zoneMin must be a win (inclusive boundary)");
        }

        // ── Boundary: marker exactly at zoneMax → WIN ────────────────────────────

        [Test]
        public void TimingMicrogame_MarkerAtExactZoneMax_IsWin()
        {
            // speed=1, zone=[0.4, 0.6].
            // After 0.6 seconds: position = 0.6 → exactly zoneMax → WIN.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.6f, AllPressed()); // marker at exactly 0.6

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Marker exactly at zoneMax must be a win (inclusive boundary)");
        }

        // ── Boundary: marker just below zoneMin → MISS ───────────────────────────

        [Test]
        public void TimingMicrogame_MarkerJustBelowZoneMin_IsMiss()
        {
            // speed=1, zone=[0.4, 0.6].
            // 0.399 seconds: marker at 0.399 < 0.4 → outside zone → MISS.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.399f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Marker just below zoneMin must be a miss");
        }

        // ── Boundary: marker just above zoneMax → MISS ───────────────────────────

        [Test]
        public void TimingMicrogame_MarkerJustAboveZoneMax_IsMiss()
        {
            // speed=1, zone=[0.4, 0.6].
            // 0.601 seconds: marker at 0.601 > 0.6 → outside zone → MISS.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.601f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Marker just above zoneMax must be a miss");
        }

        // ── First decision is latched: win latched ───────────────────────────────

        [Test]
        public void TimingMicrogame_WinLatched_SecondPressDoesNotChangeLatchedWin()
        {
            // Win on first press, then press again in bad zone — still a win.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            // Tick 1: marker at 0.5 → inside zone → WIN latched.
            game.Tick(0.5f, AllPressed());

            // Tick 2: marker at 0.5 + whatever → press again; but already latched.
            game.Tick(0.5f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Win must be latched — second press cannot change outcome");
        }

        // ── First decision is latched: miss latched ──────────────────────────────

        [Test]
        public void TimingMicrogame_MissLatched_SecondPressInZoneDoesNotWin()
        {
            // Miss on first press (outside zone), then second press inside zone — still a loss.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            // Tick 1: marker at 0.1 → outside zone → MISS latched.
            game.Tick(0.1f, AllPressed());

            // Tick 2: marker at 0.5 → inside zone → but already missed.
            game.Tick(0.4f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.False,
                "Miss must be latched — subsequent press in zone must not convert to win");
        }

        // ── Independent per-player latching ─────────────────────────────────────

        [Test]
        public void TimingMicrogame_OnlyRojoPressesInZone_OnlyRojoWins()
        {
            // speed=1, zone=[0.4, 0.6]. At t=0.5 Rojo presses (in zone); others never press.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.5f, OnlyOnePressed(PlayerSlot.Rojo)); // Rojo hits zone

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo),     Is.True,  "Rojo pressed in zone → win");
            Assert.That(result.IsWin(PlayerSlot.Azul),     Is.False, "Azul never pressed → loss");
            Assert.That(result.IsWin(PlayerSlot.Amarillo), Is.False, "Amarillo never pressed → loss");
            Assert.That(result.IsWin(PlayerSlot.Verde),    Is.False, "Verde never pressed → loss");
        }

        // ── Marker wraps mod 1 ───────────────────────────────────────────────────

        [Test]
        public void TimingMicrogame_MarkerWrapsModOne()
        {
            // speed=1, elapsed=1.5 → position = 1.5 % 1 = 0.5.
            // zone=[0.4, 0.6] → in zone → WIN.
            var game = new TimingMicrogame(speed: 1.0f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(1.5f, AllPressed()); // wraps to 0.5

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsWin(PlayerSlot.Rojo), Is.True,
                "Marker wraps mod 1; 1.5 wraps to 0.5 which is in zone");
        }

        // ── Difficulty scaling ───────────────────────────────────────────────────

        [Test]
        public void TimingMicrogame_Difficulty0_HasBaseSpeedAndZone()
        {
            // difficulty=0: speed stays base; zone stays [baseMin, baseMax].
            const float baseSpeed = 1f;
            const float baseMin   = 0.4f;
            const float baseMax   = 0.6f;
            var game = new TimingMicrogame(speed: baseSpeed, zoneMin: baseMin, zoneMax: baseMax);
            game.Prepare(MakeContext(seed: 0, difficulty: 0f));

            Assert.That(game.EffectiveSpeed,   Is.EqualTo(baseSpeed).Within(0.001f));
            Assert.That(game.EffectiveZoneMin, Is.EqualTo(baseMin).Within(0.001f));
            Assert.That(game.EffectiveZoneMax, Is.EqualTo(baseMax).Within(0.001f));
        }

        [Test]
        public void TimingMicrogame_Difficulty1_IncreasesSpeedAndNarrowsZone()
        {
            const float baseSpeed = 1f;
            const float baseMin   = 0.4f;
            const float baseMax   = 0.6f;
            var game = new TimingMicrogame(speed: baseSpeed, zoneMin: baseMin, zoneMax: baseMax);
            game.Prepare(MakeContext(seed: 0, difficulty: 1f));

            // speed = base*(1+1) = 2; zone half-width = 0.1*(1-0.5)=0.05; zone=[0.45,0.55].
            Assert.That(game.EffectiveSpeed, Is.EqualTo(2f).Within(0.001f),
                "Difficulty 1 must double the marker speed");
            Assert.That(game.EffectiveZoneMax - game.EffectiveZoneMin,
                Is.LessThan(baseMax - baseMin),
                "Difficulty 1 must narrow the target zone");
        }

        [Test]
        public void TimingMicrogame_LowDifficultyWins_HighDifficultyLoses()
        {
            // Zone at difficulty 0: [0.4, 0.6]. Press at t=0.5s → marker at 0.5 → in zone → WIN.
            // Zone at difficulty 1: centre=0.5, half-width=0.1*(1-0.5)=0.05 → [0.45,0.55].
            //   Speed=2, press at t=0.5s → marker=(2*0.5)%1=0.0 → outside [0.45,0.55] → LOSS.
            // (marker wraps: 2*0.5=1.0, mod1=0.0, outside zone)
            const float baseSpeed = 1f;
            const float baseMin   = 0.4f;
            const float baseMax   = 0.6f;

            var gameEasy = new TimingMicrogame(speed: baseSpeed, zoneMin: baseMin, zoneMax: baseMax);
            gameEasy.Prepare(MakeContext(seed: 0, difficulty: 0f));
            gameEasy.Tick(0.5f, AllPressed()); // marker at 0.5 → in [0.4, 0.6]
            var easyResult = gameEasy.Evaluate();
            gameEasy.Cleanup();

            var gameHard = new TimingMicrogame(speed: baseSpeed, zoneMin: baseMin, zoneMax: baseMax);
            gameHard.Prepare(MakeContext(seed: 0, difficulty: 1f));
            gameHard.Tick(0.5f, AllPressed()); // speed=2 → marker=(2*0.5)%1=0.0 → outside [0.45,0.55]
            var hardResult = gameHard.Evaluate();
            gameHard.Cleanup();

            Assert.That(easyResult.IsWin(PlayerSlot.Rojo), Is.True,
                "Press at t=0.5s in easy mode (marker 0.5 in [0.4,0.6]) → WIN");
            Assert.That(hardResult.IsWin(PlayerSlot.Rojo), Is.False,
                "Press at t=0.5s in hard mode (marker wraps to 0.0, outside [0.45,0.55]) → LOSS");
        }

        // ── GetMarkerPosition reflects current state ─────────────────────────────

        [Test]
        public void TimingMicrogame_GetMarkerPosition_ReflectsElapsed()
        {
            var game = new TimingMicrogame(speed: 0.5f, zoneMin: 0.3f, zoneMax: 0.7f);
            game.Prepare(MakeContext(seed: 0));

            game.Tick(0.6f, NoInput()); // marker should be at 0.5 * 0.6 = 0.3

            float pos = game.GetMarkerPosition();
            Assert.That(pos, Is.EqualTo(0.3f).Within(0.0001f),
                "GetMarkerPosition must return speed * elapsed mod 1");

            game.Evaluate();
            game.Cleanup();
        }

        // ── Evaluate returns frozen result ───────────────────────────────────────

        [Test]
        public void TimingMicrogame_Evaluate_ReturnsFrozenResult()
        {
            var game = new TimingMicrogame(speed: 1f, zoneMin: 0.4f, zoneMax: 0.6f);
            game.Prepare(MakeContext(seed: 0));
            game.Tick(0.5f, AllPressed());

            MicrogameResult result = game.Evaluate();
            game.Cleanup();

            Assert.That(result.IsFrozen, Is.True);
        }
    }

    // ── Test-only helpers ────────────────────────────────────────────────────────

    internal sealed class TimingTestContext : IMicrogameContext
    {
        public ISeededRandom Rng { get; }
        public PlayerSlot[] Players { get; }
        public float Difficulty { get; }
        public TimingTestContext(ISeededRandom rng, PlayerSlot[] players, float difficulty = 0f)
        {
            Rng        = rng;
            Players    = players;
            Difficulty = difficulty;
        }
    }

    internal sealed class TimingInputs : IReadOnlyPlayerInputs
    {
        private readonly Dictionary<PlayerSlot, InputSnapshot> _map;
        public TimingInputs(Dictionary<PlayerSlot, InputSnapshot> map) { _map = map; }
        public InputSnapshot For(PlayerSlot slot) => _map[slot];
    }
}
