using System;

namespace Barcade.Core.Microgames.V2
{
    /// <summary>
    /// Level data for the GDD §7.1 special cooperative phase — GDD's own
    /// "catálogo de violaciones ergonómicas (elementos de nivel, combinables
    /// por dato)". Plain engine-free POCO consumed by
    /// <see cref="CoopPhaseMicrogame"/>; no simulation logic lives here.
    ///
    /// <para>
    /// <b>The three toggleable elements</b> (<see cref="BottleneckEnabled"/> =
    /// cuelloBotella, <see cref="ForcedIntersectionEnabled"/> = interseccionForzada,
    /// <see cref="DynamicFloorEnabled"/> = sueloDinamico) are independent,
    /// composable booleans — any subset may be on. The fourth GDD element,
    /// distanciasEstiradas, has no flag here at all: it is realized entirely
    /// through how <see cref="CoopObjective.X"/>/<see cref="CoopObjective.Y"/>
    /// are authored (see that class's own doc).
    /// </para>
    ///
    /// <para>
    /// <b>Thresholds are data; the coin VALUES are not.</b> GDD §7.1: "Umbrales
    /// de recompensa: bronce/plata/oro → 2/4/6 monedas a todos" — the SCORE
    /// thresholds a team must reach for each tier are level-authored data
    /// (<see cref="BronzeThreshold"/>/<see cref="PlataThreshold"/>/<see cref="OroThreshold"/>),
    /// but the 2/4/6 COIN payouts themselves are GDD-literal constants
    /// (<see cref="CoopPhaseMicrogame.BronzeCoins"/>/etc.) — the same
    /// data-vs-hard-constant split <see cref="IgualaParams.ReactWindowFloorSeconds"/>
    /// already established for a different fixed GDD number.
    /// </para>
    /// </summary>
    public sealed class CoopLevelData
    {
        /// <summary>GDD literal bound: this special phase runs 60-90 seconds (distinct from the standard [3,8]s microgame validator bound — this scenario is NOT a regular MicrogameDefinitionV2 entry, see CoopPhaseMicrogame's own doc).</summary>
        public const float MinDurationSeconds = 60f;
        public const float MaxDurationSeconds = 90f;

        /// <summary>Ordered stations the team visits in sequence.</summary>
        public readonly CoopObjective[] Objectives;

        /// <summary>GDD "estadísticas uniformes": the ONE shared movement speed every avatar uses — see CoopPhaseMicrogame's structural proof.</summary>
        public readonly float AvatarSpeed;

        /// <summary>Shared avatar collision-circle radius (used only when <see cref="ForcedIntersectionEnabled"/>, and for the bottleneck's own push-out).</summary>
        public readonly float AvatarRadius;

        /// <summary>Distance within which an engaged seat counts as "arrived" at an objective.</summary>
        public readonly float ArrivalRadius;

        /// <summary>GDD "cuelloBotella": a fixed 1-unit-equivalent corridor obstacle pair blocks the direct path.</summary>
        public readonly bool BottleneckEnabled;

        /// <summary>GDD "interseccionForzada": inter-avatar collision — the ONLY place in Core this exists at all.</summary>
        public readonly bool ForcedIntersectionEnabled;

        /// <summary>GDD "sueloDinamico": at <see cref="DynamicFloorTick"/>, all not-yet-reached objectives' positions are reshuffled.</summary>
        public readonly bool DynamicFloorEnabled;

        /// <summary>GDD literal: t=30s (fixed at 60Hz — 1800 ticks). Not a per-definition value; the GDD names this exact moment.</summary>
        public const int DynamicFloorTick = 1800;

        public readonly int BronzeThreshold;
        public readonly int PlataThreshold;
        public readonly int OroThreshold;

        public readonly float DurationSeconds;

        public CoopLevelData(
            CoopObjective[] objectives,
            float avatarSpeed,
            float avatarRadius,
            float arrivalRadius,
            bool bottleneckEnabled,
            bool forcedIntersectionEnabled,
            bool dynamicFloorEnabled,
            int bronzeThreshold,
            int plataThreshold,
            int oroThreshold,
            float durationSeconds)
        {
            if (objectives == null || objectives.Length == 0)
                throw new ArgumentException("must declare at least one objective.", nameof(objectives));
            if (avatarSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarSpeed), "must be positive.");
            if (avatarRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(avatarRadius), "must be positive.");
            if (arrivalRadius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(arrivalRadius), "must be positive.");
            if (bronzeThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(bronzeThreshold), "must be at least 1 (GDD §6.1: no zero payout tier).");
            if (plataThreshold <= bronzeThreshold)
                throw new ArgumentOutOfRangeException(nameof(plataThreshold), "must exceed bronzeThreshold.");
            if (oroThreshold <= plataThreshold)
                throw new ArgumentOutOfRangeException(nameof(oroThreshold), "must exceed plataThreshold.");
            if (durationSeconds < MinDurationSeconds || durationSeconds > MaxDurationSeconds)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds),
                    $"GDD §7.1: this phase must run [{MinDurationSeconds}, {MaxDurationSeconds}]s.");

            Objectives = objectives;
            AvatarSpeed = avatarSpeed;
            AvatarRadius = avatarRadius;
            ArrivalRadius = arrivalRadius;
            BottleneckEnabled = bottleneckEnabled;
            ForcedIntersectionEnabled = forcedIntersectionEnabled;
            DynamicFloorEnabled = dynamicFloorEnabled;
            BronzeThreshold = bronzeThreshold;
            PlataThreshold = plataThreshold;
            OroThreshold = oroThreshold;
            DurationSeconds = durationSeconds;
        }

        /// <summary>
        /// [ASSUMED] a representative level: 4 stations at the arena's 4
        /// corners (distanciasEstiradas via data alone, see CoopObjective's
        /// doc), alternating Sujeta/Iguala, all three toggleable elements ON,
        /// thresholds at the GDD-literal 2/4/6 (reused as both the score
        /// thresholds AND, coincidentally, the coin amounts — GDD gives no
        /// other numbers to build thresholds from). 75s sits mid-range of the
        /// GDD's own [60,90]s bound.
        /// </summary>
        public static CoopLevelData GddDefaults => new CoopLevelData(
            objectives: new[]
            {
                CoopObjective.Sujeta(0.1f, 0.1f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, viewerSeat: 0)),
                CoopObjective.Iguala(0.9f, 0.9f, IgualaParams.GddDefaults),
                CoopObjective.Sujeta(0.9f, 0.1f, SujetaParams.GddDefaults(SujetaMode.HoldTogether, viewerSeat: 0)),
                CoopObjective.Iguala(0.1f, 0.9f, IgualaParams.GddDefaults),
            },
            avatarSpeed: 0.3f,
            avatarRadius: 0.03f,
            arrivalRadius: 0.06f,
            bottleneckEnabled: true,
            forcedIntersectionEnabled: true,
            dynamicFloorEnabled: true,
            bronzeThreshold: 2,
            plataThreshold: 4,
            oroThreshold: 6,
            durationSeconds: 75f);
    }
}
