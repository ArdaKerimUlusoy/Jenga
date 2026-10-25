using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Seated figures and their hands. Every other player sits in their chair as a kid (rigged CC0 mannequin dressed
    /// in a t-shirt and shorts, with a cap or a mop of hair, a big round head and short legs that dangle from the
    /// chair, animated with seated idle clips) and reaches over the table with two articulated hands (HandRig).
    /// If the character model is missing, a simple hooded body is used instead.
    /// The local player sees only their own two hands (first person, without the upper arm so it never hides the tower).
    ///
    /// Your right hand IS your cursor: it rests flat on the table and slides with the mouse inside your own reach,
    /// extends a finger to touch the block you aim at, flinches back from locked blocks and pinches the end of the
    /// block you pull. While carrying, the block is held by its near end from below-behind, so the top of the tower
    /// and the free slots stay visible. With rising tension the hands tremble and the fingers drum. F knocks.
    /// All of this is shared through HandSync (bots publish their hands from the host). Purely visual.
    ///
    /// Gestures (left hand, number keys): 1 OK sign, 2 a lick of the lollipop from the cup in front of you,
    /// 3 "nyah nyah" (thumb on the nose, fingers wiggling), 4 a sip of juice, 5 wave, 6 "rock on".
    /// Everyone at the table sees them; bots make them too. (The network names of the gestures are unchanged.)
    /// When a player knocks the tower over, KidsRoomLife sends another kid with a squeaky hammer: SetKnockedOut makes
    /// that player's kid slump onto the table.
    /// </summary>
    public class PlayerAvatars : MonoBehaviour
    {
        class Avatar
        {
            public GameObject Root;
            public Transform Head, Torso, Label;
            public TextMesh LabelText;
            public HandRig Right, Left;
            public int Seat = -1;
            public bool Local;
            public float KnockUntil;
            // rigged character
            public Transform Model, BHead, BUpperL, BUpperR, BLowerL, BLowerR, BSpine;
            public float Lean, LeanTarget;
            public Animator Anim;
            public float ModelScale = 20f;
            public Vector3 PelvisOffset;     // pelvis position relative to the model root (scaled, model space)
            public Quaternion FacingFix = Quaternion.identity;
            public Quaternion HeadExtra = Quaternion.identity;
            // head look: the head bone's rotation relative to a level, forward-facing frame (from the bind pose)
            public Quaternion HeadOffset = Quaternion.identity;
            public bool HasHeadOffset;
            public Vector3 LookDir;
            // the pose the bones had before we added lean / head turn on top (see RestoreBone)
            public Quaternion SpineBase, SpineSet, HeadBase, HeadSet;
            public bool SpineTouched, HeadTouched;
            // gestures
            public Gesture G;
            public float GStart;
            public Transform Mug, Ashtray, Cig;
            public Renderer CigEmber;
            public ParticleSystem Smoke;
            public int GFlags;          // one-shot events inside the current gesture (sounds, smoke puff)
            public float WispTimer;
            // kid proportions + knocked out by the squeaky hammer
            public Transform BThighL, BThighR;
            public float KO;            // 0..1, set by KidsRoomLife
        }

        static Mesh _cigMesh, _emberMesh, _ashtrayMesh;
        static Material _cigMat, _emberMat, _ashtrayMat, _smokeMat;
        MaterialPropertyBlock _mpb;

        static Mesh _mugGlass, _mugBeer, _mugFoam, _mugHandle;
        static Material _mugGlassMat, _mugBeerMat, _mugFoamMat;

        /// <summary>Short line shown over the player's head while they gesture (so nobody misses it).</summary>
        public static string GesturePhrase(Gesture g)
        {
            switch (g)
            {
                case Gesture.Ok: return L.T("OK!", "Tamam!");
                case Gesture.Smoke: return L.T("Mmm, lollipop!", "Mmm, lolipop!");
                case Gesture.MiddleFinger: return L.T("Nyah nyah!", "Nanik!");
                case Gesture.Beer: return L.T("Juice!", "Meyve suyu!");
                case Gesture.Wave: return L.T("Hi!", "Selam!");
                case Gesture.Horns: return L.T("Rock on!", "Rock!");
                default: return "";
            }
        }

        /// <summary>
        /// Keeps a free hand where a seated person can actually put it: never past the far side of the tower and
        /// never further from the shoulder than the arm reaches.
        /// </summary>
        static Vector3 LimitHand(int seat, Vector3 shoulder, Vector3 p)
        {
            var outward = SeatLayout.Outward(seat);
            const float farLimit = -1.1f;
            float d = Vector3.Dot(new Vector3(p.x, 0f, p.z), outward);
            if (d < farLimit) p += outward * (farLimit - d);
            var toP = p - shoulder;
            if (toP.magnitude > HandRig.Reach) p = shoulder + toP.normalized * HandRig.Reach;
            return p;
        }

        public static float GestureLength(Gesture g) => g == Gesture.Beer ? 3.6f : g == Gesture.Smoke ? 4.6f : g == Gesture.Wave ? 2.3f : 2.0f;

        static readonly string[] SitStates = { "Sitting_Idle_Loop", "Sitting_Idle02_Loop", "Sitting_Idle03_Loop", "Sitting_Talking_Loop", "Sitting_Nodding_Loop" };
        // kids' clothes: the t-shirt is in the player's colour; shorts, shoes, cap and hair vary per player
        static readonly Color[] Shirts = { new Color(0.95f, 0.85f, 0.35f), new Color(0.35f, 0.75f, 0.85f), new Color(0.95f, 0.55f, 0.6f), new Color(0.55f, 0.8f, 0.45f), new Color(0.98f, 0.98f, 0.95f) };
        static readonly Color[] Hats = { new Color(0.85f, 0.2f, 0.18f), new Color(0.2f, 0.45f, 0.85f), new Color(0.98f, 0.8f, 0.2f), new Color(0.25f, 0.65f, 0.35f), new Color(0.55f, 0.35f, 0.8f) };
        static readonly Color[] Shorts = { new Color(0.24f, 0.3f, 0.48f), new Color(0.75f, 0.55f, 0.35f), new Color(0.3f, 0.45f, 0.3f), new Color(0.55f, 0.2f, 0.25f), new Color(0.35f, 0.35f, 0.38f) };
        static readonly Color[] ShoeCols = { new Color(0.9f, 0.2f, 0.18f), new Color(0.95f, 0.95f, 0.95f), new Color(0.2f, 0.4f, 0.85f), new Color(0.15f, 0.15f, 0.17f), new Color(0.98f, 0.75f, 0.2f) };
        static readonly Color[] HairCols = { new Color(0.22f, 0.13f, 0.07f), new Color(0.08f, 0.06f, 0.05f), new Color(0.75f, 0.55f, 0.25f), new Color(0.55f, 0.25f, 0.1f), new Color(0.35f, 0.22f, 0.12f) };
        static readonly Color[] Skins = { new Color(0.95f, 0.78f, 0.66f), new Color(0.84f, 0.64f, 0.5f), new Color(0.62f, 0.44f, 0.33f), new Color(0.98f, 0.84f, 0.74f) };

        /// <summary>Head-bone height of the seated kid above the floor, in game units (95 cm).</summary>
        const float SeatedHeadHeight = 19.5f;
        /// <summary>Kid proportions on the adult mannequin: shorter body and legs, a big round head.</summary>
        const float KidSpine = 0.88f, KidHead = 1.5f, KidThigh = 0.8f;

        public static PlayerAvatars Instance { get; private set; }

        readonly Dictionary<ulong, Avatar> _avatars = new Dictionary<ulong, Avatar>();
        readonly List<ulong> _remove = new List<ulong>();
        Mesh _torso, _head;
        Material _skin, _cloth, _hood;

        void Awake()
        {
            Instance = this;
            var a = SecretJengaAssets.Instance;
            _skin = a.skinMaterial != null ? a.skinMaterial : RuntimeArt.MakeLit(new Color(0.66f, 0.56f, 0.5f), 0.35f);
            _cloth = a.clothMaterial != null ? a.clothMaterial : RuntimeArt.MakeLit(new Color(0.16f, 0.15f, 0.15f), 0.1f);
            _hood = a.hoodMaterial != null ? a.hoodMaterial : RuntimeArt.MakeLit(new Color(0.11f, 0.105f, 0.11f), 0.05f);
            _torso = ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0.0f, 0f), new Vector2(2.3f, 0.05f), new Vector2(2.5f, 2.5f), new Vector2(2.4f, 6.5f),
                new Vector2(2.3f, 8.8f), new Vector2(1.5f, 9.8f), new Vector2(0.75f, 10.2f), new Vector2(0f, 10.3f)
            }, 20, false, false, "TLB_Torso");
            _head = ProceduralMeshes.Lathe(new[]
            {
                new Vector2(0f, -1.7f), new Vector2(0.9f, -1.6f), new Vector2(1.45f, -0.9f), new Vector2(1.6f, 0.1f),
                new Vector2(1.45f, 1.1f), new Vector2(1.0f, 1.75f), new Vector2(0.35f, 2.15f), new Vector2(0f, 2.2f)
            }, 20, false, false, "TLB_Hood");
            HandSync.Knocked += OnKnocked;
            HandSync.GestureMade += OnGesture;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            HandSync.Knocked -= OnKnocked;
            HandSync.GestureMade -= OnGesture;
        }

        void OnGesture(ulong clientId, Gesture g)
        {
            if (!_avatars.TryGetValue(clientId, out var av)) return;
            av.G = g;
            av.GStart = Time.time;
            av.GFlags = 0;
        }

        /// <summary>Number keys 1-6 make a gesture (not while typing or in a menu).</summary>
        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null || SecretJenga.UI.UIManager.BlocksGameInput) return;
            var es = EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null && es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null) return;
            var g = Gesture.None;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) g = Gesture.Ok;
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) g = Gesture.Smoke;
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) g = Gesture.MiddleFinger;
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) g = Gesture.Beer;
            else if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) g = Gesture.Wave;
            else if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) g = Gesture.Horns;
            if (g != Gesture.None) HandSync.GestureLocal(g);
        }

        void OnKnocked(ulong clientId, Vector3 at)
        {
            if (_avatars.TryGetValue(clientId, out var av))
            {
                av.KnockUntil = Time.time + 0.5f;
                av.Right.SetPose(HandPoseId.Knock);
                av.Right.Knock();
            }
            var hits = SecretJengaAssets.Instance.blockHits;
            if (hits != null && hits.Length > 0)
            {
                AudioManager.Instance?.PlayAt(hits[0], at, 0.9f, 0.55f);
                AudioManager.Instance?.PlayAt(hits[hits.Length - 1], at, 0.8f, 0.5f);
            }
        }

        void LateUpdate()
        {
            var game = NetworkGameManager.Instance;
            if (game == null || !game.IsSpawned) { ClearAll(); return; }

            _remove.Clear();
            foreach (var id in _avatars.Keys)
                if (!game.TryGetPlayer(id, out var pd) || !pd.IsConnected) _remove.Add(id);
            foreach (var id in _remove) { Destroy(_avatars[id].Root); _avatars.Remove(id); }

            ulong local = game.NetworkManager.LocalClientId;
            float tension = game.Atmosphere != null ? game.Atmosphere.Tension.Value : 0f;
            foreach (var p in game.Players)
            {
                if (!p.IsConnected) continue;
                if (!_avatars.TryGetValue(p.ClientId, out var av)) { av = Build(p.Name.ToString(), p.ClientId == local, p.ColorIndex); _avatars[p.ClientId] = av; }
                if (av.Seat != p.SeatIndex) { av.Seat = p.SeatIndex; PlaceBody(av); }
                if (av.LabelText != null)
                {
                    av.LabelText.text = (p.IsBot ? p.Name + " (bot)" : p.Name.ToString()) + (av.G != Gesture.None ? "\n" + GesturePhrase(av.G) : "");
                    av.LabelText.color = Color.Lerp(Palette.Paper, p.Color, 0.45f);
                }
                Animate(game, p.ClientId, av, tension);
            }
        }

        void ClearAll()
        {
            foreach (var av in _avatars.Values) if (av.Root != null) Destroy(av.Root);
            _avatars.Clear();
        }

        // ------------------------------------------------------------------ Build
        Transform Part(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        Avatar Build(string playerName, bool local, int colorIndex)
        {
            var av = new Avatar { Local = local };
            av.Root = new GameObject(local ? "Avatar_Local" : "Avatar_" + playerName);
            av.Root.transform.SetParent(transform, false);
            var r = av.Root.transform;
            Material handSkin = _skin, handCloth = _cloth;
            if (local)
            {
                // your own hands match the other kids: a natural skin tone and a t-shirt sleeve in your colour
                int lh = StableHash(playerName);
                var sc = Skins[(lh / 13) % Skins.Length];
                handSkin = RuntimeArt.MakeLit(new Color(sc.r * 0.95f, sc.g * 0.9f, sc.b * 0.92f), 0.32f);
                handCloth = RuntimeArt.MakeLit(Color.Lerp(Palette.Player(colorIndex), Color.white, 0.15f), 0.12f);
            }
            else
            {
                if (!TryBuildCowboy(av, playerName, colorIndex, out handSkin, out handCloth))
                {
                    handSkin = _skin;
                    handCloth = _cloth;
                    av.Torso = Part(r, "Torso", _torso, _cloth);
                    av.Head = Part(r, "Head", _head, _hood);
                }
                var label = new GameObject("NameLabel");
                label.transform.SetParent(r, false);
                var tm = label.AddComponent<TextMesh>();
                var font = SecretJengaAssets.Instance.fontBody != null ? SecretJengaAssets.Instance.fontBody : RuntimeArt.Font;
                tm.font = font;
                tm.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                tm.fontSize = 64;
                tm.characterSize = 0.13f;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.alignment = TextAlignment.Center;
                av.Label = label.transform;
                av.LabelText = tm;
            }
            av.Right = HandRig.Create(r, "RightHand", 1f, handSkin, handCloth);
            av.Left = HandRig.Create(r, "LeftHand", -1f, handSkin, handCloth);
            av.Mug = BuildMug(r);
            BuildSmokingKit(av, r);
            if (local)
            {
                av.Right.FirstPerson = true;
                av.Left.FirstPerson = true;
            }
            return av;
        }

        /// <summary>A little cup with a lollipop standing in it (used by the lollipop gesture - network name "Smoke").</summary>
        void BuildSmokingKit(Avatar av, Transform parent)
        {
            if (_cigMesh == null)
            {
                // the stick runs along Y; the candy sits at the -Y end (the end that goes to the lips)
                _cigMesh = ProceduralMeshes.Cylinder(0.08f, 2.8f, 8, "TLB_LollipopStick");
                _emberMesh = ProceduralMeshes.Combine("TLB_LollipopCandy",
                    (ProceduralMeshes.Cylinder(0.75f, 0.32f, 20, "c"), Matrix4x4.TRS(new Vector3(0f, -1.85f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
                _ashtrayMesh = ProceduralMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.75f, 0f), new Vector2(0.85f, 0.1f), new Vector2(0.95f, 1.6f), new Vector2(0.85f, 1.65f),
                    new Vector2(0.75f, 0.25f), new Vector2(0f, 0.25f)
                }, 20, false, false, "TLB_CandyCup");
                _cigMat = RuntimeArt.MakeLit(new Color(0.97f, 0.96f, 0.93f), 0.3f);
                _emberMat = RuntimeArt.MakeLit(new Color(0.95f, 0.3f, 0.45f), 0.85f);
                _ashtrayMat = RuntimeArt.MakeLit(new Color(0.35f, 0.7f, 0.95f), 0.7f);
            }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();

            var cup = new GameObject("CandyCup").transform;
            cup.SetParent(parent, false);
            cup.gameObject.AddComponent<MeshFilter>().sharedMesh = _ashtrayMesh;
            cup.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _ashtrayMat;
            av.Ashtray = cup;

            var pop = new GameObject("Lollipop").transform;
            pop.SetParent(parent, false);
            var stick = new GameObject("Stick");
            stick.transform.SetParent(pop, false);
            stick.AddComponent<MeshFilter>().sharedMesh = _cigMesh;
            stick.AddComponent<MeshRenderer>().sharedMaterial = _cigMat;
            var candy = new GameObject("Candy");
            candy.transform.SetParent(pop, false);
            candy.AddComponent<MeshFilter>().sharedMesh = _emberMesh;
            candy.AddComponent<MeshRenderer>().sharedMaterial = _emberMat;
            av.Cig = pop;
            av.CigEmber = null;   // nothing glows and nothing smokes in a kids' room
            av.Smoke = null;
        }

        static void Puff(ParticleSystem ps, Vector3 at, Vector3 dir, int count, float speed, float size)
        {
            if (ps == null) return;
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                ep.position = at + Random.insideUnitSphere * 0.08f;
                ep.velocity = (dir + Random.insideUnitSphere * 0.35f) * speed * Random.Range(0.6f, 1.2f) + Vector3.up * 0.15f;
                ep.startSize = size * Random.Range(0.7f, 1.3f);
                ps.Emit(ep, 1);
            }
        }

        /// <summary>A glass beer mug (pivot at the bottom, about 15 cm tall) that sits on the table in front of each player.</summary>
        static Transform BuildMug(Transform parent)
        {
            if (_mugGlass == null)
            {
                _mugGlass = ProceduralMeshes.Lathe(new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.78f, 0f), new Vector2(0.82f, 0.08f), new Vector2(0.86f, 2.9f), new Vector2(0.92f, 3.0f),
                    new Vector2(0.84f, 3.0f), new Vector2(0.76f, 0.35f), new Vector2(0f, 0.35f)
                }, 20, false, false, "TLB_MugGlass");
                _mugBeer = ProceduralMeshes.Lathe(new[] { new Vector2(0f, 0.36f), new Vector2(0.75f, 0.36f), new Vector2(0.8f, 2.35f), new Vector2(0f, 2.35f) }, 18, false, true, "TLB_MugBeer");
                _mugFoam = ProceduralMeshes.Lathe(new[] { new Vector2(0.81f, 2.3f), new Vector2(0.84f, 2.75f), new Vector2(0.55f, 2.95f), new Vector2(0f, 3.0f) }, 18, false, false, "TLB_MugFoam");
                _mugHandle = SaloonMeshes.Ring(0.62f, 0.14f, 16, "TLB_MugHandle");
                _mugGlassMat = RuntimeArt.MakeLit(new Color(0.85f, 0.9f, 0.95f), 0.95f);
                _mugBeerMat = RuntimeArt.MakeLit(new Color(1f, 0.6f, 0.12f), 0.8f);      // orange juice
                _mugFoamMat = RuntimeArt.MakeLit(new Color(1f, 0.72f, 0.3f), 0.5f);
            }
            var mug = new GameObject("JuiceGlass").transform;
            mug.SetParent(parent, false);
            void Piece(string n, Mesh m, Material mat, Vector3 pos, Vector3 euler)
            {
                var go = new GameObject(n);
                go.transform.SetParent(mug, false);
                go.transform.localPosition = pos;
                go.transform.localEulerAngles = euler;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            Piece("Beer", _mugBeer, _mugBeerMat, Vector3.zero, Vector3.zero);
            Piece("Foam", _mugFoam, _mugFoamMat, Vector3.zero, Vector3.zero);
            Piece("Glass", _mugGlass, _mugGlassMat, Vector3.zero, Vector3.zero);
            Piece("Straw", ProceduralMeshes.Cylinder(0.07f, 3.4f, 6, "TLB_Straw"), _mugBeerMat, new Vector3(0.35f, 3.2f, 0f), new Vector3(0f, 0f, -12f));
            return mug;
        }

        /// <summary>Same number on every machine (string.GetHashCode is not guaranteed to be).</summary>
        static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h & 0x7fffffff;
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        bool TryBuildCowboy(Avatar av, string playerName, int colorIndex, out Material skin, out Material cloth)
        {
            skin = cloth = null;
            var a = SecretJengaAssets.Instance;
            if (a.cowboyModel == null) return false;

            var go = Instantiate(a.cowboyModel, av.Root.transform, false);
            go.name = "Cowboy";
            var model = go.transform;
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.identity;
            model.localScale = Vector3.one;

            av.BHead = FindDeep(model, "Head");
            var pelvis = FindDeep(model, "pelvis");
            av.BUpperL = FindDeep(model, "upperarm_l");
            av.BSpine = FindDeep(model, "spine_01");
            av.BUpperR = FindDeep(model, "upperarm_r");
            av.BLowerL = FindDeep(model, "lowerarm_l");
            av.BLowerR = FindDeep(model, "lowerarm_r");
            var footL = FindDeep(model, "foot_l");
            var footR = FindDeep(model, "foot_r");
            if (av.BHead == null || pelvis == null || av.BUpperL == null || av.BUpperR == null) { Destroy(go); return false; }
            av.BThighL = FindDeep(model, "thigh_l");
            av.BThighR = FindDeep(model, "thigh_r");
            // bind pose (before any animation): the head is level and faces the model's forward - remember how the
            // head bone sits in that frame, so later we can point the head exactly where the player looks
            var bindHead = Quaternion.Inverse(av.Root.transform.rotation) * av.BHead.rotation;

            av.Anim = go.GetComponentInChildren<Animator>();
            if (av.Anim == null) av.Anim = go.AddComponent<Animator>();
            if (a.cowboyAnimator != null) av.Anim.runtimeAnimatorController = a.cowboyAnimator;
            av.Anim.applyRootMotion = false;
            av.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            int h = StableHash(playerName);
            if (av.Anim.runtimeAnimatorController != null)
            {
                av.Anim.Play(SitStates[h % SitStates.Length], 0, (h % 97) / 97f);
                av.Anim.speed = 0.9f + (h % 7) * 0.03f;
                av.Anim.Update(0f);
            }
            ApplyKidProportions(av);

            // measure the seated pose once (works whatever unit scale / axis the importer chose)
            var root = av.Root.transform;
            Vector3 headL = root.InverseTransformPoint(av.BHead.position);
            Vector3 pelvisL = root.InverseTransformPoint(pelvis.position);
            float headHeight = headL.y;
            av.ModelScale = headHeight > 0.01f ? SeatedHeadHeight / headHeight : 20f;
            Vector3 fwd = Vector3.forward;
            if (footL != null && footR != null)
            {
                var feet = (root.InverseTransformPoint(footL.position) + root.InverseTransformPoint(footR.position)) * 0.5f;
                fwd = feet - pelvisL;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            }
            av.FacingFix = Quaternion.FromToRotation(fwd, Vector3.forward);
            av.HeadOffset = Quaternion.Inverse(Quaternion.LookRotation(fwd, Vector3.up)) * bindHead;
            av.HasHeadOffset = true;
            av.PelvisOffset = av.FacingFix * (pelvisL * av.ModelScale);
            av.PelvisOffset.y = 0f;
            av.Model = model;

            // clothes: the t-shirt in the player's colour, the rest varied per player; a cap or hair
            var shirtC = Color.Lerp(Palette.Player(colorIndex), Color.white, 0.15f);
            var capC = Hats[(h / 7) % Hats.Length];
            var skinC = Skins[(h / 13) % Skins.Length];
            bool wearsCap = (h / 3) % 2 == 0;
            skin = RuntimeArt.MakeLit(skinC, 0.3f);
            cloth = RuntimeArt.MakeLit(shirtC, 0.15f);
            var shorts = RuntimeArt.MakeLit(Shorts[(h / 5) % Shorts.Length], 0.12f);
            var shoes = RuntimeArt.MakeLit(ShoeCols[(h / 11) % ShoeCols.Length], 0.5f);
            var cap = RuntimeArt.MakeLit(capC, 0.3f);
            var hair = RuntimeArt.MakeLit(HairCols[(h / 17) % HairCols.Length], 0.25f);
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    if (n.Contains("Eye") || n.Contains("Mouth") || n.Contains("Socks") || n.Contains("CapBrim")) continue;   // keep as modelled
                    mats[i] = n.Contains("Skin") ? skin : n.Contains("Shirt") || n.Contains("Stripe") ? cloth : n.Contains("Shorts") ? shorts :
                              n.Contains("Shoes") ? shoes : n.Contains("Cap") ? cap : n.Contains("Hair") ? hair : mats[i];
                }
                rend.sharedMaterials = mats;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (rend is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                if (rend.gameObject.name.StartsWith("KidCap")) rend.gameObject.SetActive(wearsCap);
                if (rend.gameObject.name.StartsWith("KidHair")) rend.gameObject.SetActive(!wearsCap);
            }
            return true;
        }

        void PlaceBody(Avatar av)
        {
            var outward = SeatLayout.Outward(av.Seat);
            if (av.Model != null)
            {
                // pelvis over the back half of the seat, feet on the floor, facing the tower
                var facing = SeatLayout.Facing(av.Seat);
                var pelvisTarget = SeatLayout.ChairPosition(av.Seat) + outward * 2.6f;
                var rot = facing * av.FacingFix;
                var pos = pelvisTarget - facing * av.PelvisOffset;
                pos.y = SeatLayout.FloorY;
                av.Model.SetPositionAndRotation(pos, rot);
                av.Model.localScale = Vector3.one * av.ModelScale;
                return;
            }
            if (av.Torso == null) return;
            var hips = outward * (SeatLayout.ChairRadius + 0.4f) + Vector3.up * (SeatLayout.FloorY + 9.2f);
            av.Torso.SetPositionAndRotation(hips, SeatLayout.Facing(av.Seat) * Quaternion.Euler(8f, 0f, 0f));
        }

        /// <summary>Shorter body and legs and a big round head (the clips have no scale keys, so this sticks).</summary>
        static void ApplyKidProportions(Avatar av)
        {
            if (av.BSpine != null) av.BSpine.localScale = Vector3.one * KidSpine;
            if (av.BHead != null) av.BHead.localScale = Vector3.one * (KidHead / KidSpine);
            if (av.BThighL != null) av.BThighL.localScale = Vector3.one * KidThigh;
            if (av.BThighR != null) av.BThighR.localScale = Vector3.one * KidThigh;
        }

        /// <summary>The top of a player's head (their kid's head, or just above the camera for yourself) and their seat.</summary>
        public bool TryGetHeadTop(ulong clientId, out Vector3 top, out int seat)
        {
            top = Vector3.zero;
            seat = 0;
            if (!_avatars.TryGetValue(clientId, out var av) || av.Seat < 0) return false;
            seat = av.Seat;
            if (av.Local && Camera.main != null) top = Camera.main.transform.position + Vector3.up * 2.4f;
            else if (av.BHead != null) top = av.BHead.position + Vector3.up * (0.26f * av.ModelScale * KidHead);   // skull top: 26 cm over the head bone
            else if (av.Head != null) top = av.Head.position + Vector3.up * 2.2f;
            else top = SeatLayout.EyePosition(av.Seat) + Vector3.up * 2.4f;
            return true;
        }

        public bool IsLocal(ulong clientId) => _avatars.TryGetValue(clientId, out var av) && av.Local;

        /// <summary>0 = awake, 1 = bonked: the kid slumps forward onto the table (set by KidsRoomLife).</summary>
        public void SetKnockedOut(ulong clientId, float amount)
        {
            if (_avatars.TryGetValue(clientId, out var av)) av.KO = Mathf.Clamp01(amount);
        }

        public void ClearKnockouts()
        {
            foreach (var av in _avatars.Values) av.KO = 0f;
        }

        /// <summary>
        /// If the bone still holds exactly what we wrote last frame, the Animator did not touch it this frame: put the
        /// clean pose back before adding our offset again. If the Animator did rewrite it, its pose is the new clean one.
        /// </summary>
        static void RestoreBone(Transform bone, ref bool touched, Quaternion lastSet, ref Quaternion basePose)
        {
            if (touched && Quaternion.Angle(bone.localRotation, lastSet) < 0.01f) bone.localRotation = basePose;
            basePose = bone.localRotation;
        }

        /// <summary>
        /// The direction a remote head should face: the player's real camera direction when known, otherwise the
        /// fallback (tower / held block). Limited to what a neck can do: +-80 degrees sideways, 55 down, 40 up.
        /// </summary>
        static Vector3 HeadLookDir(ulong clientId, Vector3 bodyFwd, Vector3 fallback)
        {
            var want = fallback;
            if (HandSync.Hands.TryGetValue(clientId, out var hs) && hs.Look.sqrMagnitude > 0.5f) want = hs.Look;
            if (want.sqrMagnitude < 1e-4f) want = bodyFwd;
            var flat = new Vector3(want.x, 0f, want.z);
            float yaw = flat.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(bodyFwd, flat, Vector3.up) : 0f;
            yaw = Mathf.Clamp(yaw, -80f, 80f);
            float pitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(want.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, -55f, 40f);
            var h = Quaternion.AngleAxis(yaw, Vector3.up) * bodyFwd;
            return (h * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad)).normalized;
        }

        static Vector3 RestPoint(int seat, float side, Vector3 right)
        {
            var outward = SeatLayout.Outward(seat);
            return outward * (SeatLayout.TableRadius - 1.6f) + right * (2.3f * side) + Vector3.up * (JengaDims.TableTopY + 0.05f);
        }

        /// <summary>Keeps a resting/sliding hand inside the player's own part of the table (never in the tower).</summary>
        static Vector3 ClampToReach(int seat, Vector3 p)
        {
            var outward = SeatLayout.Outward(seat);
            var flat = new Vector3(p.x, 0f, p.z);
            float r = Mathf.Clamp(flat.magnitude, 3.4f, SeatLayout.TableRadius - 0.9f);
            float ang = Vector3.SignedAngle(outward, flat.sqrMagnitude > 1e-4f ? flat.normalized : outward, Vector3.up);
            ang = Mathf.Clamp(ang, -34f, 34f);
            var dir = Quaternion.AngleAxis(ang, Vector3.up) * outward;
            return dir * r + Vector3.up * (JengaDims.TableTopY + 0.05f);
        }

        static Quaternion Facing(Vector3 from, Vector3 to, float pitch)
        {
            var f = to - from;
            f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            return Quaternion.LookRotation(f.normalized, Vector3.up) * Quaternion.Euler(pitch, 0f, 0f);
        }

        // ------------------------------------------------------------------ Animate
        void Animate(NetworkGameManager game, ulong clientId, Avatar av, float tension)
        {
            var turns = game.Turns;
            var outward = SeatLayout.Outward(av.Seat);
            var forward = -outward;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var seatRight = right;

            // the rigged body keeps its own seated arms out of the way: the HandRig arms take over from the shoulders
            if (av.Model != null)
            {
                ApplyKidProportions(av);
                av.BUpperL.localScale = Vector3.one * 0.0001f;
                av.BUpperR.localScale = Vector3.one * 0.0001f;
                // lean over the table when the hand has to reach far (the tower is in the middle of a big table)
                av.Lean = Mathf.Lerp(av.Lean, av.LeanTarget, 1f - Mathf.Exp(-3.5f * Time.deltaTime));
                if (av.BSpine != null)
                {
                    // start from the clean pose every frame, otherwise the lean is added on top of last frame's lean
                    // whenever the Animator does not rewrite this bone - and the body keeps folding / spinning
                    RestoreBone(av.BSpine, ref av.SpineTouched, av.SpineSet, ref av.SpineBase);
                    if (av.Lean > 0.1f) av.BSpine.rotation = Quaternion.AngleAxis(av.Lean, seatRight) * av.BSpine.rotation;
                    av.SpineSet = av.BSpine.localRotation;
                    av.SpineTouched = true;
                }
            }

            // shoulders / head: from the camera for the local player (so leaning moves your arms), from the body otherwise
            Vector3 headPos, shoulderL, shoulderR;
            if (av.Local && Camera.main != null)
            {
                var cam = Camera.main.transform;
                headPos = cam.position;
                var flatFwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                if (flatFwd.sqrMagnitude < 0.01f) flatFwd = forward;
                right = Vector3.Cross(Vector3.up, flatFwd).normalized;
                // shoulders sit below and a little in front of the eyes, so the arm comes up from the bottom of the screen
                var shoulderCenter = headPos - flatFwd * 0.6f - Vector3.up * 4.2f;
                shoulderL = shoulderCenter - right * 2.3f;
                shoulderR = shoulderCenter + right * 2.3f;
            }
            else if (av.Model != null)
            {
                headPos = av.BHead.position + Vector3.up * 1.2f;
                shoulderL = av.BUpperL.position;
                shoulderR = av.BUpperR.position;
            }
            else
            {
                float idle = Mathf.Sin(Time.time * 0.9f + av.Seat) * 0.08f;
                headPos = outward * (SeatLayout.EyeRadius + 0.5f) + Vector3.up * (JengaDims.TableTopY + 6.4f + idle);
                var shoulderCenter = headPos + outward * 0.6f - Vector3.up * 3.0f;
                shoulderL = shoulderCenter - right * 2.1f;
                shoulderR = shoulderCenter + right * 2.1f;
            }
            av.Right.Shoulder = shoulderR;
            av.Left.Shoulder = shoulderL;

            bool playing = game.Phase.Value == GamePhase.Playing;
            bool active = playing && turns.ActiveClientId.Value == clientId;
            var stage = turns.Stage.Value;
            float wobble = game.Stability.Wobble.Value;
            Vector3 lookAt = new Vector3(0f, game.Tower.LayerCount.Value * JengaDims.BlockHeight * 0.5f, 0f);

            // free hands go round the tower instead of through it (one layer of headroom for the block being placed)
            if (playing)
            {
                float top = (game.Tower.LayerCount.Value + 1) * JengaDims.BlockHeight;
                float halfWidth = JengaDims.BlockLength * 0.5f + 0.05f;
                var center = JengaTower.BaseCenter + Vector3.up * (top * 0.5f);
                var half = new Vector3(halfWidth, top * 0.5f, halfWidth);
                av.Right.SetObstacle(center, half);
                av.Left.SetObstacle(center, half);
            }
            else
            {
                av.Right.ClearObstacle();
                av.Left.ClearObstacle();
            }

            // ---------------- right hand
            HandPoseId pose = HandPoseId.Flat;
            Vector3 contact = RestPoint(av.Seat, 1f, right);
            Quaternion orient = Facing(shoulderR, contact, 4f);
            float follow = 10f;
            bool fromBlock = false;

            if (active && (stage == TurnStage.Dragging || stage == TurnStage.Carrying))
            {
                var held = JengaBlock.Find(turns.HeldBlockId.Value);
                if (held != null)
                {
                    fromBlock = true;
                    var c = held.transform.position;
                    var axis = held.LongAxis;
                    // the end of the block that faces this player's shoulder
                    float s = Vector3.Dot(shoulderR - c, axis) >= 0 ? 1f : -1f;
                    var toward = axis * s;
                    if (stage == TurnStage.Dragging)
                    {
                        // pinch the near end, fingers pointing into the block
                        pose = HandPoseId.Pinch;
                        contact = c + toward * (JengaDims.BlockLength * 0.5f + 0.06f);
                        orient = Quaternion.LookRotation(-toward, Vector3.up) * Quaternion.Euler(12f, 0f, 0f);
                    }
                    else
                    {
                        // carrying: hold the near end between thumb and fingers, hand slightly BELOW the block's top,
                        // so neither the hand nor the arm covers the free slots you are aiming at
                        pose = HandPoseId.Pinch;
                        contact = c + toward * (JengaDims.BlockLength * 0.5f + 0.05f) - Vector3.up * (JengaDims.BlockHeight * 0.15f);
                        var flatToward = new Vector3(toward.x, 0f, toward.z).normalized;
                        orient = Quaternion.LookRotation(-flatToward, Vector3.up) * Quaternion.Euler(-6f, 0f, 0f);
                    }
                    follow = 40f;
                    lookAt = c;
                }
            }

            if (!fromBlock)
            {
                if (av.Local)
                {
                    var bic = BlockInteractionController.Instance;
                    if (active && stage == TurnStage.WaitingForGrab && bic != null && bic.HasHoverPoint)
                    {
                        contact = bic.HoverPoint + bic.HoverNormal * 0.05f;
                        if (bic.CursorPrompt == BlockInteractionController.Prompt.Grab)
                        {
                            pose = HandPoseId.Point;
                            orient = Facing(shoulderR, contact, 30f);
                        }
                        else
                        {
                            // locked block: the hand hesitates and pulls back
                            pose = HandPoseId.Recoil;
                            orient = Facing(shoulderR, contact, 20f);
                            contact -= (orient * Vector3.forward) * 1.4f + Vector3.down * -0.4f;
                        }
                        follow = 18f;
                    }
                    else if (bic != null && bic.HasTablePoint)
                    {
                        contact = active ? bic.TablePoint : ClampToReach(av.Seat, bic.TablePoint);
                        contact.y = JengaDims.TableTopY + 0.05f;
                        pose = tension > 0.55f && !active ? HandPoseId.Drum : HandPoseId.Flat;
                        orient = Facing(shoulderR, contact, 4f);
                        follow = 14f;
                    }
                    if (Time.time < av.KnockUntil) pose = HandPoseId.Knock;
                    contact = LimitHand(av.Seat, shoulderR, contact);
                    HandSync.SubmitLocal(contact, pose);
                }
                else if (HandSync.Hands.TryGetValue(clientId, out var net))
                {
                    contact = net.Contact;
                    pose = (HandPoseId)net.Pose;
                    float pitch = pose == HandPoseId.Point ? 30f : pose == HandPoseId.Recoil ? 20f : 4f;
                    orient = Facing(shoulderR, contact, pitch);
                    follow = 8f;
                    if (pose == HandPoseId.Point || pose == HandPoseId.Recoil) lookAt = contact;
                }
                if (Time.time < av.KnockUntil) pose = HandPoseId.Knock;
            }

            // remote bodies lean in as far as the hand needs (computed from where the hand wants to go)
            if (av.Model != null)
            {
                var hip = outward * (SeatLayout.ChairRadius + 2.6f);
                float need = new Vector2(contact.x - hip.x, contact.z - hip.z).magnitude;
                av.LeanTarget = Mathf.Clamp((need - 11f) * 4.5f, 0f, 38f);
                if (av.KO > 0f) av.LeanTarget = Mathf.Lerp(av.LeanTarget, 62f, av.KO);
            }
            if (!fromBlock) contact = LimitHand(av.Seat, shoulderR, contact);
            av.Right.SetPose(pose);
            av.Right.SetContact(contact, orient, follow, fromBlock);
            float handlingWobble = active && fromBlock ? wobble * 1.2f : 0f;
            av.Right.Tremble = Mathf.Clamp01(0.06f + tension * 0.45f + handlingWobble);

            // ---------------- left hand: rests flat, drums when the room gets tense, steadies the table while you carry
            var leftPoint = RestPoint(av.Seat, -1f, right);
            if (av.Local) leftPoint = Vector3.Lerp(leftPoint, ClampToReach(av.Seat, shoulderL + (right * -1.5f) - outward * 3f), 0.5f);
            leftPoint.y = JengaDims.TableTopY + 0.05f;
            var leftPose = tension > 0.5f && !active ? HandPoseId.Drum : HandPoseId.Flat;
            var leftOrient = Facing(shoulderL, leftPoint, 4f);
            float leftFollow = 6f;
            av.Left.Tremble = Mathf.Clamp01(0.05f + tension * 0.4f);

            // the beer mug lives on the table in front of you (left side) unless a gesture picks it up
            var mugHome = RestPoint(av.Seat, -1f, seatRight) + forward * 1.2f - seatRight * 1.4f;
            mugHome.y = JengaDims.TableTopY;
            bool mugInHand = false;

            if (av.G != Gesture.None)
            {
                float e = Time.time - av.GStart, len = GestureLength(av.G);
                if (e >= len) { av.G = Gesture.None; if (av.Local && CameraController.Instance != null) CameraController.Instance.GesturePitch = 0f; }
                else
                {
                    // where the hand is shown: in front of the chest for others, lower left of the screen for yourself
                    Vector3 f = forward, rgt = seatRight, show;
                    if (av.Local && Camera.main != null)
                    {
                        var cam = Camera.main.transform;
                        f = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
                        rgt = Vector3.Cross(Vector3.up, f).normalized;
                        show = cam.position + cam.forward * 5.2f - Vector3.up * 1.4f - rgt * 2.0f;
                    }
                    // others: held up just above the table edge in front of them, where everyone's camera looks
                    else show = outward * (SeatLayout.TableRadius + 0.6f) + Vector3.up * (JengaDims.TableTopY + 5.2f) - rgt * 2.0f;

                    float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.35f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - e) / 0.35f));
                    var palmToOthers = Quaternion.LookRotation(Vector3.up, -f);   // fingers up, palm towards the table
                    var backToOthers = Quaternion.LookRotation(Vector3.up, f);    // fingers up, back of the hand towards the table
                    Vector3 target = show;
                    Quaternion gOrient = palmToOthers;
                    switch (av.G)
                    {
                        case Gesture.Ok: leftPose = HandPoseId.Ok; break;
                        case Gesture.Horns:
                            leftPose = HandPoseId.Horns;
                            target += Vector3.up * Mathf.Abs(Mathf.Sin(e * 8f)) * 0.5f;
                            break;
                        case Gesture.MiddleFinger:
                        {
                            // "nyah nyah": thumb on the nose, fingers spread forward and wiggling
                            leftPose = HandPoseId.Open;
                            Vector3 nose;
                            if (av.Local && Camera.main != null)
                            {
                                var cam = Camera.main.transform;
                                nose = cam.position + cam.forward * 2.4f - Vector3.up * 0.5f;
                            }
                            else if (av.BHead != null) nose = av.BHead.position + f * 2.6f + Vector3.up * 1.2f;
                            else nose = headPos + f * 1.8f;
                            target = nose + f * 1.1f;
                            gOrient = Quaternion.AngleAxis(Mathf.Sin(e * 22f) * 14f, f) * Quaternion.LookRotation((f + Vector3.up * 0.35f).normalized, rgt);
                            break;
                        }
                        case Gesture.Wave:
                            leftPose = HandPoseId.Open;
                            gOrient = Quaternion.AngleAxis(Mathf.Sin(e * 10f) * 28f, f) * palmToOthers;
                            target += rgt * Mathf.Sin(e * 10f) * 0.35f + Vector3.up * 0.6f;
                            break;
                        case Gesture.Beer:
                        {
                            // reach for the mug, lift it to the mouth, tilt, put it back
                            leftPose = HandPoseId.MugGrip;
                            var grip = mugHome + Vector3.up * 1.5f - rgt * 0.95f;
                            Vector3 mouth;
                            if (av.Local && Camera.main != null)
                            {
                                var cam = Camera.main.transform;
                                mouth = cam.position + cam.forward * 1.9f - Vector3.up * 1.0f - rgt * 0.2f;
                            }
                            else if (av.BHead != null) mouth = av.BHead.position + f * 1.7f - Vector3.up * 0.6f - rgt * 0.95f;
                            else mouth = headPos + f * 1.7f - Vector3.up * 1.2f - rgt * 0.95f;
                            float lift = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 1.15f, e)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f, 3.1f, e)));
                            float tilt = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.1f, 1.5f, e)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.2f, 2.55f, e)));
                            float a = tilt * 62f * Mathf.Deg2Rad;
                            var fingers = f * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a);
                            gOrient = Quaternion.LookRotation(fingers, -rgt);
                            target = Vector3.Lerp(grip, mouth, lift);
                            mugInHand = e > 0.45f && e < 3.2f;
                            w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.45f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - e) / 0.35f));
                            if (av.Local && CameraController.Instance != null) CameraController.Instance.GesturePitch = -14f * tilt;
                            if (tilt > 0.8f && (av.GFlags & 1) == 0)
                            {
                                av.GFlags |= 1;
                                var gulp = SecretJengaAssets.Instance.gulp;
                                if (gulp != null) AudioManager.Instance?.PlayAt(gulp, mouth, av.Local ? 0.8f : 0.5f, Random.Range(0.92f, 1.06f));
                            }
                            break;
                        }
                        case Gesture.Smoke:
                        {
                            // pick the cigarette off the ashtray, take a long drag, lower the hand, blow the smoke out, put it back
                            leftPose = HandPoseId.Cigarette;
                            var trayAt = mugHome - rgt * 2.6f + f * 0.6f;
                            Vector3 mouth;
                            if (av.Local && Camera.main != null)
                            {
                                var cam = Camera.main.transform;
                                mouth = cam.position + cam.forward * 1.3f - Vector3.up * 0.75f;
                            }
                            else if (av.BHead != null) mouth = av.BHead.position + f * 1.5f - Vector3.up * 0.5f;
                            else mouth = headPos + f * 1.5f - Vector3.up * 1.0f;
                            var pick = trayAt + Vector3.up * 1.2f - f * 0.6f;
                            var atMouth = mouth + f * 0.9f - Vector3.up * 1.5f;            // fingertips (and the filter) at the lips
                            var lowered = mouth + f * 2.8f - Vector3.up * 3.2f - rgt * 1.0f;
                            float toMouth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1.1f, e));
                            float away = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.0f, 2.5f, e));
                            float back = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.7f, 4.25f, e));
                            target = Vector3.Lerp(Vector3.Lerp(Vector3.Lerp(pick, atMouth, toMouth), lowered, away), pick, back);
                            // palm towards the face at the lips, then turned sideways while the hand rests lower
                            var atLips = Quaternion.LookRotation(Vector3.up, f);
                            var relaxed = Quaternion.LookRotation((f + Vector3.up * 0.6f).normalized, -rgt);
                            gOrient = Quaternion.Slerp(Quaternion.Slerp(relaxed, atLips, toMouth), relaxed, away);
                            w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.45f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - e) / 0.35f));
                            float drag = toMouth * (1f - away);
                            if (av.Local && CameraController.Instance != null) CameraController.Instance.GesturePitch = -5f * drag;
                            // ember glows during the drag
                            if (av.CigEmber != null)
                            {
                                av.CigEmber.GetPropertyBlock(_mpb);
                                _mpb.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f) * Mathf.Lerp(1.5f, 6f, drag * (0.75f + 0.25f * Mathf.Sin(e * 30f))));
                                av.CigEmber.SetPropertyBlock(_mpb);
                            }
                            if (e > 1.1f && (av.GFlags & 1) == 0)
                            {
                                av.GFlags |= 1;
                                Puff(av.Smoke, mouth + f * 0.4f, (f + Vector3.up * 0.25f).normalized, 34, 1.6f, 0.7f);
                                var ex = SecretJengaAssets.Instance.exhale;
                                if (ex != null) AudioManager.Instance?.PlayAt(ex, mouth, av.Local ? 0.7f : 0.45f, Random.Range(0.9f, 1.05f));
                            }
                            break;
                        }
                    }
                    leftPoint = Vector3.Lerp(leftPoint, target, w);
                    leftOrient = Quaternion.Slerp(leftOrient, gOrient, w);
                    leftFollow = 22f;
                    if (w > 0.5f) av.Left.Tremble *= 0.4f;
                }
            }
            av.Left.SetPose(leftPose);
            av.Left.SetContact(leftPoint, leftOrient, leftFollow);

            // ---------------- ashtray + cigarette (+ a thin wisp from the ember all the time)
            if (av.Ashtray != null)
            {
                var trayHome = mugHome - seatRight * 2.6f + forward * 0.6f;
                av.Ashtray.position = trayHome;
                bool cigInHand = av.G == Gesture.Smoke && Time.time - av.GStart > 0.45f && Time.time - av.GStart < 4.15f;
                if (cigInHand)
                {
                    // between index and middle finger, filter towards the palm, lit end out of the back of the hand
                    var wr = av.Left.Wrist.rotation;
                    var up = wr * Vector3.up;
                    av.Cig.SetPositionAndRotation(av.Left.CigarettePoint + up * 0.35f, Quaternion.FromToRotation(Vector3.up, up));
                }
                else
                {
                    // standing in its cup, candy up
                    av.Cig.SetPositionAndRotation(trayHome + Vector3.up * 1.6f, Quaternion.AngleAxis(180f, seatRight) * Quaternion.AngleAxis(8f, forward));
                    if (av.CigEmber != null && av.G != Gesture.Smoke)
                    {
                        av.CigEmber.GetPropertyBlock(_mpb);
                        _mpb.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f) * (1.2f + 0.4f * Mathf.PerlinNoise(Time.time * 2f, av.Seat)));
                        av.CigEmber.SetPropertyBlock(_mpb);
                    }
                }
                av.WispTimer -= Time.deltaTime;
                if (av.WispTimer <= 0f && av.Smoke != null && av.CigEmber != null)
                {
                    av.WispTimer = Random.Range(0.18f, 0.32f);
                    Puff(av.Smoke, av.CigEmber.transform.position, Vector3.up, 1, 0.35f, 0.22f);
                }
            }

            if (av.Mug != null)
            {
                if (mugInHand)
                {
                    // the mug follows the real hand: thumb side up, palm against the glass
                    var wr = av.Left.Wrist.rotation;
                    var mugUp = wr * Vector3.right;
                    var palmDir = wr * Vector3.down;
                    var bottom = av.Left.PalmPosition + palmDir * 0.95f - mugUp * 1.5f;
                    av.Mug.SetPositionAndRotation(bottom, Quaternion.LookRotation(Vector3.ProjectOnPlane(-palmDir, mugUp).normalized, mugUp) * Quaternion.Euler(0f, -90f, 0f));
                }
                else
                {
                    var homeRot = Quaternion.LookRotation(seatRight, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
                    av.Mug.position = Vector3.Lerp(av.Mug.position, mugHome, 1f - Mathf.Exp(-14f * Time.deltaTime));
                    av.Mug.rotation = Quaternion.Slerp(av.Mug.rotation, homeRot, 1f - Mathf.Exp(-14f * Time.deltaTime));
                }
            }

            // ---------------- body / head (remote only)
            if (av.Local) return;
            if (av.Model != null)
            {
                // the head looks where that player's camera looks (sent through HandSync); bots look at what matters
                var bodyFwd = SeatLayout.Facing(av.Seat) * Vector3.forward;
                var dir = HeadLookDir(clientId, bodyFwd, (lookAt - av.BHead.position).normalized);
                if (av.KO > 0f) dir = Vector3.Slerp(dir, (bodyFwd * 0.25f - Vector3.up + Vector3.Cross(Vector3.up, bodyFwd) * 0.35f).normalized, av.KO);
                if (av.LookDir.sqrMagnitude < 0.5f) av.LookDir = dir;
                av.LookDir = Vector3.Slerp(av.LookDir, dir, 1f - Mathf.Exp(-9f * Time.deltaTime)).normalized;
                RestoreBone(av.BHead, ref av.HeadTouched, av.HeadSet, ref av.HeadBase);
                if (av.HasHeadOffset)
                {
                    // absolute and level: no tilt from the leaning spine or the idle animation
                    av.BHead.rotation = Quaternion.LookRotation(av.LookDir, Vector3.up) * av.HeadOffset;
                }
                else
                {
                    var delta = Quaternion.FromToRotation(bodyFwd, av.LookDir);
                    av.BHead.rotation = delta * av.BHead.rotation;
                }
                av.HeadSet = av.BHead.localRotation;
                av.HeadTouched = true;
                av.Label.position = av.BHead.position + Vector3.up * 8.5f;
            }
            else
            {
                var dir = HeadLookDir(clientId, SeatLayout.Facing(av.Seat) * Vector3.forward, (lookAt - headPos).normalized);
                var headRot = Quaternion.LookRotation(dir, Vector3.up);
                av.Head.SetPositionAndRotation(headPos, Quaternion.Slerp(av.Head.rotation, headRot, 1f - Mathf.Exp(-9f * Time.deltaTime)));
                av.Torso.localScale = new Vector3(1f, 1f + Mathf.Sin(Time.time * 1.4f + av.Seat) * 0.008f, 1f);
                av.Label.position = headPos + Vector3.up * 2.9f;
            }
            if (Camera.main != null) av.Label.rotation = Quaternion.LookRotation(av.Label.position - Camera.main.transform.position);
        }
    }
}
