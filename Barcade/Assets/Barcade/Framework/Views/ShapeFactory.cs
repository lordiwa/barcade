using UnityEngine;
using Barcade.Core;

namespace Barcade.Framework
{
    /// <summary>
    /// Lightweight factory that creates coloured geometric shapes at runtime
    /// using Unity primitives and a 1x1 white sprite tinted to the player colour.
    ///
    /// Player palette (Rojo/Azul/Amarillo/Verde + neutral grey for non-player objects).
    ///
    /// All GameObjects returned by this factory should be destroyed by the caller
    /// (typically in the view's OnDisable or when the microgame ends).
    ///
    /// No art assets required — geometric aesthetic is intentional for Milestone 1.
    /// Lives in Barcade.Framework (UnityEngine allowed).
    /// </summary>
    public static class ShapeFactory
    {
        // ── Player colours (GDD primary palette) ─────────────────────────────────

        private static readonly Color ColorRojo     = new Color(0.91f, 0.13f, 0.17f); // #E8212B
        private static readonly Color ColorAzul     = new Color(0.12f, 0.37f, 0.75f); // #1E5FBE
        private static readonly Color ColorAmarillo = new Color(0.96f, 0.77f, 0.00f); // #F5C400
        private static readonly Color ColorVerde    = new Color(0.17f, 0.66f, 0.29f); // #2BA84A
        private static readonly Color ColorNeutral  = new Color(0.60f, 0.60f, 0.60f); // neutral grey

        // ── Cached white sprite ───────────────────────────────────────────────────

        private static Sprite _whiteSprite;

        private static Sprite WhiteSprite
        {
            get
            {
                if (_whiteSprite == null)
                    _whiteSprite = CreateWhiteSprite();
                return _whiteSprite;
            }
        }

        private static Sprite CreateWhiteSprite()
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(
                tex,
                new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f),
                1f);
        }

        // ── Public API ───────────────────────────────────────────────────────────

        /// <summary>Returns the tint colour for a given player slot.</summary>
        public static Color PlayerColor(PlayerSlot slot)
        {
            switch (slot)
            {
                case PlayerSlot.Rojo:     return ColorRojo;
                case PlayerSlot.Azul:     return ColorAzul;
                case PlayerSlot.Amarillo: return ColorAmarillo;
                case PlayerSlot.Verde:    return ColorVerde;
                default:                  return ColorNeutral;
            }
        }

        /// <summary>
        /// Creates a square (Quad primitive) at the given world position,
        /// scaled to <paramref name="size"/> world units, tinted to the player colour.
        /// </summary>
        public static GameObject MakeSquare(
            PlayerSlot slot,
            Vector3 position,
            float size,
            Transform parent = null)
        {
            return MakeQuad(PlayerColor(slot), position, new Vector2(size, size), parent);
        }

        /// <summary>
        /// Creates a square with a specific colour (for non-player objects such as hazards).
        /// </summary>
        public static GameObject MakeSquare(
            Color color,
            Vector3 position,
            float size,
            Transform parent = null)
        {
            return MakeQuad(color, position, new Vector2(size, size), parent);
        }

        /// <summary>
        /// Creates a rectangle with independent width and height, tinted to the player colour.
        /// </summary>
        public static GameObject MakeRect(
            PlayerSlot slot,
            Vector3 position,
            float width,
            float height,
            Transform parent = null)
        {
            return MakeQuad(PlayerColor(slot), position, new Vector2(width, height), parent);
        }

        /// <summary>
        /// Creates a rectangle with independent width and height using a specific colour.
        /// </summary>
        public static GameObject MakeRect(
            Color color,
            Vector3 position,
            float width,
            float height,
            Transform parent = null)
        {
            return MakeQuad(color, position, new Vector2(width, height), parent);
        }

        /// <summary>
        /// Creates a circle (SpriteRenderer with white sprite scaled to circle-like appearance)
        /// tinted to the player colour.
        /// </summary>
        public static GameObject MakeCircle(
            PlayerSlot slot,
            Vector3 position,
            float radius,
            Transform parent = null)
        {
            return MakeSpriteShape(PlayerColor(slot), position, radius * 2f, parent);
        }

        /// <summary>
        /// Creates a circle using a specific colour.
        /// </summary>
        public static GameObject MakeCircle(
            Color color,
            Vector3 position,
            float radius,
            Transform parent = null)
        {
            return MakeSpriteShape(color, position, radius * 2f, parent);
        }

        /// <summary>
        /// Creates a thin line using a SpriteRenderer stretched in one axis.
        /// <paramref name="thickness"/> controls the narrow dimension (world units).
        /// </summary>
        public static GameObject MakeLine(
            Color color,
            Vector3 from,
            Vector3 to,
            float thickness,
            Transform parent = null)
        {
            Vector3 mid = (from + to) * 0.5f;
            float length = Vector3.Distance(from, to);
            float angle = Mathf.Atan2(to.y - from.y, to.x - from.x) * Mathf.Rad2Deg;

            var go = MakeQuad(color, mid, new Vector2(length, thickness), parent);
            go.transform.rotation = Quaternion.Euler(0, 0, angle);
            return go;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        private static GameObject MakeQuad(
            Color color,
            Vector3 position,
            Vector2 scale,
            Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(go.GetComponent<MeshCollider>()); // no physics in views
            go.transform.position = position;
            go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: true);

            var mr = go.GetComponent<MeshRenderer>();
            mr.material = CreateUnlitMaterial(color);
            return go;
        }

        private static GameObject MakeSpriteShape(
            Color color,
            Vector3 position,
            float size,
            Transform parent)
        {
            var go = new GameObject("Shape");
            go.transform.position = position;
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: true);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = WhiteSprite;
            sr.color = color;
            go.transform.localScale = new Vector3(size, size, 1f);
            return go;
        }

        private static Material CreateUnlitMaterial(Color color)
        {
            // Use Unity's built-in Unlit/Color shader — no external assets needed.
            var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
            mat.color = color;
            return mat;
        }
    }
}
