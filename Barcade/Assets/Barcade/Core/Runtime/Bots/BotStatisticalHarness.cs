using System;
using Barcade.Core.Microgames.V2;
// Barcade.Core.Bots is lexically nested inside Barcade.Core, so an unqualified
// name resolves against Barcade.Core's own members before this file's own
// "using"-imported namespace — the same collision SessionStateMachineTests.cs
// documents for the same three names (IMicrogame/InputSnapshot/MicrogameResult
// exist in both Barcade.Core (v1) and Barcade.Core.Microgames.V2 (v2)).
// Renamed aliases sidestep it.
using V2Microgame = Barcade.Core.Microgames.V2.IMicrogame;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;
using V2Result = Barcade.Core.Microgames.V2.MicrogameResult;

namespace Barcade.Core.Bots
{
    /// <summary>
    /// GDD §16 "tests estadísticos... sobre 1000+ semillas" / Annex D.3: the
    /// executable machinery behind "bots calibrados" — drives a full mechanic
    /// round, tick by tick, with one <see cref="IBotPolicy"/> instance per
    /// active seat deciding real input from that seat's own <see cref="BotView"/>,
    /// then repeats across a seed batch. Generic over <see cref="IMicrogame"/>
    /// via <see cref="MicrogameFactory"/>/<see cref="BotPolicyFactory"/> — a
    /// future mechanic needs no change here, only its own factory delegates at
    /// the call site (AC4).
    ///
    /// <para>
    /// <b>RNG stream separation.</b> Each run derives TWO independent
    /// <see cref="SeededRandom"/> instances from the same integer seed: the
    /// mechanic's own (<c>new SeededRandom(seed)</c>, the DEFAULT stream — the
    /// exact same construction every existing mechanic test already uses) and
    /// the bots' own (<see cref="SeededRandom.Derive"/> on
    /// <see cref="RngStream.Bots"/>) — so bot decisions can never perturb (or be
    /// perturbed by) the mechanic's own spawner/RNG consumption, per GDD §13's
    /// "streams independientes por subsistema" rule <see cref="RngStream"/>
    /// already establishes for every other subsystem.
    /// </para>
    /// </summary>
    public static class BotStatisticalHarness
    {
        public delegate V2Microgame MicrogameFactory();

        /// <summary>Constructs a fresh <see cref="IBotPolicy"/> for the given seat index — fresh per run, since policies carry per-seat memory across ticks that must not leak between seeds/rounds.</summary>
        public delegate IBotPolicy BotPolicyFactory(int seatIndex);

        public readonly struct RunResult
        {
            public readonly int Seed;
            public readonly int TicksElapsed;
            public readonly bool Finished;
            public readonly V2Result? Result;

            public RunResult(int seed, int ticksElapsed, bool finished, V2Result? result)
            {
                Seed = seed;
                TicksElapsed = ticksElapsed;
                Finished = finished;
                Result = result;
            }
        }

        /// <summary>
        /// Runs one full round for the given seed. Terminates early (Finished
        /// = false, Result = null) if the mechanic has not reported
        /// <see cref="IMicrogame.IsFinished"/> within <paramref name="maxTicks"/> —
        /// a defensive bound so a genuine non-termination bug surfaces as a
        /// clearly-flagged unfinished run rather than an infinite loop.
        /// </summary>
        public static RunResult RunOne(
            MicrogameFactory mechanicFactory,
            BotPolicyFactory policyFactory,
            BotSkill skill,
            PlayerRoster roster,
            int seed,
            float difficultyMult = 1f,
            int maxTicks = 100_000)
        {
            if (mechanicFactory == null) throw new ArgumentNullException(nameof(mechanicFactory));
            if (policyFactory == null) throw new ArgumentNullException(nameof(policyFactory));

            V2Microgame mechanic = mechanicFactory();
            SeededRandom mechanicRng = new SeededRandom(seed);
            SeededRandom botRng = SeededRandom.Derive(seed, roundNumber: 0, RngStream.Bots);

            mechanic.Initialize(mechanicRng, roster, difficultyMult);

            var policies = new IBotPolicy[4];
            for (int i = 0; i < 4; i++)
                if (roster.IsActive((PlayerSlot)i))
                    policies[i] = policyFactory(i);

            var players = new PlayerInput[4];
            int tick = 0;
            while (!mechanic.IsFinished && tick < maxTicks)
            {
                RenderState rs = mechanic.GetRenderState();
                for (int i = 0; i < 4; i++)
                    players[i] = policies[i] != null
                        ? policies[i].Decide(skill, new BotView(rs, i), botRng)
                        : default;

                mechanic.Tick(new V2Snapshot(tick, players));
                tick++;
            }

            bool finished = mechanic.IsFinished;
            V2Result? result = finished ? mechanic.GetResult() : (V2Result?)null;
            return new RunResult(seed, tick, finished, result);
        }

        /// <summary>Runs <paramref name="seedCount"/> consecutive seeds starting at <paramref name="seedStart"/>, returning one <see cref="RunResult"/> per seed in order.</summary>
        public static RunResult[] RunBatch(
            MicrogameFactory mechanicFactory,
            BotPolicyFactory policyFactory,
            BotSkill skill,
            PlayerRoster roster,
            int seedCount,
            float difficultyMult = 1f,
            int maxTicks = 100_000,
            int seedStart = 0)
        {
            var results = new RunResult[seedCount];
            for (int s = 0; s < seedCount; s++)
                results[s] = RunOne(mechanicFactory, policyFactory, skill, roster, seedStart + s, difficultyMult, maxTicks);
            return results;
        }
    }
}
