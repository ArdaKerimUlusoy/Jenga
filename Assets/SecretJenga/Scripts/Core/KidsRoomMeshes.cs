using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// Procedural furniture and toys for the kids' bedroom (1 unit = 5 cm). Everything is built from boxes, lathes
    /// and spheres so it needs no model files. Pivots sit on the floor unless noted.
    /// </summary>
    public static class KidsRoomMeshes
    {
        static Matrix4x4 TRS(Vector3 p, Vector3 euler, Vector3? s = null) => Matrix4x4.TRS(p, Quaternion.Euler(euler), s ?? Vector3.one);
        static Mesh Box(Vector3 size, float bevel = 0.15f, float uv = 8f) => ProceduralMeshes.BeveledBox(size, bevel, false, new Vector2(uv, uv), "box");
        static Mesh Ball(float r, int lat = 12, int lon = 18) => ProceduralMeshes.UVSphere(r, lat, lon, "ball");

        // ------------------------------------------------------------------ flat round disc with planar UVs (rug)
        public static Mesh Disc(float radius, float thickness, int segments, string name)
        {
            var p = new[]
            {
                new Vector2(0f, 0f), new Vector2(radius - 0.3f, 0f), new Vector2(radius, thickness * 0.4f),
                new Vector2(radius - 0.2f, thickness), new Vector2(0f, thickness)
            };
            var m = ProceduralMeshes.Lathe(p, segments, false, false, name);
            var v = m.vertices;
            var uv = m.uv;
            for (int i = 0; i < v.Length; i++) uv[i] = new Vector2(v[i].x / (radius * 2f) + 0.5f, v[i].z / (radius * 2f) + 0.5f);
            m.uv = uv;
            m.RecalculateTangents();
            return m;
        }

        // ------------------------------------------------------------------ bunk bed (along +Z, 21 x 42 footprint, pivot on the floor at the centre)
        public static Mesh BunkFrame()
        {
            var post = Box(new Vector3(1.6f, 34f, 1.6f), 0.25f);
            var railL = Box(new Vector3(1.2f, 2.2f, 40f), 0.2f);
            var railW = Box(new Vector3(20f, 2.2f, 1.2f), 0.2f);
            var board = Box(new Vector3(20f, 8f, 1.0f), 0.3f);
            var slats = Box(new Vector3(19f, 0.6f, 39f), 0.1f);
            var ladderRail = Box(new Vector3(0.8f, 30f, 0.8f), 0.15f);
            var rung = Box(new Vector3(0.6f, 0.6f, 6f), 0.1f);
            var guard = Box(new Vector3(1.0f, 3.4f, 26f), 0.25f);
            float hx = 9.9f, hz = 20.4f;
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>
            {
                (post, TRS(new Vector3(-hx, 17f, -hz), Vector3.zero)), (post, TRS(new Vector3(hx, 17f, -hz), Vector3.zero)),
                (post, TRS(new Vector3(-hx, 17f, hz), Vector3.zero)), (post, TRS(new Vector3(hx, 17f, hz), Vector3.zero)),
            };
            foreach (var y in new[] { 5.2f, 23.2f })
            {
                parts.Add((railL, TRS(new Vector3(-hx, y, 0f), Vector3.zero)));
                parts.Add((railL, TRS(new Vector3(hx, y, 0f), Vector3.zero)));
                parts.Add((board, TRS(new Vector3(0f, y + 3f, -hz), Vector3.zero)));
                parts.Add((board, TRS(new Vector3(0f, y + 3f, hz), Vector3.zero)));
                parts.Add((slats, TRS(new Vector3(0f, y + 0.5f, 0f), Vector3.zero)));
            }
            // top bunk guard rail on the room side (+X) and the ladder at the foot end
            parts.Add((guard, TRS(new Vector3(hx, 29f, -3f), Vector3.zero)));
            parts.Add((ladderRail, TRS(new Vector3(hx + 1.6f, 15f, hz - 9f), new Vector3(0f, 0f, -6f))));
            parts.Add((ladderRail, TRS(new Vector3(hx + 1.6f, 15f, hz - 2f), new Vector3(0f, 0f, -6f))));
            for (int i = 0; i < 6; i++)
                parts.Add((rung, TRS(new Vector3(hx + 1.6f + (i * 4.6f - 13f) * -0.1f, 3.5f + i * 4.6f, hz - 5.5f), Vector3.zero)));
            return ProceduralMeshes.Combine("KR_BunkFrame", parts.ToArray());
        }

        public static Mesh Mattress() => ProceduralMeshes.BeveledBox(new Vector3(18.6f, 2.8f, 38.6f), 0.9f, false, new Vector2(10f, 10f), "KR_Mattress");
        public static Mesh Blanket() => ProceduralMeshes.BeveledBox(new Vector3(19.6f, 1.0f, 28f), 0.45f, false, new Vector2(19.6f, 19.6f), "KR_Blanket");
        public static Mesh Pillow() => ProceduralMeshes.Combine("KR_Pillow",
            (ProceduralMeshes.UVSphere(1f, 10, 16, "p"), TRS(Vector3.zero, Vector3.zero, new Vector3(7.5f, 1.8f, 4.2f))));

        // ------------------------------------------------------------------ toy shelf (30 wide, 9 deep, 34 tall, back to +Z)
        public static Mesh ToyShelf()
        {
            var side = Box(new Vector3(1.2f, 34f, 9f), 0.2f);
            var shelf = Box(new Vector3(29f, 1.0f, 8.6f), 0.15f);
            var back = Box(new Vector3(29f, 34f, 0.5f), 0.1f);
            var parts = new System.Collections.Generic.List<(Mesh, Matrix4x4)>
            {
                (side, TRS(new Vector3(-14.5f, 17f, 0f), Vector3.zero)), (side, TRS(new Vector3(14.5f, 17f, 0f), Vector3.zero)),
                (back, TRS(new Vector3(0f, 17f, 4.3f), Vector3.zero)),
            };
            foreach (var y in new[] { 0.6f, 11.5f, 22.5f, 33.5f }) parts.Add((shelf, TRS(new Vector3(0f, y, 0f), Vector3.zero)));
            return ProceduralMeshes.Combine("KR_ToyShelf", parts.ToArray());
        }

        public static Mesh Cube(float s) => ProceduralMeshes.BeveledBox(Vector3.one * s, s * 0.12f, false, new Vector2(s, s), "KR_Cube");
        public static Mesh Book() => ProceduralMeshes.BeveledBox(new Vector3(1.3f, 6f, 4.6f), 0.15f, false, new Vector2(4f, 4f), "KR_Book");

        // ------------------------------------------------------------------ teddy bear (sitting, ~13 tall, facing +Z)
        public static Mesh Teddy()
        {
            var s = Ball(1f, 12, 18);
            return ProceduralMeshes.Combine("KR_Teddy",
                (s, TRS(new Vector3(0f, 4.2f, 0f), Vector3.zero, new Vector3(4.2f, 4.8f, 3.8f))),        // body
                (s, TRS(new Vector3(0f, 10.4f, 0.3f), Vector3.zero, new Vector3(3.4f, 3.2f, 3.1f))),     // head
                (s, TRS(new Vector3(-2.5f, 12.9f, 0f), Vector3.zero, new Vector3(1.2f, 1.2f, 0.7f))),    // ears
                (s, TRS(new Vector3(2.5f, 12.9f, 0f), Vector3.zero, new Vector3(1.2f, 1.2f, 0.7f))),
                (s, TRS(new Vector3(0f, 9.8f, 3.0f), Vector3.zero, new Vector3(1.5f, 1.2f, 1.1f))),      // snout
                (s, TRS(new Vector3(-4.2f, 5.6f, 1.0f), new Vector3(0f, 0f, 35f), new Vector3(1.4f, 3.0f, 1.4f))),   // arms
                (s, TRS(new Vector3(4.2f, 5.6f, 1.0f), new Vector3(0f, 0f, -35f), new Vector3(1.4f, 3.0f, 1.4f))),
                (s, TRS(new Vector3(-2.3f, 1.4f, 3.2f), new Vector3(70f, 0f, 0f), new Vector3(1.7f, 2.8f, 1.6f))),  // legs
                (s, TRS(new Vector3(2.3f, 1.4f, 3.2f), new Vector3(70f, 0f, 0f), new Vector3(1.7f, 2.8f, 1.6f))));
        }

        public static Mesh TeddyFace()
        {
            var s = Ball(1f, 8, 12);
            return ProceduralMeshes.Combine("KR_TeddyFace",
                (s, TRS(new Vector3(-1.2f, 11.2f, 2.85f), Vector3.zero, Vector3.one * 0.42f)),
                (s, TRS(new Vector3(1.2f, 11.2f, 2.85f), Vector3.zero, Vector3.one * 0.42f)),
                (s, TRS(new Vector3(0f, 10.1f, 4.0f), Vector3.zero, new Vector3(0.6f, 0.45f, 0.4f))));
        }

        // ------------------------------------------------------------------ beanbag (soft drop, ~10 tall)
        public static Mesh Beanbag() => ProceduralMeshes.Lathe(new[]
        {
            new Vector2(0f, 0f), new Vector2(8.5f, 0.2f), new Vector2(10f, 2.2f), new Vector2(9.8f, 5f), new Vector2(8.2f, 7.8f),
            new Vector2(5.5f, 9.6f), new Vector2(2.5f, 10.4f), new Vector2(0f, 10.2f)
        }, 32, false, false, "KR_Beanbag");

        // ------------------------------------------------------------------ toy chest (22 x 12 x 12) with the lid a bit open
        public static Mesh ChestBody() => ProceduralMeshes.Combine("KR_ChestBody",
            (Box(new Vector3(22f, 11f, 12f), 0.5f), TRS(new Vector3(0f, 5.5f, 0f), Vector3.zero)));
        public static Mesh ChestLid() => ProceduralMeshes.Combine("KR_ChestLid",
            (Box(new Vector3(22.6f, 1.4f, 12.6f), 0.5f), TRS(new Vector3(0f, 0.7f, -6.3f), Vector3.zero)));   // pivot at the back hinge

        // ------------------------------------------------------------------ desk + desk lamp
        public static Mesh Desk()
        {
            var top = Box(new Vector3(26f, 1.2f, 13f), 0.3f);
            var leg = Box(new Vector3(1.2f, 14f, 1.2f), 0.2f);
            var drawer = Box(new Vector3(9f, 5.5f, 12f), 0.3f);
            return ProceduralMeshes.Combine("KR_Desk",
                (top, TRS(new Vector3(0f, 14.6f, 0f), Vector3.zero)),
                (leg, TRS(new Vector3(-12f, 7f, -5.4f), Vector3.zero)), (leg, TRS(new Vector3(-12f, 7f, 5.4f), Vector3.zero)),
                (leg, TRS(new Vector3(12f, 7f, -5.4f), Vector3.zero)), (leg, TRS(new Vector3(12f, 7f, 5.4f), Vector3.zero)),
                (drawer, TRS(new Vector3(7.5f, 11f, 0f), Vector3.zero)));
        }

        /// <summary>Unit cylinder stretched between two points (the lathe is centred on its own origin).</summary>
        static (Mesh, Matrix4x4) Rod(Mesh unitCylinder, Vector3 a, Vector3 b, float radius) =>
            (unitCylinder, Matrix4x4.TRS((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, b - a), new Vector3(radius, Vector3.Distance(a, b), radius)));

        public static Mesh DeskLamp()
        {
            var basePlate = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(2.2f, 0f), new Vector2(2.2f, 0.4f), new Vector2(1.4f, 0.8f), new Vector2(0f, 0.8f) }, 20, false, false, "b");
            var rod = ProceduralMeshes.Cylinder(1f, 1f, 10, "a");
            var hinge = Ball(1f, 8, 12);
            var shade = ProceduralMeshes.Lathe(new[] { new Vector2(2.4f, -1.8f), new Vector2(2.2f, -1.6f), new Vector2(1.0f, 0f), new Vector2(0.6f, 0.4f), new Vector2(0f, 0.45f) }, 20, false, true, "s");
            Vector3 foot = new Vector3(0.3f, 0.8f, 0f), elbow = new Vector3(1.8f, 7.8f, 0f), head = new Vector3(5.6f, 10.2f, 0f);
            const float shadeTilt = 35f;
            var shadeAxis = Quaternion.Euler(0f, 0f, shadeTilt) * Vector3.down;
            return ProceduralMeshes.Combine("KR_DeskLamp",
                (basePlate, Matrix4x4.identity),
                Rod(rod, foot, elbow, 0.25f),
                Rod(rod, elbow, head, 0.25f),
                (hinge, TRS(foot, Vector3.zero, Vector3.one * 0.42f)),
                (hinge, TRS(elbow, Vector3.zero, Vector3.one * 0.42f)),
                (hinge, TRS(head, Vector3.zero, Vector3.one * 0.42f)),
                (shade, TRS(head + shadeAxis * 0.35f, new Vector3(0f, 0f, shadeTilt))));
        }

        // ------------------------------------------------------------------ paper lantern (ribbed, pivot at the top)
        public static Mesh PaperLantern(float r)
        {
            var prof = new System.Collections.Generic.List<Vector2>();
            int n = 26;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float th = t * Mathf.PI;
                float rib = (i % 2 == 0) ? 1f : 0.965f;     // the paper bulges between the wire ribs
                prof.Add(new Vector2(Mathf.Max(0.35f, Mathf.Sin(th) * r * rib), -r - Mathf.Cos(th) * r * 0.92f));
            }
            return ProceduralMeshes.Lathe(prof.ToArray(), 36, true, true, "KR_PaperLantern");
        }

        // ------------------------------------------------------------------ toys
        public static Mesh CarBody()
        {
            var body = Box(new Vector3(4f, 1.8f, 8f), 0.5f);
            var cab = Box(new Vector3(3.4f, 1.8f, 3.6f), 0.5f);
            return ProceduralMeshes.Combine("KR_CarBody", (body, TRS(new Vector3(0f, 1.9f, 0f), Vector3.zero)), (cab, TRS(new Vector3(0f, 3.5f, -0.6f), Vector3.zero)));
        }

        public static Mesh CarWheels()
        {
            var w = ProceduralMeshes.Cylinder(1.05f, 0.8f, 14, "w");
            return ProceduralMeshes.Combine("KR_CarWheels",
                (w, TRS(new Vector3(-2.1f, 1.05f, 2.6f), new Vector3(0f, 0f, 90f))), (w, TRS(new Vector3(2.1f, 1.05f, 2.6f), new Vector3(0f, 0f, 90f))),
                (w, TRS(new Vector3(-2.1f, 1.05f, -2.6f), new Vector3(0f, 0f, 90f))), (w, TRS(new Vector3(2.1f, 1.05f, -2.6f), new Vector3(0f, 0f, 90f))));
        }

        public static Mesh Duck()
        {
            var s = Ball(1f, 12, 16);
            return ProceduralMeshes.Combine("KR_Duck",
                (s, TRS(new Vector3(0f, 1.6f, 0f), Vector3.zero, new Vector3(2.2f, 1.7f, 2.8f))),
                (s, TRS(new Vector3(0f, 3.8f, 1.4f), Vector3.zero, new Vector3(1.5f, 1.5f, 1.5f))),
                (s, TRS(new Vector3(0f, 2.6f, -2.4f), new Vector3(-30f, 0f, 0f), new Vector3(0.9f, 0.7f, 1.2f))));
        }

        public static Mesh DuckBeak() => ProceduralMeshes.Combine("KR_DuckBeak",
            (Ball(1f, 8, 12), TRS(new Vector3(0f, 3.6f, 2.9f), Vector3.zero, new Vector3(0.8f, 0.35f, 0.7f))));

        /// <summary>Stacking-ring toy: the wooden pole (rings are separate tori).</summary>
        public static Mesh RingPole() => ProceduralMeshes.Combine("KR_RingPole",
            (ProceduralMeshes.Cylinder(2.6f, 0.8f, 20, "b"), TRS(new Vector3(0f, 0.4f, 0f), Vector3.zero)),
            (ProceduralMeshes.Cylinder(0.45f, 9f, 10, "p"), TRS(new Vector3(0f, 5f, 0f), Vector3.zero)),
            (Ball(0.9f), TRS(new Vector3(0f, 9.8f, 0f), Vector3.zero)));

        /// <summary>
        /// A soft curtain (w x h), pivot at the top centre, gathered in waves, facing -Z. The front and the back are
        /// separate vertices (shared vertices would average opposite normals to zero and the cloth would glare).
        /// </summary>
        public static Mesh Curtain(float w, float h)
        {
            int nx = 28, ny = 6, per = (nx + 1) * (ny + 1);
            var verts = new Vector3[per * 2];
            var uvs = new Vector2[per * 2];
            var tris = new int[nx * ny * 12];
            for (int side = 0; side < 2; side++)
            for (int j = 0; j <= ny; j++)
            for (int i = 0; i <= nx; i++)
            {
                float u = i / (float)nx, v = j / (float)ny;
                float z = Mathf.Sin(u * Mathf.PI * 9f) * 0.8f * (1f - 0.3f * v) + (side == 0 ? -0.02f : 0.02f);
                verts[side * per + j * (nx + 1) + i] = new Vector3((u - 0.5f) * w, -v * h, z);
                uvs[side * per + j * (nx + 1) + i] = new Vector2(u * w / 10f, v * h / 10f);
            }
            int t = 0;
            for (int j = 0; j < ny; j++)
            for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d;          // front (-Z)
                a += per; b += per; c += per; d += per;
                tris[t++] = a; tris[t++] = b; tris[t++] = c; tris[t++] = b; tris[t++] = d; tris[t++] = c;          // back (+Z)
            }
            var m = new Mesh { name = "KR_Curtain", vertices = verts, uv = uvs, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>Small round bulbs strung along a sagging line from a to b (combined, emissive material).</summary>
        public static Mesh FairyBulbs(Vector3 a, Vector3 b, int count, float sag, string name)
        {
            var s = ProceduralMeshes.UVSphere(0.38f, 6, 8, "f");
            var parts = new (Mesh, Matrix4x4)[count];
            for (int i = 0; i < count; i++)
            {
                float t = (i + 0.5f) / count;
                var p = Vector3.Lerp(a, b, t) - Vector3.up * sag * 4f * t * (1f - t);
                parts[i] = (s, TRS(p, Vector3.zero));
            }
            return ProceduralMeshes.Combine(name, parts);
        }

        public static Mesh FairyWire(Vector3 a, Vector3 b, float sag, string name)
        {
            var seg = ProceduralMeshes.Cylinder(0.06f, 1f, 5, "w");
            int n = 24;
            var parts = new (Mesh, Matrix4x4)[n];
            for (int i = 0; i < n; i++)
            {
                float t0 = i / (float)n, t1 = (i + 1) / (float)n;
                var p0 = Vector3.Lerp(a, b, t0) - Vector3.up * sag * 4f * t0 * (1f - t0);
                var p1 = Vector3.Lerp(a, b, t1) - Vector3.up * sag * 4f * t1 * (1f - t1);
                var d = p1 - p0;
                parts[i] = (seg, Matrix4x4.TRS((p0 + p1) * 0.5f, Quaternion.FromToRotation(Vector3.up, d.normalized), new Vector3(1f, d.magnitude, 1f)));
            }
            return ProceduralMeshes.Combine(name, parts);
        }

        /// <summary>Open door frame (casing) around a w x h doorway, pivot at the floor centre, in the XY plane.</summary>
        public static Mesh DoorFrame(float w, float h)
        {
            var side = Box(new Vector3(1.3f, h + 1.3f, 1.4f), 0.2f);
            var top = Box(new Vector3(w + 2.6f, 1.3f, 1.4f), 0.2f);
            return ProceduralMeshes.Combine("KR_DoorFrame",
                (side, TRS(new Vector3(-w * 0.5f - 0.65f, (h + 1.3f) * 0.5f, 0f), Vector3.zero)),
                (side, TRS(new Vector3(w * 0.5f + 0.65f, (h + 1.3f) * 0.5f, 0f), Vector3.zero)),
                (top, TRS(new Vector3(0f, h + 0.65f, 0f), Vector3.zero)));
        }

        // ------------------------------------------------------------------ the squeaky hammer (pivot at the grip end of the handle, head along +Y)
        public static Mesh HammerHandle() => ProceduralMeshes.Combine("KR_HammerHandle",
            (ProceduralMeshes.Cylinder(0.42f, 9f, 12, "h"), TRS(new Vector3(0f, 4.5f, 0f), Vector3.zero)));

        public static Mesh HammerHead() => ProceduralMeshes.Combine("KR_HammerHead",
            (ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -3.4f), new Vector2(1.5f, -3.4f), new Vector2(1.95f, -3.0f), new Vector2(2.05f, -2.4f),
                new Vector2(1.85f, -1.6f), new Vector2(1.6f, 0f), new Vector2(1.85f, 1.6f), new Vector2(2.05f, 2.4f),
                new Vector2(1.95f, 3.0f), new Vector2(1.5f, 3.4f), new Vector2(0f, 3.4f)
            }, 24, false, false, "head"), TRS(new Vector3(0f, 9.8f, 0f), new Vector3(0f, 0f, 90f))));

        public static Mesh HammerCaps() => ProceduralMeshes.Combine("KR_HammerCaps",
            (ProceduralMeshes.Cylinder(1.6f, 0.6f, 20, "c"), TRS(new Vector3(3.3f, 9.8f, 0f), new Vector3(0f, 0f, 90f))),
            (ProceduralMeshes.Cylinder(1.6f, 0.6f, 20, "c"), TRS(new Vector3(-3.3f, 9.8f, 0f), new Vector3(0f, 0f, 90f))));
    }
}
