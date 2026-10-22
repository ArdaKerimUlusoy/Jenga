using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Named hand shapes. Every player's hand blends smoothly between them.</summary>
    public enum HandPoseId : byte
    {
        Rest = 0, Flat = 1, Point = 2, Pinch = 3, Hold = 4, Recoil = 5, Drum = 6, Knock = 7,
        // gestures (see HandGestures)
        Ok = 8, ThumbsUp = 9, MiddleFinger = 10, MugGrip = 11, Open = 12, Horns = 13, Cigarette = 14,
    }

    /// <summary>
    /// A procedurally built, articulated hand + arm (no imported model needed).
    /// Palm, four fingers with three joints each, an opposable thumb, forearm with sleeve and an upper arm solved by
    /// two-bone IK from the shoulder. The hand is driven by a *contact point* (fingertip, pinch point or palm) instead
    /// of the wrist, so a pointing index really touches the block and a pinch really closes on its end.
    /// Purely visual and local; HandSync / PlayerAvatars decide where each player's hand goes.
    /// </summary>
    public class HandRig : MonoBehaviour
    {
        // ---- pose data: curl per finger (index, middle, ring, pinky), thumb curl, thumb opposition, spread
        struct Pose
        {
            public float I, M, R, P, ThumbCurl, ThumbOpp, Spread;
            public Pose(float i, float m, float r, float p, float tc, float to, float sp) { I = i; M = m; R = r; P = p; ThumbCurl = tc; ThumbOpp = to; Spread = sp; }
            public static Pose Lerp(Pose a, Pose b, float t) => new Pose(
                Mathf.Lerp(a.I, b.I, t), Mathf.Lerp(a.M, b.M, t), Mathf.Lerp(a.R, b.R, t), Mathf.Lerp(a.P, b.P, t),
                Mathf.Lerp(a.ThumbCurl, b.ThumbCurl, t), Mathf.Lerp(a.ThumbOpp, b.ThumbOpp, t), Mathf.Lerp(a.Spread, b.Spread, t));
        }

        static Pose PoseFor(HandPoseId id)
        {
            switch (id)
            {
                // relaxed hand lying on the table: fingers slightly bent, close together
                case HandPoseId.Flat: return new Pose(0.2f, 0.22f, 0.27f, 0.32f, 0.22f, 0.18f, 0.16f);
                // cigarette held between index and middle finger
                case HandPoseId.Cigarette: return new Pose(0.22f, 0.26f, 0.82f, 0.86f, 0.45f, 0.55f, 0.02f);
                case HandPoseId.Point: return new Pose(0.02f, 0.78f, 0.86f, 0.9f, 0.55f, 0.55f, 0.1f);
                case HandPoseId.Pinch: return new Pose(0.40f, 0.58f, 0.66f, 0.74f, 0.42f, 1.0f, 0.05f);
                case HandPoseId.Hold: return new Pose(0.50f, 0.62f, 0.66f, 0.70f, 0.40f, 0.95f, 0.05f);
                case HandPoseId.Recoil: return new Pose(0.70f, 0.78f, 0.84f, 0.88f, 0.65f, 0.55f, 0.2f);
                case HandPoseId.Drum: return new Pose(0.18f, 0.18f, 0.2f, 0.22f, 0.15f, 0.1f, 0.25f);
                case HandPoseId.Knock: return new Pose(0.95f, 0.98f, 0.98f, 0.98f, 0.75f, 0.35f, 0f);
                // index and thumb make the ring, the other three fingers fan out
                case HandPoseId.Ok: return new Pose(0.62f, 0.06f, 0.08f, 0.12f, 0.55f, 1.0f, 0.55f);
                // fist, thumb straight out
                case HandPoseId.ThumbsUp: return new Pose(0.97f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f, 0f);
                case HandPoseId.MiddleFinger: return new Pose(0.97f, 0.0f, 0.97f, 0.97f, 0.7f, 0.6f, 0f);
                // wrapped round a beer mug
                case HandPoseId.MugGrip: return new Pose(0.62f, 0.66f, 0.68f, 0.7f, 0.35f, 0.9f, 0.02f);
                // open hand, fingers spread (wave)
                case HandPoseId.Open: return new Pose(0.0f, 0.0f, 0.02f, 0.05f, 0.05f, 0.0f, 1.0f);
                // index + pinky up ("rock on")
                case HandPoseId.Horns: return new Pose(0.0f, 0.97f, 0.97f, 0.0f, 0.75f, 0.85f, 0.4f);
                default: return new Pose(0.32f, 0.38f, 0.44f, 0.5f, 0.3f, 0.25f, 0.2f);
            }
        }

        [Tooltip("+1 = right hand, -1 = left hand.")]
        public float side = 1f;
        public Transform Wrist { get; private set; }
        /// <summary>World position of the palm centre (where a held object sits).</summary>
        public Vector3 PalmPosition => _palmCenter != null ? _palmCenter.position : transform.position;
        /// <summary>Between the middle joints of index and middle finger (where a cigarette is held).</summary>
        public Vector3 CigarettePoint => _finger[0][0] != null ? (_finger[0][1].position + _finger[1][1].position) * 0.5f : transform.position;
        public Vector3 Shoulder { get; set; }
        /// <summary>0..1 visible trembling (fear, a wobbling tower).</summary>
        public float Tremble { get; set; }
        public HandPoseId CurrentPose => _poseId;
        /// <summary>First-person: hide the upper arm and the sleeve so your own arm never covers the tower.</summary>
        public bool FirstPerson
        {
            get => _firstPerson;
            set
            {
                if (_firstPerson == value) return;
                _firstPerson = value;
                foreach (var r in _upper.GetComponentsInChildren<Renderer>(true)) r.enabled = !value;

            }
        }
        bool _firstPerson;

        const float UpperArm = 7.4f, Forearm = 6.6f;
        /// <summary>Longest comfortable reach from the shoulder to the contact point.</summary>
        public const float Reach = (UpperArm + Forearm) * 0.96f;

        Transform[][] _finger = new Transform[4][];
        Transform[] _thumb = new Transform[3];
        Transform[] _tips = new Transform[5]; // index, middle, ring, pinky, thumb
        Transform _palmCenter, _knuckle, _forearm, _sleeve, _upper;
        Pose _pose, _target;
        HandPoseId _poseId = HandPoseId.Rest;
        Vector3 _contact, _contactVel;
        Quaternion _orient = Quaternion.identity;
        bool _hasContact;
        float _drumT, _knockT;

        static Mesh _segMesh, _palmMeshR, _palmMeshL, _armMesh, _sleeveMesh, _cuffMesh, _nailMesh;
        static readonly System.Collections.Generic.Dictionary<Material, Material> _nailMats = new System.Collections.Generic.Dictionary<Material, Material>();

        static Matrix4x4 M(Vector3 p, Vector3 s) => Matrix4x4.TRS(p, Quaternion.identity, s);

        /// <summary>Palm with a soft heel, the thumb's ball and a row of knuckles (built per side).</summary>
        static Mesh PalmMesh(float side)
        {
            var box = ProceduralMeshes.BeveledBox(new Vector3(1.62f, 0.5f, 1.85f), 0.24f, false, Vector2.one, "palmBox");
            var ball = ProceduralMeshes.UVSphere(1f, 10, 14, "ball");
            return ProceduralMeshes.Combine(side > 0 ? "TLB_PalmR" : "TLB_PalmL",
                (box, M(new Vector3(0f, 0f, 1.0f), Vector3.one)),
                (ball, M(new Vector3(-0.5f * side, -0.12f, 0.55f), new Vector3(0.42f, 0.3f, 0.62f))),   // thumb ball
                (ball, M(new Vector3(0.18f * side, -0.1f, 0.3f), new Vector3(0.62f, 0.27f, 0.42f))),   // heel
                (ball, M(new Vector3(-0.62f * side, 0.1f, 1.86f), new Vector3(0.21f, 0.2f, 0.2f))),    // knuckles
                (ball, M(new Vector3(-0.2f * side, 0.12f, 1.92f), new Vector3(0.22f, 0.21f, 0.2f))),
                (ball, M(new Vector3(0.22f * side, 0.11f, 1.9f), new Vector3(0.21f, 0.2f, 0.2f))),
                (ball, M(new Vector3(0.6f * side, 0.08f, 1.76f), new Vector3(0.18f, 0.18f, 0.18f))));
        }

        static Material NailMaterial(Material skin)
        {
            if (skin == null) return null;
            if (_nailMats.TryGetValue(skin, out var m) && m != null) return m;
            m = new Material(skin) { name = skin.name + "_Nail" };
            var c = skin.HasProperty("_BaseColor") ? skin.GetColor("_BaseColor") : skin.color;
            var nail = Color.Lerp(c, new Color(0.96f, 0.86f, 0.82f), 0.55f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", nail);
            m.color = nail;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
            _nailMats[skin] = m;
            return m;
        }

        // ------------------------------------------------------------------ Construction
        public static HandRig Create(Transform parent, string name, float side, Material skin, Material cloth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<HandRig>();
            rig.side = side;
            rig.Build(skin, cloth);
            return rig;
        }

        static Mesh AlongZ(Mesh m)
        {
            var v = m.vertices; var n = m.normals;
            var r = Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < v.Length; i++) { v[i] = r * v[i]; n[i] = r * n[i]; }
            m.vertices = v; m.normals = n;
            m.RecalculateBounds();
            return m;
        }

        static void EnsureMeshes()
        {
            if (_segMesh != null) return;
            // unit-length rounded finger segment along +Z (radius 1, scaled per bone)
            _segMesh = AlongZ(ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -0.35f), new Vector2(0.6f, -0.25f), new Vector2(0.95f, 0.0f), new Vector2(1f, 0.5f),
                new Vector2(0.95f, 1.0f), new Vector2(0.6f, 1.22f), new Vector2(0f, 1.3f)
            }, 12, false, false, "TLB_FingerSeg"));
            _palmMeshR = PalmMesh(1f);
            _palmMeshL = PalmMesh(-1f);
            // forearm: slim wrist, fuller towards the elbow
            _armMesh = AlongZ(ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -0.12f), new Vector2(0.36f, -0.08f), new Vector2(0.44f, 0.04f), new Vector2(0.5f, 0.2f), new Vector2(0.62f, 0.55f),
                new Vector2(0.68f, 0.85f), new Vector2(0.62f, 1.0f), new Vector2(0f, 1.05f)
            }, 16, false, false, "TLB_Forearm"));
            _cuffMesh = AlongZ(ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0.95f, 0f), new Vector2(1.12f, 0.05f), new Vector2(1.18f, 0.5f), new Vector2(1.12f, 0.95f), new Vector2(0.95f, 1f)
            }, 16, false, false, "TLB_Cuff"));
            _nailMesh = ProceduralMeshes.UVSphere(1f, 6, 10, "TLB_Nail");
            _sleeveMesh = AlongZ(ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, 0f), new Vector2(1.0f, 0.02f), new Vector2(1.08f, 0.15f), new Vector2(1.12f, 0.85f), new Vector2(1.18f, 1f), new Vector2(0f, 1.02f)
            }, 14, false, false, "TLB_Sleeve"));
        }

        static Transform Node(Transform parent, string name, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static void Skin(Transform parent, Mesh mesh, Material mat, Vector3 scale, Vector3 pos = default)
        {
            var g = new GameObject("Mesh");
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        void Build(Material skin, Material cloth)
        {
            EnsureMeshes();
            Wrist = Node(transform, "Wrist", Vector3.zero);
            // palm: wrist at origin, fingers point +Z, back of the hand +Y, thumb on -X for the right hand
            Skin(Wrist, side > 0 ? _palmMeshR : _palmMeshL, skin, Vector3.one);
            var nailMat = NailMaterial(skin);
            _palmCenter = Node(Wrist, "PalmContact", new Vector3(0f, -0.35f, 1.1f));
            _knuckle = Node(Wrist, "KnuckleContact", new Vector3(0f, 0.1f, 2.15f));

            float[] xs = { -0.62f, -0.2f, 0.22f, 0.6f };
            float[][] lens = { new[] { 0.95f, 0.58f, 0.46f }, new[] { 1.05f, 0.65f, 0.5f }, new[] { 1.0f, 0.6f, 0.47f }, new[] { 0.76f, 0.47f, 0.4f } };
            float[] radius = { 0.19f, 0.2f, 0.19f, 0.16f };
            for (int f = 0; f < 4; f++)
            {
                _finger[f] = new Transform[3];
                var parent = Wrist;
                var pos = new Vector3(xs[f] * side, 0.02f, 1.95f - (f == 3 ? 0.12f : 0f));
                for (int j = 0; j < 3; j++)
                {
                    var joint = Node(parent, $"F{f}_{j}", pos);
                    float r = radius[f] * (1f - 0.12f * j);
                    Skin(joint, _segMesh, skin, new Vector3(r, r * 0.92f, lens[f][j]));
                    if (j == 2 && nailMat != null) Skin(joint, _nailMesh, nailMat, new Vector3(r * 0.78f, r * 0.22f, lens[f][j] * 0.42f), new Vector3(0f, r * 0.78f, lens[f][j] * 0.72f));
                    _finger[f][j] = joint;
                    parent = joint;
                    pos = new Vector3(0f, 0f, lens[f][j]);
                }
                _tips[f] = Node(parent, $"F{f}_Tip", new Vector3(0f, -0.05f, lens[f][2] + 0.08f));
            }

            float[] tl = { 0.72f, 0.56f, 0.46f };
            var tp = Wrist;
            var tpos = new Vector3(-0.78f * side, -0.15f, 0.55f);
            for (int j = 0; j < 3; j++)
            {
                var joint = Node(tp, "T_" + j, tpos);
                float r = 0.23f * (1f - 0.1f * j);
                Skin(joint, _segMesh, skin, new Vector3(r, r * 0.9f, tl[j]));
                if (j == 2 && nailMat != null) Skin(joint, _nailMesh, nailMat, new Vector3(r * 0.8f, r * 0.22f, tl[j] * 0.45f), new Vector3(0f, r * 0.75f, tl[j] * 0.7f));
                _thumb[j] = joint;
                tp = joint;
                tpos = new Vector3(0f, 0f, tl[j]);
            }
            _tips[4] = Node(tp, "T_Tip", new Vector3(0f, 0f, tl[2] + 0.1f));

            // arm: forearm hangs back from the wrist, sleeve over its rear half, upper arm in cloth
            _forearm = Node(transform, "Forearm", Vector3.zero);
            Skin(_forearm, _armMesh, skin, new Vector3(1f, 0.85f, Forearm));
            // a rolled-up shirt sleeve over the back half of the forearm, with a thick turned-up cuff
            _sleeve = Node(_forearm, "Sleeve", new Vector3(0f, 0f, Forearm * 0.52f));
            Skin(_sleeve, _sleeveMesh, cloth, new Vector3(0.82f, 0.74f, Forearm * 0.53f));
            Skin(_sleeve, _cuffMesh, cloth, new Vector3(0.86f, 0.78f, 0.9f), new Vector3(0f, 0f, -0.15f));
            _upper = Node(transform, "UpperArm", Vector3.zero);
            Skin(_upper, _sleeveMesh, cloth, new Vector3(1.15f, 1.05f, UpperArm));

            _pose = _target = PoseFor(HandPoseId.Rest);
            ApplyPose(_pose, 0f);
        }

        // ------------------------------------------------------------------ Driving
        public void SetPose(HandPoseId id)
        {
            if (id == HandPoseId.Knock && _poseId != HandPoseId.Knock) _knockT = 0f;
            _poseId = id;
            _target = PoseFor(id);
        }

        /// <summary>Start a knock on the table (fist taps twice).</summary>
        public void Knock()
        {
            _knockT = 0.0001f;
        }

        /// <summary>
        /// Where the hand's contact point should be and how the hand is oriented (forward = fingers, up = back of hand).
        /// `follow` = responsiveness (higher = faster). The hand moves on a critically damped spring, so it eases in
        /// and out like a real hand instead of sliding at a constant rate. With `glue` the hand locks onto the target
        /// once it has arrived (holding a block: the grip must not lag behind the replicated block).
        /// </summary>
        public void SetContact(Vector3 worldContact, Quaternion orientation, float follow, bool glue = false)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (!_hasContact) { _contact = worldContact; _orient = orientation; _hasContact = true; _contactVel = Vector3.zero; }
            if (glue && _glued)
            {
                _contact = worldContact;
                _contactVel = Vector3.zero;
            }
            else
            {
                float smooth = Mathf.Clamp(1.8f / Mathf.Max(1f, follow), 0.035f, 0.5f);
                _contact = Vector3.SmoothDamp(_contact, worldContact, ref _contactVel, smooth, 80f, dt);
                if (glue && (_contact - worldContact).sqrMagnitude < 0.04f) _glued = true;
            }
            if (!glue) _glued = false;
            _orient = Quaternion.Slerp(_orient, orientation, 1f - Mathf.Exp(-follow * (glue ? 1.2f : 0.7f) * dt));
        }

        bool _glued;

        Transform ContactFor(HandPoseId id)
        {
            switch (id)
            {
                case HandPoseId.Point: return _tips[0];
                case HandPoseId.Pinch:
                case HandPoseId.Hold: return null; // midpoint of thumb and index tips
                case HandPoseId.Knock: return _knuckle;
                default: return _palmCenter;
            }
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            _pose = Pose.Lerp(_pose, _target, 1f - Mathf.Exp(-12f * dt));
            if (_poseId == HandPoseId.Drum) _drumT += dt;
            ApplyPose(_pose, _poseId == HandPoseId.Drum ? _drumT : -1f);

            // orientation + tremble
            var rot = _orient;
            float tr = Tremble;
            if (tr > 0.001f)
            {
                float t = Time.time;
                rot *= Quaternion.Euler((Mathf.PerlinNoise(t * 9f, 1.3f) - 0.5f) * 7f * tr, (Mathf.PerlinNoise(t * 8f, 4.1f) - 0.5f) * 5f * tr, (Mathf.PerlinNoise(t * 10f, 7.7f) - 0.5f) * 6f * tr);
            }
            Wrist.rotation = rot;

            // place the wrist so the contact point lands on the target
            Wrist.position = Vector3.zero;
            Vector3 contactLocal;
            var c = ContactFor(_poseId);
            if (c != null) contactLocal = c.position;
            else contactLocal = (_tips[0].position + _tips[4].position) * 0.5f;
            var pos = _contact - contactLocal;
            if (tr > 0.001f)
            {
                float t = Time.time;
                pos += new Vector3(Mathf.PerlinNoise(t * 11f, 2.2f) - 0.5f, Mathf.PerlinNoise(t * 12f, 5.5f) - 0.5f, Mathf.PerlinNoise(t * 10.5f, 8.8f) - 0.5f) * 0.12f * tr;
            }
            if (_knockT > 0f)
            {
                _knockT += dt;
                float k = _knockT;
                float lift = k < 0.12f ? k / 0.12f : k < 0.2f ? 1f - (k - 0.12f) / 0.08f : k < 0.32f ? (k - 0.2f) / 0.12f : k < 0.4f ? 1f - (k - 0.32f) / 0.08f : 0f;
                pos += Vector3.up * lift * 0.9f;
                if (k > 0.5f) _knockT = 0f;
            }
            Wrist.position = pos;

            SolveArm();
        }

        void ApplyPose(Pose p, float drumTime)
        {
            float[] curls = { p.I, p.M, p.R, p.P };
            float[] max = { 72f, 92f, 64f };
            for (int f = 0; f < 4; f++)
            {
                float c = curls[f];
                if (drumTime >= 0f)
                {
                    // fingers tap the table one after another (pinky -> index), with a pause
                    float cycle = Mathf.Repeat(drumTime * 1.6f, 2.2f);
                    float local = cycle - (3 - f) * 0.14f;
                    if (local > 0f && local < 0.22f) c -= Mathf.Sin(local / 0.22f * Mathf.PI) * 0.35f;
                }
                float spread = (f - 1.5f) * p.Spread * 5f * side;
                for (int j = 0; j < 3; j++)
                    _finger[f][j].localRotation = Quaternion.Euler(c * max[j], j == 0 ? spread : 0f, 0f);
            }
            // thumb: base swings under the palm (opposition), joints curl inwards
            _thumb[0].localRotation = Quaternion.Euler(18f + 26f * p.ThumbOpp, (-48f + 40f * p.ThumbOpp) * side, (-30f - 55f * p.ThumbOpp) * side);
            _thumb[1].localRotation = Quaternion.Euler(p.ThumbCurl * 40f, 6f * side, 0f);
            _thumb[2].localRotation = Quaternion.Euler(p.ThumbCurl * 55f, 0f, 0f);
        }

        void SolveArm()
        {
            var wrist = Wrist.position;
            var back = Wrist.rotation * Vector3.back;
            // the elbow sits behind the wrist (forearm direction), constrained by the shoulder distance (2-bone IK)
            var s = Shoulder;
            var toW = wrist - s;
            float d = Mathf.Clamp(toW.magnitude, 0.5f, UpperArm + Forearm - 0.01f);
            var dir = toW.normalized;
            float a = (UpperArm * UpperArm - Forearm * Forearm + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, UpperArm * UpperArm - a * a));
            var pole = Vector3.ProjectOnPlane(Vector3.down + Vector3.Cross(Vector3.up, dir) * 0.6f * side + back * 0.4f, dir).normalized;
            if (pole.sqrMagnitude < 1e-4f) pole = Vector3.down;
            var elbow = s + dir * a + pole * h;

            var fDir = (wrist - elbow);
            _forearm.position = wrist;
            _forearm.rotation = Quaternion.LookRotation(-fDir.normalized, Vector3.up);
            var uDir = (elbow - s);
            _upper.position = s;
            _upper.rotation = Quaternion.LookRotation(uDir.normalized, Vector3.up);
            _upper.localScale = new Vector3(1f, 1f, uDir.magnitude / UpperArm);
            _forearm.localScale = new Vector3(1f, 1f, fDir.magnitude / Forearm);
        }

        public void SetVisible(bool v)
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        }
    }
}
