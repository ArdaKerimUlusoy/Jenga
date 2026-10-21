using UnityEngine;
using UnityEngine.Rendering;

namespace SecretJenga
{
    /// <summary>Runtime fallbacks for sprites/materials so nothing is ever null (used when generated assets are missing).</summary>
    public static class RuntimeArt
    {
        static Sprite _rounded, _circle;
        static Font _font;

        public static Font Font
        {
            get
            {
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        public static Sprite Rounded
        {
            get
            {
                if (_rounded == null) _rounded = MakeRounded(64, 16);
                return _rounded;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null) _circle = MakeRounded(128, 64, false);
                return _circle;
            }
        }

        public static Sprite MakeRounded(int size, int radius, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            var border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, border);
        }

        public static Shader LitShader
        {
            get
            {
                var s = Shader.Find("Universal Render Pipeline/Lit");
                return s != null ? s : Shader.Find("Standard");
            }
        }

        public static Shader UnlitShader
        {
            get
            {
                var s = Shader.Find("Universal Render Pipeline/Unlit");
                return s != null ? s : Shader.Find("Unlit/Color");
            }
        }

        public static Material MakeLit(Color c, float smooth = 0.4f)
        {
            var m = new Material(LitShader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            EnableEmission(m);
            return m;
        }

        public static void EnableEmission(Material m)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.GetColor("_EmissionColor").maxColorComponent <= 0f) m.SetColor("_EmissionColor", Color.black);
        }

        /// <summary>Configures a URP Lit/Unlit material for alpha blending.</summary>
        public static void MakeTransparent(Material m)
        {
            if (m == null) return;
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        public static Material MakeGhost(Color c)
        {
            var m = new Material(UnlitShader);
            MakeTransparent(m);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }
    }
}
