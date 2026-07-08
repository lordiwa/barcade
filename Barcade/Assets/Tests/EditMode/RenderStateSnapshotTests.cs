using NUnit.Framework;
using Barcade.Core.Microgames.V2;
using Barcade.Presentation;

namespace Barcade.Presentation.Tests
{
    /// <summary>
    /// TASK-027 (GDD T-104b) AC2/AC5 regression lock: <see cref="RenderStateSnapshot"/>
    /// deep-copies a Core <see cref="RenderState"/> tick so the presenter can hold
    /// "the two most recent" states (§10.3) independently of the sim's single
    /// reused/mutated instance. No Unity required -- pure C#.
    /// </summary>
    [TestFixture]
    public class RenderStateSnapshotTests
    {
        private static RenderState BuildSource(int tick, int entityCount)
        {
            var state = new RenderState(entityCapacity: 8, feedbackCapacity: 4);
            state.Tick = tick;
            state.EntityCount = entityCount;
            for (int i = 0; i < entityCount; i++)
            {
                state.Entities[i] = new RenderEntity
                {
                    Kind = EntityKind.PlayerAvatar,
                    OwnerSeat = i,
                    X = 0.1f * i,
                    Y = 0.2f * i,
                    Height = 0.3f * i,
                    Rotation = 10f * i,
                    Scale = 1f,
                    VisualVariant = (byte)i,
                    Progress01 = 0.1f * i,
                };
            }
            state.Hud.ActiveVerb = "¡ESQUIVA!";
            for (int i = 0; i < 4; i++)
            {
                state.Hud.Scores[i] = i * 10;
                state.Hud.Meters[i] = i * 0.25f;
            }
            state.FeedbackCount = 1;
            state.Feedback[0] = new FeedbackEvent(seat: 0, level: FeedbackLevel.High, cue: 7);
            return state;
        }

        [Test]
        public void CopyFrom_CopiesAllFields()
        {
            RenderState source = BuildSource(tick: 5, entityCount: 3);
            var snapshot = new RenderStateSnapshot();

            snapshot.CopyFrom(source);

            Assert.That(snapshot.HasData, Is.True);
            Assert.That(snapshot.Tick, Is.EqualTo(5));
            Assert.That(snapshot.EntityCount, Is.EqualTo(3));
            Assert.That(snapshot.Entities[1].X, Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(snapshot.Entities[2].OwnerSeat, Is.EqualTo(2));
            Assert.That(snapshot.HudActiveVerb, Is.EqualTo("¡ESQUIVA!"));
            Assert.That(snapshot.HudScores[2], Is.EqualTo(20));
            Assert.That(snapshot.HudMeters[2], Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(snapshot.FeedbackCount, Is.EqualTo(1));
            Assert.That(snapshot.Feedback[0].Level, Is.EqualTo(FeedbackLevel.High));
        }

        [Test]
        public void CopyFrom_IsIndependent_OfLaterMutationToSource()
        {
            // The sim reuses ONE RenderState instance and overwrites it every
            // tick -- a snapshot copy must not alias its arrays.
            RenderState source = BuildSource(tick: 1, entityCount: 2);
            var snapshot = new RenderStateSnapshot();
            snapshot.CopyFrom(source);

            // Mutate the source as the sim would on the next tick.
            source.Tick = 2;
            source.Entities[0].X = 999f;
            source.Hud.Scores[0] = 999;

            Assert.That(snapshot.Tick, Is.EqualTo(1));
            Assert.That(snapshot.Entities[0].X, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(snapshot.HudScores[0], Is.EqualTo(0));
        }

        [Test]
        public void TwoSnapshots_FromTheSameReusedSource_StayIndependent()
        {
            // Mirrors the double-buffer usage: presenter keeps a "previous" and
            // a "current" snapshot, both copied from the SAME mutable RenderState
            // instance at different ticks.
            RenderState source = BuildSource(tick: 1, entityCount: 1);
            var previous = new RenderStateSnapshot();
            var current = new RenderStateSnapshot();

            previous.CopyFrom(source);

            source.Tick = 2;
            source.Entities[0].X = 5f;
            current.CopyFrom(source);

            Assert.That(previous.Tick, Is.EqualTo(1));
            Assert.That(previous.Entities[0].X, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(current.Tick, Is.EqualTo(2));
            Assert.That(current.Entities[0].X, Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void CopyFrom_GrowingSourceCapacity_ResizesSnapshotArrays()
        {
            var snapshot = new RenderStateSnapshot();
            snapshot.CopyFrom(BuildSource(tick: 1, entityCount: 2));

            RenderState larger = BuildSource(tick: 2, entityCount: 8);
            larger.Entities[7].X = 42f;
            snapshot.CopyFrom(larger);

            Assert.That(snapshot.EntityCount, Is.EqualTo(8));
            Assert.That(snapshot.Entities[7].X, Is.EqualTo(42f).Within(1e-5f));
        }

        [Test]
        public void HasData_FalseBeforeFirstCopy()
        {
            var snapshot = new RenderStateSnapshot();

            Assert.That(snapshot.HasData, Is.False);
        }
    }
}
