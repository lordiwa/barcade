using System.Collections.Generic;
using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Renders the Apunta (Aim and Fire) microgame each frame.
    ///
    /// Rendering contract:
    ///   - One coloured square per player avatar at a fixed position.
    ///   - One target indicator (small white square) per player drawn at distance
    ///     from the avatar in the target direction.
    ///   - An aim-direction line (player colour, thin) extends from each avatar
    ///     in the direction the stick is pointing.
    ///   - On fire: a bright shot flash line (thick white/yellow) appears along the
    ///     aim direction and fades after ~0.2 s.
    ///   - After latch: target square turns green (HIT, scaled up) or dim red (MISS).
    ///     Avatar colour is never changed — it stays player colour for identification.
    ///
    /// Read-only view: never mutates microgame state.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public class ApuntaView : MonoBehaviour
    {
        private ApuntaMicrogame _game;
        private PlayerSlot[]    _players;

        // Per-player shapes.
        private List<GameObject> _avatarShapes   = new List<GameObject>();
        // White outline behind each avatar for instant player identification.
        private List<GameObject> _avatarOutlines  = new List<GameObject>();
        private List<GameObject> _targetShapes   = new List<GameObject>();
        private List<GameObject> _aimLines       = new List<GameObject>();

        // Previous-frame latch state per player — used to detect the shot frame.
        private bool?[] _prevLatch;

        // Active shot-flash GOs and their remaining lifetime (seconds).
        private List<GameObject> _shotFlashes     = new List<GameObject>();
        private List<float>      _shotFlashTimers = new List<float>();

        // Avatar is larger than the target so it reads as "this is ME, that is the goal".
        private const float AvatarInner   = 1.0f;
        private const float AvatarOuter   = 1.35f;
        private const float TargetSize    = 0.45f;
        private const float TargetHitScale = 1.6f; // scale-up pop on hit
        private const float AimLength     = 2.5f;
        private const float AimThick      = 0.08f;
        private const float FlashThick    = 0.25f;
        private const float FlashDuration = 0.2f;

        private static readonly float[] AvatarOffsetX = { -3f, 3f, -3f,  3f };
        private static readonly float[] AvatarOffsetY = {  2f, 2f, -2f, -2f };

        private static readonly Color TargetColor  = Color.white;
        private static readonly Color HitColor     = Color.green;
        private static readonly Color MissColor    = new Color(0.7f, 0.1f, 0.1f); // dim red
        private static readonly Color FlashColor   = new Color(1f, 0.95f, 0.3f);  // bright yellow-white

        // ── Initialise ────────────────────────────────────────────────────────────

        /// <summary>
        /// Binds this view to the given ApuntaMicrogame instance and player set.
        /// Call immediately after Prepare() has run on the microgame.
        /// </summary>
        public void Bind(ApuntaMicrogame game, PlayerSlot[] players)
        {
            _game      = game;
            _players   = players;
            _prevLatch = new bool?[players.Length];
            BuildShapes();
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────────

        private void Update()
        {
            if (_game == null) return;
            TickFlashes();
            RefreshShapes();
        }

        private void OnDisable()
        {
            DestroyShapes();
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private void BuildShapes()
        {
            DestroyShapes();

            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                Vector3 avatarPos = GetAvatarPos(i);

                // Avatar square — outlined so it stands apart from the target.
                GameObject outline;
                var avatarGo = ShapeFactory.MakeOutlinedSquare(
                    slot, avatarPos, AvatarInner, AvatarOuter, transform, out outline);
                avatarGo.name  = $"Avatar_{slot}";
                outline.name   = $"AvatarOutline_{slot}";
                _avatarShapes.Add(avatarGo);
                _avatarOutlines.Add(outline);

                // Target indicator — drawn at 2 units so bottom-row targets don't fall off-screen.
                float tx = _game.GetTargetDirX(slot);
                float ty = _game.GetTargetDirY(slot);
                Vector3 targetPos = avatarPos + new Vector3(tx, ty, 0f) * 2f;
                var targetGo = ShapeFactory.MakeSquare(TargetColor, targetPos, TargetSize, transform);
                targetGo.name = $"Target_{slot}";
                _targetShapes.Add(targetGo);

                // Aim-direction line (initial: pointing right).
                var aimGo = ShapeFactory.MakeLine(ShapeFactory.PlayerColor(slot),
                    avatarPos,
                    avatarPos + Vector3.right * AimLength,
                    AimThick,
                    transform);
                aimGo.name = $"AimLine_{slot}";
                _aimLines.Add(aimGo);
            }
        }

        private void RefreshShapes()
        {
            for (int i = 0; i < _players.Length; i++)
            {
                PlayerSlot slot = _players[i];
                Vector3 avatarPos = GetAvatarPos(i);
                bool? latch = _game.GetLatch(slot);

                // Detect the shot frame: latch just transitioned from null to a value.
                bool shotThisFrame = !_prevLatch[i].HasValue && latch.HasValue;
                _prevLatch[i] = latch;

                if (shotThisFrame)
                    SpawnShotFlash(avatarPos, slot);

                // Apply persistent outcome to the TARGET square (neutral white, so
                // green/red read clearly regardless of player colour).
                // Avatar colour is intentionally left unchanged — it stays player colour.
                if (latch.HasValue && i < _targetShapes.Count && _targetShapes[i] != null)
                    ApplyOutcomeToTarget(i, latch.Value);

                // Update aim line direction — read live stick from the game each frame.
                float ax = _game.GetAimX(slot);
                float ay = _game.GetAimY(slot);
                float mag = Mathf.Sqrt(ax * ax + ay * ay);
                if (mag > 0.1f)
                {
                    ax /= mag; ay /= mag;
                    if (_aimLines[i] != null)
                        DestroyAndRebuildAimLine(i, avatarPos, ax, ay, slot);
                }
            }
        }

        // Colour the target square green (hit) or dim red (miss) and scale it on hit.
        private void ApplyOutcomeToTarget(int i, bool hit)
        {
            var targetGo = _targetShapes[i];
            if (targetGo == null) return;

            var mr = targetGo.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = hit ? HitColor : MissColor;

            // Scale up for a satisfying "pop" on hit; leave size unchanged on miss.
            float s = hit ? TargetHitScale : 1f;
            targetGo.transform.localScale = new Vector3(s, s, 1f);
        }

        // Spawn a bright shot flash along the current aim direction (or fallback to
        // target direction if the stick is at rest at the fire moment).
        private void SpawnShotFlash(Vector3 avatarPos, PlayerSlot slot)
        {
            float ax = _game.GetAimX(slot);
            float ay = _game.GetAimY(slot);
            float mag = Mathf.Sqrt(ax * ax + ay * ay);

            // If stick at rest fall back to target direction so the flash is still visible.
            if (mag < 0.1f)
            {
                ax = _game.GetTargetDirX(slot);
                ay = _game.GetTargetDirY(slot);
                mag = Mathf.Sqrt(ax * ax + ay * ay);
            }

            if (mag > 1e-6f) { ax /= mag; ay /= mag; }

            Vector3 end = avatarPos + new Vector3(ax, ay, 0f) * AimLength;
            var flash = ShapeFactory.MakeLine(FlashColor, avatarPos, end, FlashThick, transform);
            flash.name = $"ShotFlash_{slot}";

            _shotFlashes.Add(flash);
            _shotFlashTimers.Add(FlashDuration);
        }

        // Age and destroy shot flashes each frame.
        private void TickFlashes()
        {
            for (int i = _shotFlashes.Count - 1; i >= 0; i--)
            {
                _shotFlashTimers[i] -= Time.deltaTime;
                if (_shotFlashTimers[i] <= 0f)
                {
                    if (_shotFlashes[i] != null) Destroy(_shotFlashes[i]);
                    _shotFlashes.RemoveAt(i);
                    _shotFlashTimers.RemoveAt(i);
                }
            }
        }

        private void DestroyAndRebuildAimLine(int i, Vector3 avatarPos, float dx, float dy, PlayerSlot slot)
        {
            if (_aimLines[i] != null) Destroy(_aimLines[i]);

            Vector3 end = avatarPos + new Vector3(dx, dy, 0f) * AimLength;
            var aimGo = ShapeFactory.MakeLine(ShapeFactory.PlayerColor(slot),
                avatarPos, end, AimThick, transform);
            aimGo.name = $"AimLine_{slot}";
            _aimLines[i] = aimGo;
        }

        private static Vector3 GetAvatarPos(int playerIndex)
        {
            int idx = playerIndex < 4 ? playerIndex : 0;
            return new Vector3(AvatarOffsetX[idx], AvatarOffsetY[idx], 0f);
        }

        private void DestroyShapes()
        {
            foreach (var go in _avatarShapes)   if (go != null) Destroy(go);
            foreach (var go in _avatarOutlines)  if (go != null) Destroy(go);
            foreach (var go in _targetShapes)   if (go != null) Destroy(go);
            foreach (var go in _aimLines)       if (go != null) Destroy(go);
            foreach (var go in _shotFlashes)    if (go != null) Destroy(go);
            _avatarShapes.Clear();
            _avatarOutlines.Clear();
            _targetShapes.Clear();
            _aimLines.Clear();
            _shotFlashes.Clear();
            _shotFlashTimers.Clear();
        }
    }
}
