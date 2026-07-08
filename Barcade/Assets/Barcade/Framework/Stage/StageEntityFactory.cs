using System.Collections.Generic;
using UnityEngine;
using Barcade.Core.Microgames.V2;

namespace Barcade.Framework.Stage
{
    /// <summary>
    /// One pooled 3D instance for a <see cref="RenderEntity"/> slot. Wraps the
    /// spawned GameObject (either a resolved prefab or a
    /// <see cref="StagePrimitiveFactory"/> fallback) plus cached renderers for
    /// cheap per-frame tinting (GDD §10.4 avatar color channel).
    /// </summary>
    public sealed class StageEntityView
    {
        public readonly GameObject GameObject;
        public readonly Transform Transform;
        public readonly EntityKind Kind;
        public readonly byte Variant;

        private readonly Renderer[] _renderers;

        public StageEntityView(GameObject gameObject, EntityKind kind, byte variant)
        {
            GameObject = gameObject;
            Transform = gameObject.transform;
            Kind = kind;
            Variant = variant;
            _renderers = gameObject.GetComponentsInChildren<Renderer>();
        }

        /// <summary>
        /// Tints every renderer this instance carries. Accesses
        /// <c>Renderer.material</c> (an instance, not <c>sharedMaterial</c>) so
        /// tinting one entity never bleeds into a pooled sibling — the first
        /// access per renderer allocates a material instance (a well-known,
        /// one-time-per-renderer Unity cost), never per-frame afterwards.
        /// </summary>
        public void ApplyTint(Color color)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].material.color = color;
            }
        }
    }

    /// <summary>
    /// Resolves EntityKind+VisualVariant -&gt; prefab per the active
    /// <c>stageProfile.entityPrefabSet</c> (GDD §10.4/§11.1), with a
    /// headless-safe primitive fallback (<see cref="StagePrimitiveFactory"/>)
    /// mirroring <c>Barcade.Framework.DodgeGameBootstrap</c>'s
    /// "Resources.Load; null falls back to CreatePrimitive" pattern. Pools
    /// instances per (Kind, VisualVariant) so steady-state rendering allocates
    /// no managed heap memory once the round's peak entity count is reached
    /// (GDD §14) — <see cref="StagePresenter"/> is expected to
    /// <see cref="Acquire"/>/<see cref="Release"/> every frame rather than
    /// Instantiate/Destroy directly.
    ///
    /// [ASSUMED] prefab addressing convention (no GDD text specifies one):
    /// <c>Resources.Load&lt;GameObject&gt;("Stage/{entityPrefabSetId}/{Kind}_{VisualVariant}")</c>,
    /// falling back to <c>"Stage/{entityPrefabSetId}/{Kind}"</c> (variant-less)
    /// before the primitive fallback.
    /// </summary>
    public sealed class StageEntityFactory
    {
        private readonly string _entityPrefabSetId;
        private readonly Transform _parent;

        private readonly Dictionary<(EntityKind, byte), Stack<StageEntityView>> _pools =
            new Dictionary<(EntityKind, byte), Stack<StageEntityView>>();

        private readonly Dictionary<(EntityKind, byte), GameObject> _resolvedPrefabCache =
            new Dictionary<(EntityKind, byte), GameObject>();

        public StageEntityFactory(string entityPrefabSetId, Transform parent)
        {
            _entityPrefabSetId = entityPrefabSetId ?? string.Empty;
            _parent = parent;
        }

        public StageEntityView Acquire(EntityKind kind, byte variant)
        {
            var key = (kind, variant);
            if (_pools.TryGetValue(key, out Stack<StageEntityView> pool) && pool.Count > 0)
            {
                StageEntityView reused = pool.Pop();
                reused.GameObject.SetActive(true);
                return reused;
            }

            return CreateNew(kind, variant);
        }

        public void Release(StageEntityView view)
        {
            if (view == null) return;

            view.GameObject.SetActive(false);
            var key = (view.Kind, view.Variant);
            if (!_pools.TryGetValue(key, out Stack<StageEntityView> pool))
            {
                pool = new Stack<StageEntityView>();
                _pools[key] = pool;
            }
            pool.Push(view);
        }

        private StageEntityView CreateNew(EntityKind kind, byte variant)
        {
            GameObject prefab = ResolvePrefab(kind, variant);
            GameObject instance = prefab != null
                ? Object.Instantiate(prefab, _parent)
                : StagePrimitiveFactory.CreateFallback(kind, _parent);

            return new StageEntityView(instance, kind, variant);
        }

        private GameObject ResolvePrefab(EntityKind kind, byte variant)
        {
            var key = (kind, variant);
            if (_resolvedPrefabCache.TryGetValue(key, out GameObject cached))
                return cached; // a previously-resolved "no prefab" (null) is cached too, so we never re-probe Resources for it

            GameObject prefab = Resources.Load<GameObject>($"Stage/{_entityPrefabSetId}/{kind}_{variant}")
                ?? Resources.Load<GameObject>($"Stage/{_entityPrefabSetId}/{kind}");

            _resolvedPrefabCache[key] = prefab;
            return prefab;
        }
    }
}
