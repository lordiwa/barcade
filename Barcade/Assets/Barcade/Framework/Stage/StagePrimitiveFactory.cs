using UnityEngine;
using Barcade.Core.Microgames.V2;

namespace Barcade.Framework.Stage
{
    /// <summary>
    /// Headless-safe primitive fallback per <see cref="EntityKind"/>, used by
    /// <see cref="StageEntityFactory"/> when no prefab resolves for a given
    /// Kind+VisualVariant. Mirrors <c>Barcade.Framework.DodgeGameBootstrap</c>'s
    /// "CreatePrimitive, then <c>.material.color =</c>" pattern exactly (same
    /// primitive-first approach that already works in this URP project) so a
    /// stage with no authored 3D content still renders something readable and
    /// the fast/EditMode test suites never depend on Resources content existing.
    ///
    /// [ASSUMED] primitive shape + neutral tint per Kind — no GDD art direction
    /// is given beyond "modelos low-poly" (§10.4); these are placeholders for
    /// human UAT/art pass, not a final look.
    /// </summary>
    public static class StagePrimitiveFactory
    {
        private static readonly Color NeutralHazard = new Color(0.85f, 0.25f, 0.10f);   // warning orange-red
        private static readonly Color NeutralProjectile = new Color(0.85f, 0.85f, 0.85f); // light grey
        private static readonly Color NeutralTarget = new Color(0.25f, 0.55f, 0.85f);   // steel blue
        private static readonly Color NeutralBoardPawn = new Color(0.6f, 0.6f, 0.6f);    // neutral grey (retinted per-seat by the presenter)
        private static readonly Color NeutralPickup = new Color(0.95f, 0.80f, 0.15f);    // gold

        public static GameObject CreateFallback(EntityKind kind, Transform parent)
        {
            GameObject go;
            switch (kind)
            {
                case EntityKind.PlayerAvatar:
                    go = MakePrimitive(PrimitiveType.Cube, Color.white, parent); // retinted per-seat by the presenter
                    go.transform.localScale = Vector3.one * 0.7f;
                    break;

                case EntityKind.Hazard:
                    go = MakePrimitive(PrimitiveType.Sphere, NeutralHazard, parent);
                    go.transform.localScale = Vector3.one * 0.8f;
                    break;

                case EntityKind.Projectile:
                    go = MakePrimitive(PrimitiveType.Sphere, NeutralProjectile, parent);
                    go.transform.localScale = Vector3.one * 0.3f;
                    break;

                case EntityKind.Target:
                    go = MakePrimitive(PrimitiveType.Cylinder, NeutralTarget, parent);
                    go.transform.localScale = new Vector3(0.8f, 0.1f, 0.8f); // flat disc-like
                    break;

                case EntityKind.BoardPawn:
                    go = MakePrimitive(PrimitiveType.Capsule, NeutralBoardPawn, parent); // retinted per-seat by the presenter
                    go.transform.localScale = Vector3.one * 0.5f;
                    break;

                case EntityKind.Pickup:
                    go = MakePrimitive(PrimitiveType.Sphere, NeutralPickup, parent);
                    go.transform.localScale = Vector3.one * 0.35f;
                    break;

                default:
                    go = MakePrimitive(PrimitiveType.Cube, Color.magenta, parent); // loud "unhandled Kind" signal
                    break;
            }

            go.name = $"StageFallback_{kind}";
            return go;
        }

        private static GameObject MakePrimitive(PrimitiveType type, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider); // no physics in stage views (GDD §10.4: nothing that affects the result passes through PhysX)

            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<Renderer>().material.color = color;
            return go;
        }
    }
}
