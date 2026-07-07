// Barcade.Core.Board is lexically nested inside Barcade.Core, which declares its
// own (v1, per-seat) InputSnapshot -- an unqualified "InputSnapshot" here would
// resolve against THAT enclosing-namespace member before this "using"-imported
// one is ever considered (enclosing-namespace member lookup outranks a using
// directive, regardless of whether the using is a same-name alias). Every other
// file in this codebase that is nested under Barcade.Core and needs the v2 type
// works around this with a distinctly-named alias (SessionStateMachine.cs's
// "V2 = Barcade.Core.Microgames.V2", every V2 test fixture's "V2Snapshot = ...")
// rather than an alias sharing the bare "InputSnapshot" name -- this file follows
// the same proven pattern.
using V2Snapshot = Barcade.Core.Microgames.V2.InputSnapshot;

namespace Barcade.Core.Board
{
    /// <summary>
    /// The board meta-game contract (GDD Annex D.2, literal signature). Engine-free
    /// Core: all randomness flows through the injected <see cref="SeededRandom"/>;
    /// no <c>UnityEngine</c> type may appear in an implementation.
    ///
    /// <para>
    /// <b>InputSnapshot choice.</b> Annex D.2 lists <c>TickMove(in InputSnapshot)</c>
    /// with the same GDD §3.2 session-level shape (<c>{ int Tick; PlayerInput[]
    /// Players; }</c>) as <c>Microgames.V2.IMicrogame.Tick</c> — not the older
    /// per-seat <c>Barcade.Core.InputSnapshot</c>. <see cref="V2Snapshot"/> (this
    /// file's alias for <c>Microgames.V2.InputSnapshot</c>) is that type — the one
    /// <c>SessionStateMachine.Tick</c> already receives every simulation tick, so a
    /// future BOARD_MOVE integration can forward it straight through with no
    /// translation layer. The alias exists only because this file is nested under
    /// Barcade.Core (see the using-directive's own comment); the contract's actual
    /// parameter type is exactly Annex D.2's <c>InputSnapshot</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Concrete implementer note (<see cref="BoardModel"/>).</b> The Annex D.2
    /// signature has no room for the real-time, multi-tick player choice GDD §5.3
    /// describes for Inversión ("elección con palanca en ≤4s") — <see cref="Resolve"/>
    /// takes only an RNG, no input, no ticking. <see cref="BoardModel"/> therefore
    /// adds a parallel, additive mini-contract beyond this interface —
    /// <c>BeginResolvePhase</c>/<c>TickResolve</c>/<c>ResolveFinished</c>, mirroring
    /// this interface's own Move-phase shape — so BOARD_RESOLVE can tick a choice
    /// window before calling the literal <see cref="Resolve"/>. See
    /// <see cref="BoardModel"/>'s class doc for the full rationale; this does not
    /// change this interface's own semantics.
    /// </para>
    /// </summary>
    public interface IBoardModel
    {
        /// <summary>Starts a new BOARD_MOVE: resets every seat's stop-meter to live/cycling and unfrozen (GDD §5.2).</summary>
        void BeginMovePhase(SeededRandom rng);

        /// <summary>Advances the stop-meters by exactly one simulation tick (GDD §5.2's medidor de parada).</summary>
        void TickMove(in V2Snapshot input);

        /// <summary>True once every seat's stop-meter has frozen (tap or the 8s timeout) and positions have been advanced.</summary>
        bool MoveFinished { get; }

        /// <summary>Resolves this round's landed-square effects and weapon use (GDD §5.3/§5.4) into a <see cref="BoardResolution"/>.</summary>
        BoardResolution Resolve(SeededRandom rng);

        /// <summary>The current board state — positions, balances, owners, weapons (GDD Annex D.2).</summary>
        BoardSnapshot GetSnapshot();
    }
}
