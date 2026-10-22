using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// Procedural props for the old frontier saloon (used by the project generator, 1 unit = 5 cm).
    /// Everything is built from ProceduralMeshes primitives so the project needs no imported environment art.
    /// </summary>
    public static class SaloonMeshes
    {
        static Matrix4x4 TRS(Vector3 p, Vector3 euler, Vector3? s = null) => Matrix4x4.TRS(p, Quaternion.Euler(euler), s ?? Vector3.one);

        /// <summary>Ring (torus) of the given radius and tube thickness, around Y.</summary>
        public static Mesh Ring(float radius, float tube, int segments, string name)
        {
            const int n = 10;
            var p = new Vector2[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float a = -Mathf.PI * 0.5f + i / (float)n * Mathf.PI * 2f;
                p[i] = new Vector2(radius + Mathf.Cos(a) * tube, Mathf.Sin(a) * tube);
            }
            return ProceduralMeshes.Lathe(p, segments, false, false, name);
        }

        /// <summary>Whiskey barrel standing on its end (pivot at the bottom). Height ~ 18 (90 cm).</summary>
        public static Mesh Barrel()
        {
            var p = new[]
            {
                new Vector2(0f, 0f), new Vector2(4.6f, 0f), new Vector2(4.8f, 0.4f), new Vector2(5.4f, 4.5f), new Vector2(5.8f, 9f),
                new Vector2(5.4f, 13.5f), new Vector2(4.8f, 17.6f), new Vector2(4.6f, 18f), new Vector2(0f, 18f)
            };
            return ProceduralMeshes.Lathe(p, 28, false, false, "TLB_Barrel", 2f);
        }

        /// <summary>The iron hoops of a barrel, matching Barrel().</summary>
        public static Mesh BarrelHoops()
        {
            return ProceduralMeshes.Combine("TLB_BarrelHoops",
                (Ring(4.95f, 0.18f, 28, "h"), TRS(new Vector3(0, 1.6f, 0), Vector3.zero)),
                (Ring(5.62f, 0.18f, 28, "h"), TRS(new Vector3(0, 6.2f, 0), Vector3.zero)),
                (Ring(5.62f, 0.18f, 28, "h"), TRS(new Vector3(0, 11.8f, 0), Vector3.zero)),
                (Ring(4.95f, 0.18f, 28, "h"), TRS(new Vector3(0, 16.4f, 0), Vector3.zero)));
        }

        /// <summary>A bottle, pivot at the bottom (height ~ 6 = 30 cm).</summary>
        public static Mesh Bottle(float scale = 1f, string name = "TLB_Bottle")
        {
            var p = new[]
            {
                new Vector2(0f, 0f), new Vector2(0.75f, 0f), new Vector2(0.8f, 0.15f), new Vector2(0.8f, 3.6f), new Vector2(0.62f, 4.3f),
                new Vector2(0.28f, 4.8f), new Vector2(0.26f, 5.8f), new Vector2(0.32f, 5.9f), new Vector2(0.3f, 6.1f), new Vector2(0f, 6.1f)
            };
            for (int i = 0; i < p.Length; i++) p[i] *= scale;
            return ProceduralMeshes.Lathe(p, 16, false, false, name);
        }

        /// <summary>Shot glass, pivot at the bottom.</summary>
        public static Mesh ShotGlass()
        {
            var p = new[] { new Vector2(0f, 0f), new Vector2(0.45f, 0f), new Vector2(0.55f, 1.1f), new Vector2(0.5f, 1.1f), new Vector2(0.38f, 0.25f), new Vector2(0f, 0.25f) };
            return ProceduralMeshes.Lathe(p, 14, false, false, "TLB_ShotGlass");
        }

        /// <summary>Wagon wheel chandelier: rim, hub and spokes (pivot at the centre, wheel lying flat).</summary>
        public static Mesh WagonWheel(float radius)
        {
            var spoke = ProceduralMeshes.BeveledBox(new Vector3(radius - 1.2f, 0.45f, 0.55f), 0.08f, false, new Vector2(4f, 4f), "spoke");
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>
            {
                (Ring(radius, 0.55f, 48, "rim"), Matrix4x4.identity),
                (ProceduralMeshes.Cylinder(1.3f, 1.6f, 20, "hub"), Matrix4x4.identity),
            };
            for (int i = 0; i < 12; i++)
            {
                float a = i * 30f;
                var dir = Quaternion.Euler(0, a, 0) * Vector3.right;
                parts.Add((spoke, TRS(dir * (radius * 0.5f + 0.5f), new Vector3(0, -a, 0))));
            }
            return ProceduralMeshes.Combine("TLB_WagonWheel", parts.ToArray());
        }

        /// <summary>Candle holders + candles on the wheel rim (count around a radius). Flames are a separate mesh.</summary>
        public static Mesh Candles(float radius, int count)
        {
            var cup = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.55f, 0f), new Vector2(0.6f, 0.3f), new Vector2(0.3f, 0.4f), new Vector2(0f, 0.4f) }, 12, false, false, "cup");
            var candle = ProceduralMeshes.Cylinder(0.22f, 1.4f, 10, "candle");
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>();
            for (int i = 0; i < count; i++)
            {
                var d = Quaternion.Euler(0, i * 360f / count + 15f, 0) * Vector3.right * radius;
                parts.Add((cup, TRS(d + Vector3.up * 0.4f, Vector3.zero)));
                parts.Add((candle, TRS(d + Vector3.up * 1.5f, Vector3.zero)));
            }
            return ProceduralMeshes.Combine("TLB_Candles", parts.ToArray());
        }

        public static Mesh Flames(float radius, int count)
        {
            var flame = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.16f, 0.12f), new Vector2(0.2f, 0.32f), new Vector2(0.1f, 0.6f), new Vector2(0f, 0.75f) }, 10, false, false, "flame");
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>();
            for (int i = 0; i < count; i++)
            {
                var d = Quaternion.Euler(0, i * 360f / count + 15f, 0) * Vector3.right * radius;
                parts.Add((flame, TRS(d + Vector3.up * 2.25f, Vector3.zero)));
            }
            return ProceduralMeshes.Combine("TLB_Flames", parts.ToArray());
        }

        /// <summary>Wall oil lantern: bracket + glass chimney (pivot at the wall mount, sticking out along +Z).</summary>
        public static Mesh LanternFrame()
        {
            var bracket = ProceduralMeshes.BeveledBox(new Vector3(0.35f, 0.35f, 2.6f), 0.06f, false, Vector2.one, "bracket");
            var plate = ProceduralMeshes.BeveledBox(new Vector3(1.6f, 2.4f, 0.25f), 0.06f, false, Vector2.one, "plate");
            var cap = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(1.0f, 0f), new Vector2(0.5f, 0.6f), new Vector2(0.15f, 0.9f), new Vector2(0f, 0.9f) }, 14, true, false, "cap");
            var foot = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.9f, 0f), new Vector2(0.9f, 0.5f), new Vector2(0.5f, 0.7f), new Vector2(0f, 0.7f) }, 14, true, true, "foot");
            return ProceduralMeshes.Combine("TLB_LanternFrame",
                (plate, Matrix4x4.identity),
                (bracket, TRS(new Vector3(0, 0.6f, 1.3f), Vector3.zero)),
                (foot, TRS(new Vector3(0, -1.6f, 2.6f), Vector3.zero)),
                (cap, TRS(new Vector3(0, 1.5f, 2.6f), Vector3.zero)));
        }

        public static Mesh LanternGlass()
        {
            var g = ProceduralMeshes.Lathe(new[] { new Vector2(0f, -0.9f), new Vector2(0.55f, -0.9f), new Vector2(0.85f, -0.3f), new Vector2(0.8f, 0.3f), new Vector2(0.45f, 1.0f), new Vector2(0.4f, 1.6f), new Vector2(0f, 1.6f) }, 16, false, false, "glass");
            return ProceduralMeshes.Combine("TLB_LanternGlass", (g, TRS(new Vector3(0, -0.4f, 2.6f), Vector3.zero)));
        }

        /// <summary>Saloon swinging half door (pivot at the hinge side, bottom). Width w, height h.</summary>
        public static Mesh BatwingDoor(float w, float h)
        {
            var frame = ProceduralMeshes.BeveledBox(new Vector3(w, 0.9f, 0.5f), 0.08f, false, new Vector2(4f, 4f), "rail");
            var stile = ProceduralMeshes.BeveledBox(new Vector3(0.8f, h, 0.5f), 0.08f, false, new Vector2(4f, 4f), "stile");
            var slat = ProceduralMeshes.BeveledBox(new Vector3(0.5f, h - 2.2f, 0.25f), 0.05f, false, new Vector2(4f, 4f), "slat");
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>
            {
                (frame, TRS(new Vector3(w * 0.5f, 0.45f, 0), Vector3.zero)),
                (frame, TRS(new Vector3(w * 0.5f, h - 0.45f, 0), Vector3.zero)),
                (stile, TRS(new Vector3(0.4f, h * 0.5f, 0), Vector3.zero)),
                (stile, TRS(new Vector3(w - 0.4f, h * 0.5f, 0), Vector3.zero)),
            };
            int n = Mathf.Max(2, Mathf.RoundToInt(w / 1.1f));
            for (int i = 1; i < n; i++) parts.Add((slat, TRS(new Vector3(w * i / n, h * 0.5f, 0), Vector3.zero)));
            return ProceduralMeshes.Combine("TLB_Batwing", parts.ToArray());
        }

        /// <summary>Window frame with a cross bar (pivot at the centre, in the XY plane, facing -Z/+Z).</summary>
        public static Mesh WindowFrame(float w, float h)
        {
            var side = ProceduralMeshes.BeveledBox(new Vector3(0.9f, h + 1.8f, 1.2f), 0.1f, false, new Vector2(4f, 4f), "side");
            var top = ProceduralMeshes.BeveledBox(new Vector3(w + 1.8f, 0.9f, 1.2f), 0.1f, false, new Vector2(4f, 4f), "top");
            var barV = ProceduralMeshes.BeveledBox(new Vector3(0.35f, h, 0.5f), 0.05f, false, Vector2.one, "barV");
            var barH = ProceduralMeshes.BeveledBox(new Vector3(w, 0.35f, 0.5f), 0.05f, false, Vector2.one, "barH");
            var sill = ProceduralMeshes.BeveledBox(new Vector3(w + 3f, 0.6f, 2.2f), 0.1f, false, new Vector2(4f, 4f), "sill");
            return ProceduralMeshes.Combine("TLB_WindowFrame",
                (side, TRS(new Vector3(-w * 0.5f - 0.45f, 0, 0), Vector3.zero)),
                (side, TRS(new Vector3(w * 0.5f + 0.45f, 0, 0), Vector3.zero)),
                (top, TRS(new Vector3(0, h * 0.5f + 0.45f, 0), Vector3.zero)),
                (top, TRS(new Vector3(0, -h * 0.5f - 0.45f, 0), Vector3.zero)),
                (sill, TRS(new Vector3(0, -h * 0.5f - 1.0f, -0.6f), Vector3.zero)),
                (barV, Matrix4x4.identity),
                (barH, TRS(new Vector3(0, h * 0.12f, 0), Vector3.zero)));
        }

        /// <summary>Upright piano (pivot at the floor, back against the wall along +Z, keys facing -Z).</summary>
        public static Mesh PianoBody()
        {
            var body = ProceduralMeshes.BeveledBox(new Vector3(29f, 25f, 11f), 0.4f, false, new Vector2(10f, 10f), "body");
            var shelf = ProceduralMeshes.BeveledBox(new Vector3(28f, 1.2f, 5.5f), 0.2f, false, new Vector2(10f, 10f), "keybed");
            var top = ProceduralMeshes.BeveledBox(new Vector3(30f, 1f, 12f), 0.3f, false, new Vector2(10f, 10f), "lid");
            var leg = ProceduralMeshes.BeveledBox(new Vector3(1.4f, 13.5f, 1.4f), 0.2f, false, new Vector2(4f, 4f), "leg");
            return ProceduralMeshes.Combine("TLB_Piano",
                (body, TRS(new Vector3(0, 12.5f, 0), Vector3.zero)),
                (top, TRS(new Vector3(0, 25.5f, 0), Vector3.zero)),
                (shelf, TRS(new Vector3(0, 14f, -7.5f), Vector3.zero)),
                (leg, TRS(new Vector3(-12.6f, 6.75f, -9.2f), Vector3.zero)),
                (leg, TRS(new Vector3(12.6f, 6.75f, -9.2f), Vector3.zero)));
        }

        public static Mesh PianoKeys() => ProceduralMeshes.BeveledBox(new Vector3(26.5f, 0.5f, 3.2f), 0.06f, false, Vector2.one, "TLB_PianoKeys");

        /// <summary>Small round side table: top + turned leg (pivot at the floor). Top at height h.</summary>
        public static Mesh SideTable(float radius, float h)
        {
            var top = ProceduralMeshes.Lathe(new[] { new Vector2(0f, -0.6f), new Vector2(radius - 0.2f, -0.6f), new Vector2(radius, -0.4f), new Vector2(radius, -0.1f), new Vector2(radius - 0.2f, 0f), new Vector2(0f, 0f) }, 32, false, false, "top", 2f);
            var leg = ProceduralMeshes.Lathe(new[]
            {
                new Vector2(3.2f, 0f), new Vector2(3.2f, 0.5f), new Vector2(1.2f, 1.2f), new Vector2(0.8f, 2.5f), new Vector2(0.65f, h - 3f),
                new Vector2(1.1f, h - 2f), new Vector2(1.6f, h - 0.6f)
            }, 20, true, true, "leg", 3f);
            return ProceduralMeshes.Combine("TLB_SideTable", (top, TRS(new Vector3(0, h, 0), Vector3.zero)), (leg, Matrix4x4.identity));
        }

        /// <summary>Cowboy hat hanging on a wall peg (pivot at the brim centre).</summary>
        public static Mesh Hat()
        {
            var crown = ProceduralMeshes.Lathe(new[] { new Vector2(1.95f, 0f), new Vector2(2.0f, 0.6f), new Vector2(1.9f, 1.9f), new Vector2(1.4f, 2.5f), new Vector2(0f, 2.3f) }, 24, false, false, "crown");
            var brim = ProceduralMeshes.Lathe(new[] { new Vector2(1.9f, 0.05f), new Vector2(3.0f, 0.2f), new Vector2(3.7f, 0.75f), new Vector2(3.75f, 0.85f), new Vector2(3.6f, 0.82f), new Vector2(2.95f, 0.12f), new Vector2(1.9f, -0.05f) }, 32, false, false, "brim");
            return ProceduralMeshes.Combine("TLB_Hat", (crown, Matrix4x4.identity), (brim, Matrix4x4.identity));
        }

        public static Mesh HatBand() => ProceduralMeshes.Lathe(new[] { new Vector2(2.02f, 0.15f), new Vector2(2.06f, 0.2f), new Vector2(2.06f, 0.55f), new Vector2(2.02f, 0.6f) }, 24, false, false, "TLB_HatBand");

        /// <summary>A stack of poker chips (pivot at the bottom). 1 chip = 3.3 mm.</summary>
        public static Mesh ChipStack(int chips)
        {
            var chip = ProceduralMeshes.Cylinder(0.78f, 0.066f, 16, "chip");
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>();
            var r = new System.Random(chips * 31);
            for (int i = 0; i < chips; i++)
                parts.Add((chip, TRS(new Vector3((float)r.NextDouble() * 0.06f, 0.033f + i * 0.068f, (float)r.NextDouble() * 0.06f), Vector3.zero)));
            return ProceduralMeshes.Combine("TLB_Chips" + chips, parts.ToArray());
        }

        /// <summary>A playing card lying flat (pivot at its centre).</summary>
        public static Mesh Card() => ProceduralMeshes.BeveledBox(new Vector3(1.26f, 0.02f, 1.76f), 0.005f, false, new Vector2(1.26f, 1.76f), "TLB_Card");

        /// <summary>Brass spittoon (pivot at the bottom).</summary>
        public static Mesh Spittoon() => ProceduralMeshes.Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(1.9f, 0f), new Vector2(2.4f, 0.8f), new Vector2(2.45f, 1.9f), new Vector2(1.6f, 2.6f), new Vector2(1.0f, 2.8f),
            new Vector2(1.4f, 3.3f), new Vector2(2.0f, 3.5f), new Vector2(1.8f, 3.55f), new Vector2(0.9f, 3.0f), new Vector2(0f, 2.9f)
        }, 28, false, false, "TLB_Spittoon");

        /// <summary>Old rifle (pivot at its centre, lying along X).</summary>
        public static Mesh Rifle()
        {
            var stock = ProceduralMeshes.BeveledBox(new Vector3(6.5f, 1.3f, 0.7f), 0.2f, false, new Vector2(4f, 4f), "stock");
            var body = ProceduralMeshes.BeveledBox(new Vector3(4f, 0.7f, 0.6f), 0.12f, false, new Vector2(4f, 4f), "body");
            var barrel = ProceduralMeshes.Pipe(0.17f, 12f);
            var fore = ProceduralMeshes.BeveledBox(new Vector3(6f, 0.55f, 0.5f), 0.15f, false, new Vector2(4f, 4f), "fore");
            return ProceduralMeshes.Combine("TLB_Rifle",
                (stock, TRS(new Vector3(-8.5f, -0.35f, 0f), new Vector3(0, 0, 6f))),
                (body, TRS(new Vector3(-3.5f, 0f, 0f), Vector3.zero)),
                (fore, TRS(new Vector3(1.5f, -0.1f, 0f), Vector3.zero)),
                (barrel, TRS(new Vector3(4f, 0.2f, 0f), Vector3.zero)));
        }

        /// <summary>Small hanging oil lamp shade + chimney (pivot at the top hook).</summary>
        public static Mesh PendantLamp()
        {
            var shade = ProceduralMeshes.Lathe(new[] { new Vector2(0.2f, 0f), new Vector2(0.5f, -0.3f), new Vector2(1.6f, -1.2f), new Vector2(1.7f, -1.35f), new Vector2(1.55f, -1.32f), new Vector2(0.45f, -0.45f) }, 24, false, false, "shade");
            var chain = ProceduralMeshes.Cylinder(0.06f, 6f, 6, "chain");
            return ProceduralMeshes.Combine("TLB_PendantLamp", (shade, Matrix4x4.identity), (chain, TRS(new Vector3(0, 3f, 0), Vector3.zero)));
        }

        public static Mesh PendantGlass() => ProceduralMeshes.Lathe(new[] { new Vector2(0f, -0.6f), new Vector2(0.55f, -0.75f), new Vector2(0.7f, -1.3f), new Vector2(0.5f, -1.9f), new Vector2(0f, -2.0f) }, 16, false, false, "TLB_PendantGlass");

        /// <summary>A soft light beam: a quad 1 x 1 in XY (pivot centre) - stretched by the transform.</summary>
        public static Mesh ShaftQuad() => Quad(1f, 1f, "TLB_ShaftQuad");

        /// <summary>Hanging sign / poster quad (pivot at the centre, facing -Z). Size in units.</summary>
        public static Mesh Quad(float w, float h, string name)
        {
            var m = ProceduralMeshes.Plane(w, h, 1f, 1f, name);
            var v = m.vertices; var n = m.normals;
            var r = Quaternion.Euler(-90f, 0f, 0f);   // XZ plane facing +Y -> XY plane facing -Z
            for (int i = 0; i < v.Length; i++) { v[i] = r * v[i]; n[i] = r * n[i]; }
            m.vertices = v; m.normals = n;
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
