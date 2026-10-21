using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SecretJenga
{
    /// <summary>
    /// Procedural mesh library used by the editor generator (meshes are saved as assets + exported to OBJ)
    /// and as a runtime fallback. All meshes are centered on their pivot unless stated otherwise.
    /// </summary>
    public static class ProceduralMeshes
    {
        class Builder
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<List<int>> subs = new List<List<int>>();

            public Builder(int submeshes) { for (int i = 0; i < submeshes; i++) subs.Add(new List<int>()); }

            public int Add(Vector3 p, Vector3 normal, Vector2 tc)
            {
                v.Add(p); n.Add(normal); uv.Add(tc);
                return v.Count - 1;
            }

            /// <summary>Adds a triangle, flipping winding so that it faces along `outward`.</summary>
            public void Tri(int sub, int a, int b, int c, Vector3 outward)
            {
                var cr = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
                if (Vector3.Dot(cr, outward) < 0f) { var t = b; b = c; c = t; }
                var l = subs[sub];
                l.Add(a); l.Add(b); l.Add(c);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name };
                if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                m.SetVertices(v);
                m.SetNormals(n);
                m.SetUVs(0, uv);
                m.subMeshCount = subs.Count;
                for (int i = 0; i < subs.Count; i++) m.SetTriangles(subs[i], i);
                m.RecalculateBounds();
                m.RecalculateTangents();
                return m;
            }
        }

        // ------------------------------------------------------------------ Beveled box
        /// <summary>
        /// Chamfered box. Submesh 0 = long faces + bevels, submesh 1 = the two end caps (±X) when endCapSubmesh is true.
        /// UVs: grain runs along local X. uvWorld = world units covered by one UV unit (u, v).
        /// </summary>
        public static Mesh BeveledBox(Vector3 size, float bevel, bool endCapSubmesh, Vector2 uvWorld, string name = "BeveledBox")
        {
            var b = new Builder(endCapSubmesh ? 2 : 1);
            Vector3 h = size * 0.5f;
            bevel = Mathf.Min(bevel, Mathf.Min(h.x, Mathf.Min(h.y, h.z)) * 0.9f);
            Vector3 e = h - Vector3.one * bevel;

            Vector2 UV(Vector3 p, Vector3 nn)
            {
                float ax = Mathf.Abs(nn.x), ay = Mathf.Abs(nn.y), az = Mathf.Abs(nn.z);
                if (ax > ay + 0.01f && ax > az + 0.01f) return new Vector2(p.z / uvWorld.y + 0.5f, p.y / uvWorld.y + 0.5f);
                if (ay >= az) return new Vector2(p.x / uvWorld.x + 0.5f, p.z / uvWorld.y + 0.5f);
                return new Vector2(p.x / uvWorld.x + 0.5f, p.y / uvWorld.y + 0.5f);
            }

            void Quad(int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 nn)
            {
                int i0 = b.Add(p0, nn, UV(p0, nn)), i1 = b.Add(p1, nn, UV(p1, nn)), i2 = b.Add(p2, nn, UV(p2, nn)), i3 = b.Add(p3, nn, UV(p3, nn));
                b.Tri(sub, i0, i1, i2, nn);
                b.Tri(sub, i0, i2, i3, nn);
            }

            Vector3 Make(int a, float va, int c1, float v1, int c2, float v2)
            {
                var p = Vector3.zero;
                p[a] = va; p[c1] = v1; p[c2] = v2;
                return p;
            }

            // 6 faces
            for (int a = 0; a < 3; a++)
            for (int s = -1; s <= 1; s += 2)
            {
                int c1 = (a + 1) % 3, c2 = (a + 2) % 3;
                var nn = Vector3.zero; nn[a] = s;
                int sub = (endCapSubmesh && a == 0) ? 1 : 0;
                Quad(sub,
                    Make(a, s * h[a], c1, -e[c1], c2, -e[c2]),
                    Make(a, s * h[a], c1, e[c1], c2, -e[c2]),
                    Make(a, s * h[a], c1, e[c1], c2, e[c2]),
                    Make(a, s * h[a], c1, -e[c1], c2, e[c2]), nn);
            }

            // 12 edge chamfers
            for (int a = 0; a < 3; a++)
            {
                int bAx = (a + 1) % 3, c = (a + 2) % 3;
                for (int sa = -1; sa <= 1; sa += 2)
                for (int sb = -1; sb <= 1; sb += 2)
                {
                    var nn = Vector3.zero; nn[a] = sa; nn[bAx] = sb; nn.Normalize();
                    Vector3 P1(float cv) { var p = Vector3.zero; p[a] = sa * h[a]; p[bAx] = sb * e[bAx]; p[c] = cv; return p; }
                    Vector3 P2(float cv) { var p = Vector3.zero; p[a] = sa * e[a]; p[bAx] = sb * h[bAx]; p[c] = cv; return p; }
                    Quad(0, P1(-e[c]), P1(e[c]), P2(e[c]), P2(-e[c]), nn);
                }
            }

            // 8 corners
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var nn = new Vector3(sx, sy, sz).normalized;
                var p0 = new Vector3(sx * h.x, sy * e.y, sz * e.z);
                var p1 = new Vector3(sx * e.x, sy * h.y, sz * e.z);
                var p2 = new Vector3(sx * e.x, sy * e.y, sz * h.z);
                int i0 = b.Add(p0, nn, UV(p0, nn)), i1 = b.Add(p1, nn, UV(p1, nn)), i2 = b.Add(p2, nn, UV(p2, nn));
                b.Tri(0, i0, i1, i2, nn);
            }

            return b.Build(name);
        }

        public static Mesh JengaBlock() =>
            BeveledBox(JengaDims.BlockSize, JengaDims.Bevel, true, new Vector2(JengaDims.BlockLength, JengaDims.BlockLength * 0.5f), "SJ_JengaBlock");

        // ------------------------------------------------------------------ Lathe (turned parts)
        /// <summary>Revolves a (radius, height) profile around Y. Profile ordered bottom to top.</summary>
        public static Mesh Lathe(Vector2[] profile, int segments, bool capBottom, bool capTop, string name = "Lathe", float uvV = 1f)
        {
            var b = new Builder(1);
            int rings = profile.Length;
            float totalLen = 0f;
            var acc = new float[rings];
            for (int i = 1; i < rings; i++) { totalLen += Vector2.Distance(profile[i], profile[i - 1]); acc[i] = totalLen; }

            var start = b.v.Count;
            for (int i = 0; i < rings; i++)
            {
                var prev = profile[Mathf.Max(0, i - 1)];
                var next = profile[Mathf.Min(rings - 1, i + 1)];
                var t = (next - prev).normalized;
                var n2 = new Vector2(t.y, -t.x);
                for (int j = 0; j <= segments; j++)
                {
                    float ang = j / (float)segments * Mathf.PI * 2f;
                    float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);
                    var p = new Vector3(profile[i].x * cs, profile[i].y, profile[i].x * sn);
                    var nn = new Vector3(n2.x * cs, n2.y, n2.x * sn).normalized;
                    b.Add(p, nn, new Vector2(j / (float)segments, acc[i] / Mathf.Max(0.001f, totalLen) * uvV));
                }
            }
            for (int i = 0; i < rings - 1; i++)
            for (int j = 0; j < segments; j++)
            {
                int a = start + i * (segments + 1) + j;
                int c = a + segments + 1;
                var outward = (b.n[a] + b.n[a + 1] + b.n[c] + b.n[c + 1]);
                if (outward.sqrMagnitude < 1e-6f) outward = new Vector3(b.v[a].x, 0, b.v[a].z);
                b.Tri(0, a, c, a + 1, outward);
                b.Tri(0, a + 1, c, c + 1, outward);
            }

            void Cap(Vector2 pr, float dir)
            {
                if (pr.x <= 0.0001f) return;
                var nn = new Vector3(0, dir, 0);
                int center = b.Add(new Vector3(0, pr.y, 0), nn, new Vector2(0.5f, 0.5f));
                int first = b.v.Count;
                for (int j = 0; j <= segments; j++)
                {
                    float ang = j / (float)segments * Mathf.PI * 2f;
                    b.Add(new Vector3(pr.x * Mathf.Cos(ang), pr.y, pr.x * Mathf.Sin(ang)), nn, new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang)));
                }
                for (int j = 0; j < segments; j++) b.Tri(0, center, first + j, first + j + 1, nn);
            }
            if (capBottom) Cap(profile[0], -1f);
            if (capTop) Cap(profile[rings - 1], 1f);
            return b.Build(name);
        }

        // ------------------------------------------------------------------ Sphere
        public static Mesh UVSphere(float radius, int lat, int lon, string name = "Sphere")
        {
            var b = new Builder(1);
            for (int i = 0; i <= lat; i++)
            {
                float th = i / (float)lat * Mathf.PI;
                for (int j = 0; j <= lon; j++)
                {
                    float ph = j / (float)lon * Mathf.PI * 2f;
                    var nn = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    b.Add(nn * radius, nn, new Vector2(j / (float)lon, 1f - i / (float)lat));
                }
            }
            for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j, c = a + lon + 1;
                var o = b.n[a] + b.n[c + 1];
                if (i != 0) b.Tri(0, a, c, a + 1, o);
                if (i != lat - 1) b.Tri(0, a + 1, c, c + 1, o);
            }
            return b.Build(name);
        }

        // ------------------------------------------------------------------ Plane (XZ, facing up)
        public static Mesh Plane(float sizeX, float sizeZ, float uvRepeatX, float uvRepeatZ, string name = "Plane")
        {
            var b = new Builder(1);
            float hx = sizeX * 0.5f, hz = sizeZ * 0.5f;
            int i0 = b.Add(new Vector3(-hx, 0, -hz), Vector3.up, new Vector2(0, 0));
            int i1 = b.Add(new Vector3(hx, 0, -hz), Vector3.up, new Vector2(uvRepeatX, 0));
            int i2 = b.Add(new Vector3(hx, 0, hz), Vector3.up, new Vector2(uvRepeatX, uvRepeatZ));
            int i3 = b.Add(new Vector3(-hx, 0, hz), Vector3.up, new Vector2(0, uvRepeatZ));
            b.Tri(0, i0, i1, i2, Vector3.up);
            b.Tri(0, i0, i2, i3, Vector3.up);
            return b.Build(name);
        }

        // ------------------------------------------------------------------ Table parts
        public static Mesh TableTop() => BeveledBox(new Vector3(14f, 0.5f, 9.5f), 0.12f, false, new Vector2(7f, 7f), "SJ_TableTop");

        public static Mesh TableLeg()
        {
            // turned leg, pivot at the top (y = 0) going down to -11
            var p = new[]
            {
                new Vector2(0.20f, -11.0f), new Vector2(0.26f, -10.85f), new Vector2(0.24f, -10.6f), new Vector2(0.20f, -10.2f),
                new Vector2(0.22f, -8.0f), new Vector2(0.26f, -6.0f), new Vector2(0.32f, -5.6f), new Vector2(0.36f, -5.3f),
                new Vector2(0.32f, -5.0f), new Vector2(0.28f, -4.6f), new Vector2(0.30f, -2.6f), new Vector2(0.38f, -2.2f),
                new Vector2(0.40f, -1.9f), new Vector2(0.36f, -1.6f), new Vector2(0.40f, -0.2f), new Vector2(0.40f, 0f),
            };
            return Lathe(p, 24, true, true, "SJ_TableLeg", 4f);
        }

        public static Mesh Apron(float length) => BeveledBox(new Vector3(length, 0.9f, 0.18f), 0.04f, false, new Vector2(4f, 4f), "SJ_Apron");

        public static Mesh LampShade()  // legacy (Secret Jenga)
        {
            var p = new[] { new Vector2(1.6f, -0.9f), new Vector2(1.62f, -0.85f), new Vector2(1.2f, -0.35f), new Vector2(0.45f, 0.1f), new Vector2(0.18f, 0.3f), new Vector2(0.18f, 0.5f) };
            return Lathe(p, 40, false, true, "SJ_LampShade");
        }

        public static Mesh Mug()
        {
            var p = new[] { new Vector2(0.0f, 0f), new Vector2(0.42f, 0f), new Vector2(0.46f, 0.05f), new Vector2(0.48f, 0.9f), new Vector2(0.5f, 0.98f), new Vector2(0.44f, 1.0f), new Vector2(0.42f, 0.2f), new Vector2(0.0f, 0.2f) };
            return Lathe(p, 28, false, false, "SJ_Mug");
        }

        // ------------------------------------------------------------------ Combining
        /// <summary>Merges meshes (each with a local transform) into one single-submesh mesh.</summary>
        public static Mesh Combine(string name, params (Mesh mesh, Matrix4x4 m)[] parts)
        {
            var ci = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].mesh, transform = parts[i].m, subMeshIndex = 0 };
            var mesh = new Mesh { name = name };
            mesh.CombineMeshes(ci, true, true);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        static Matrix4x4 TRS(Vector3 p, Vector3 euler) => Matrix4x4.TRS(p, Quaternion.Euler(euler), Vector3.one);

        // ------------------------------------------------------------------ Placeholder hand (pivot = palm center, fingers along +Z)
        public static Mesh Hand()
        {
            var palm = BeveledBox(new Vector3(1.1f, 0.34f, 1.05f), 0.12f, false, Vector2.one, "palm");
            var finger = BeveledBox(new Vector3(0.24f, 0.22f, 0.9f), 0.08f, false, Vector2.one, "finger");
            var thumb = BeveledBox(new Vector3(0.28f, 0.24f, 0.75f), 0.09f, false, Vector2.one, "thumb");
            return Combine("TLB_Hand",
                (palm, Matrix4x4.identity),
                (finger, TRS(new Vector3(-0.39f, -0.04f, 0.92f), new Vector3(14f, 0f, 0f))),
                (finger, TRS(new Vector3(-0.13f, -0.03f, 0.98f), new Vector3(10f, 0f, 0f))),
                (finger, TRS(new Vector3(0.13f, -0.03f, 0.96f), new Vector3(12f, 0f, 0f))),
                (finger, TRS(new Vector3(0.39f, -0.05f, 0.85f), new Vector3(18f, 0f, 0f))),
                (thumb, TRS(new Vector3(-0.66f, -0.06f, 0.2f), new Vector3(10f, -38f, 0f))));
        }

        // ------------------------------------------------------------------ Room pieces (THE LAST BLOCK)
        /// <summary>Solid cylinder centered on its pivot.</summary>
        public static Mesh Cylinder(float radius, float height, int segments, string name = "Cylinder")
        {
            float h = height * 0.5f;
            return Lathe(new[] { new Vector2(radius, -h), new Vector2(radius, h) }, segments, true, true, name, height / Mathf.Max(0.01f, radius * 6.283f));
        }

        /// <summary>Round table top with a softened edge. Top surface at y = 0.</summary>
        public static Mesh RoundTableTop(float radius, float thickness)
        {
            var p = new[]
            {
                new Vector2(0f, -thickness), new Vector2(radius - 0.25f, -thickness), new Vector2(radius - 0.05f, -thickness + 0.08f),
                new Vector2(radius, -thickness * 0.5f), new Vector2(radius - 0.05f, -0.06f), new Vector2(radius - 0.2f, 0f), new Vector2(0f, 0f)
            };
            var m = Lathe(p, 64, false, false, "TLB_TableTop", 1f);
            // planar UVs on the top so wood grain runs straight across
            var v = m.vertices;
            var uv = m.uv;
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2(v[i].x / 7f + 0.5f, v[i].z / 7f + 0.5f);
            m.uv = uv;
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Simple wooden chair, pivot on the floor under the seat center, facing +Z.</summary>
        public static Mesh Chair()
        {
            var seat = BeveledBox(new Vector3(7.2f, 0.6f, 6.6f), 0.15f, false, new Vector2(4f, 4f), "seat");
            var leg = BeveledBox(new Vector3(0.6f, 9f, 0.6f), 0.08f, false, new Vector2(2f, 4f), "leg");
            var post = BeveledBox(new Vector3(0.6f, 9.5f, 0.6f), 0.08f, false, new Vector2(2f, 4f), "post");
            var slat = BeveledBox(new Vector3(6.6f, 1.6f, 0.35f), 0.1f, false, new Vector2(4f, 4f), "slat");
            return Combine("TLB_Chair",
                (seat, TRS(new Vector3(0f, 9f, 0f), Vector3.zero)),
                (leg, TRS(new Vector3(-3.1f, 4.5f, 2.8f), Vector3.zero)),
                (leg, TRS(new Vector3(3.1f, 4.5f, 2.8f), Vector3.zero)),
                (leg, TRS(new Vector3(-3.1f, 4.5f, -2.8f), new Vector3(0f, 0f, 1.5f))),
                (leg, TRS(new Vector3(3.1f, 4.5f, -2.8f), new Vector3(0f, 0f, -1.5f))),
                (post, TRS(new Vector3(-3.1f, 13.9f, -3.0f), new Vector3(-6f, 0f, 0f))),
                (post, TRS(new Vector3(3.1f, 13.9f, -3.0f), new Vector3(-6f, 0f, 0f))),
                (slat, TRS(new Vector3(0f, 17.2f, -3.35f), new Vector3(-6f, 0f, 0f))),
                (slat, TRS(new Vector3(0f, 13.8f, -3.0f), new Vector3(-6f, 0f, 0f))));
        }

        /// <summary>Enamelled industrial lamp shade (wide cone), pivot at the top socket.</summary>
        public static Mesh IndustrialShade()
        {
            var p = new[]
            {
                new Vector2(2.6f, -1.55f), new Vector2(2.68f, -1.5f), new Vector2(2.5f, -1.35f), new Vector2(1.4f, -0.55f),
                new Vector2(0.55f, -0.15f), new Vector2(0.42f, 0.0f), new Vector2(0.42f, 0.55f), new Vector2(0.2f, 0.62f)
            };
            return Lathe(p, 48, false, true, "TLB_IndustrialShade");
        }

        /// <summary>Straight pipe along +X, length centered on the pivot.</summary>
        public static Mesh Pipe(float radius, float length)
        {
            var m = Cylinder(radius, length, 16, "TLB_Pipe");
            var v = m.vertices; var n = m.normals;
            var r = Quaternion.Euler(0f, 0f, 90f);
            for (int i = 0; i < v.Length; i++) { v[i] = r * v[i]; n[i] = r * n[i]; }
            m.vertices = v; m.normals = n;
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }
}
