using Barcade.Core;
using Barcade.Core.Microgames.V2;
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.SlowTests
{
    /// <summary>
    /// TASK-042 (GDD T-114, Hito 4 gate) — a simple, deterministic per-seat
    /// decision heuristic covering everything a "seat" needs to decide during
    /// the board sub-loop: BOARD_MOVE's stop-meter tap, BOARD_RESOLVE's
    /// Inversión deposit-tier choice, and BOARD_RESOLVE's weapon aim+confirm.
    ///
    /// <para>
    /// <b>[ASSUMED] deliberately simple</b> (the ticket's own allowance: "a
    /// simple heuristic" is enough to exercise the gate). A uniformly random tap
    /// tick for movement, a uniformly random deposit tier, and a randomized
    /// weapon aim that sometimes never confirms — broad enough to exercise every
    /// weapon code path INCLUDING restriction-rejection (a "dumb" bot aiming at
    /// an ineligible target for Guante/Iman is exactly the kind of no-op the
    /// economic-invariant sweep needs to see, not just the success path), without
    /// needing a smarter targeting AI this gate doesn't require.
    /// </para>
    ///
    /// <para>
    /// <b>Why not <c>Barcade.Core.Bots.IBotPolicy</c>.</b> That contract is
    /// shaped around a single microgame's <c>RenderState</c>/<c>BotView</c> (GDD
    /// Annex D.3) — it has no notion of BOARD_MOVE/BOARD_RESOLVE's own input
    /// semantics (stop-meter tap, deposit tier, weapon aim+confirm), and no
    /// production need drives board-phase bot seat-fill today (bots fill
    /// MICROGAME seats only, via <c>BotStatisticalHarness</c>/
    /// <c>EsquivaBotPolicy</c> etc.). This driver is scoped to the Hito 4
    /// statistical gate — same "test-only Oracle" placement as
    /// <see cref="FakeInputs"/>/<c>Mash</c>/<c>CorrePerfectRunner</c>/
    /// <c>MantenCorrectionOracle</c> in this same folder.
    /// </para>
    ///
    /// Reads <see cref="SessionStateMachine.CurrentPhase"/> and
    /// <see cref="SessionStateMachine.BoardWeaponWindowActive"/> — both already
    /// public — to know what each tick's input should mean; never reaches into
    /// BoardModel internals.
    /// </summary>
    internal sealed class BoardBotDriver
    {
        private const int MoveTapWindowTicks = 300; // <=5s -- comfortably under BOARD_MOVE's 8s target/12s max
        private const int SubWindowTicks = 240; // 4s @ 60Hz -- matches BoardModel's deposit/weapon window length
        private const double NeverFireChance = 0.2; // exercises the "never confirms this round" path too

        private readonly SeededRandom _rng;
        private readonly int[] _moveTapTick = new int[4];
        private readonly int[] _depositSlot = new int[4]; // 0=W,1=N,2=E,3=none
        private readonly int[] _weaponAimSlot = new int[4]; // 0=W,1=N,2=E
        private readonly int[] _weaponTapTick = new int[4]; // >= SubWindowTicks => never fires this round

        private SessionPhase? _prevPhase;
        private int _localTick;
        private bool _prevWeaponWindow;
        private int _localWeaponTick;

        public BoardBotDriver(SeededRandom rng)
        {
            _rng = rng;
        }

        /// <summary>Builds this tick's input snapshot for whichever phase the session is currently in.</summary>
        public V2Snapshot Decide(SessionStateMachine fsm, int tick)
        {
            var inputs = new FakeInputs();

            if (fsm.CurrentPhase != _prevPhase)
            {
                _localTick = 0;
                _prevWeaponWindow = false;
                _localWeaponTick = 0;
                if (fsm.CurrentPhase == SessionPhase.BoardMove) RerollMove();
                if (fsm.CurrentPhase == SessionPhase.BoardResolve) RerollResolve();
                _prevPhase = fsm.CurrentPhase;
            }

            switch (fsm.CurrentPhase)
            {
                case SessionPhase.Join:
                    for (int s = 0; s < 4; s++) inputs.Set((PlayerSlot)s, button: true);
                    break;

                case SessionPhase.BoardMove:
                    for (int s = 0; s < 4; s++)
                        inputs.Set((PlayerSlot)s, button: _localTick >= _moveTapTick[s]);
                    break;

                case SessionPhase.BoardResolve:
                {
                    bool weaponWindow = fsm.BoardWeaponWindowActive;
                    if (weaponWindow && !_prevWeaponWindow) _localWeaponTick = 0;
                    _prevWeaponWindow = weaponWindow;

                    for (int s = 0; s < 4; s++)
                    {
                        if (!weaponWindow)
                            inputs.Set((PlayerSlot)s, stick: DepositSlotToDirection(_depositSlot[s]));
                        else
                            inputs.Set((PlayerSlot)s,
                                stick: WeaponSlotToDirection(_weaponAimSlot[s]),
                                button: _localWeaponTick >= _weaponTapTick[s]);
                    }
                    if (weaponWindow) _localWeaponTick++;
                    break;
                }

                default:
                    break; // MgIntro/Play/Result/Intermission/FinalWager/FinalMg/GameOver: nothing board-specific to decide
            }

            _localTick++;
            return inputs.Build(tick);
        }

        private void RerollMove()
        {
            for (int s = 0; s < 4; s++) _moveTapTick[s] = _rng.NextInt(0, MoveTapWindowTicks);
        }

        private void RerollResolve()
        {
            for (int s = 0; s < 4; s++)
            {
                _depositSlot[s] = _rng.NextInt(0, 4);
                _weaponAimSlot[s] = _rng.NextInt(0, 3);
                bool neverFires = _rng.NextFloat() < NeverFireChance;
                _weaponTapTick[s] = neverFires ? SubWindowTicks + 1 : _rng.NextInt(0, SubWindowTicks);
            }
        }

        private static Direction8 DepositSlotToDirection(int slot)
        {
            switch (slot)
            {
                case 0: return Direction8.W;
                case 1: return Direction8.N;
                case 2: return Direction8.E;
                default: return Direction8.None;
            }
        }

        private static Direction8 WeaponSlotToDirection(int slot)
        {
            switch (slot)
            {
                case 0: return Direction8.W;
                case 1: return Direction8.N;
                default: return Direction8.E;
            }
        }
    }
}
