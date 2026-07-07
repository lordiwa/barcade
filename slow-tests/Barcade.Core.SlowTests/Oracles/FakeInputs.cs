using Barcade.Core;
using Barcade.Core.Microgames.V2;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.SlowTests
{
    /// <summary>
    /// Per-seat stick/button test double that builds a v2 <see cref="InputSnapshot"/>.
    /// Byte-identical to the private <c>FakeInputs</c> nested in each fast v2
    /// microgame test fixture (EsquivaMicrogameV2Tests / MantenMicrogameV2Tests /
    /// CorreMicrogameV2Tests) — lifted here verbatim so the slow sweep drives the
    /// sims through the exact same input surface the fast tests use.
    /// </summary>
    internal sealed class FakeInputs
    {
        private readonly PlayerInput[] _players = new PlayerInput[4];

        public void Set(PlayerSlot slot, Direction8 stick = Direction8.None, bool button = false)
            => _players[(int)slot] = new PlayerInput(stick, button);

        public V2Snapshot Build(int tick) => new V2Snapshot(tick, _players);
    }
}
