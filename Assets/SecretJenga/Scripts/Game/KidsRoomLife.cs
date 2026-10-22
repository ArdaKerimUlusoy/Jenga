using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Makes the kids' bedroom alive and stages the end of a lost round. Purely local and cosmetic (nothing here is
    /// networked; every client plays the same scene from the replicated game phase and loser).
    ///
    ///  * Other kids are in the room: two sit chatting on the bunk bed, one plays on the floor mat, one kneels by the
    ///    toy shelf building with blocks, one lounges on the beanbag, and one wanders about, stopping to look around.
    ///  * When a player knocks the tower over, the bedroom door creaks open and a big kid walks in with a squeaky
    ///    plastic hammer, goes round the table to the one who dropped it, raises the hammer... and BONK (squeak!).
    ///    The bonked kid slumps onto the table with stars circling their head; the others cheer. Everyone's camera
    ///    turns to watch; if it is you, your view drops onto the table, the stars spin and the birdies sing.
    ///    The results screen waits for it (HoldResults), then the big kid walks out and the door shuts.
    /// </summary>
    public class KidsRoomLife : MonoBehaviour
    {
        public static KidsRoomLife Instance { get; private set; }
        /// <summary>True while the hammer scene is playing - the results screen waits for it.</summary>
        public static bool HoldResults { get; private set; }

        // room layout (must match the generator: RoomHalf = 50, the door on the south wall)
        const float RoomHalf = 50f;
        static float F => SeatLayout.FloorY;
        const float WalkRadius = 26f;      // the big kid walks round the table on this circle (clear of chairs and furniture)
        const float StandRadius = 27.5f;   // ...and stands here behind the loser's chair
        const float StandingHead = 22f;    // head-bone height of a standing kid (about 1.1 m)
        const float BigKidHead = 29f;      // the big kid with the hammer (about 1.45 m)
        const float KidSpine = 0.88f, KidHeadScale = 1.5f, KidThigh = 0.8f;

        class Kid
        {
            public Transform Model, Head, Spine, Spine2, ThighL, ThighR, UpperL, UpperR, LowerR, HandR, Pelvis;
            public Animator Anim;
            public Quaternion FacingFix = Quaternion.identity;
            public float Scale = 20f;
            public string Clip;
            // wanderer
            public bool Walker;
            public float Angle, From, To, Wait;
            public int Leg;
            // reaction
            public float CheerUntil;
            public string BaseClip;
        }

        readonly List<Kid> _kids = new List<Kid>();
        Kid _big;
        // the big kid's own right arm is posed by a two-bone IK after the animation (no extra arm)
        bool _carry = true;
        Vector3 _grip, _hammerUp = Vector3.up;
        float _bend;
        Transform _hammer, _door, _starRing;
        readonly List<Transform> _stars = new List<Transform>();
        AudioSource _birdies;
        Coroutine _scene;
        GamePhase _lastPhase = (GamePhase)255;
        ulong _bonked = ulong.MaxValue;
        float _doorAngle, _doorTarget, _stepTimer;
        Canvas _overlay;
        UnityEngine.UI.Text _bonkText;
        CanvasGroup _bonkGroup, _flashGroup;
        bool _built;

        void Awake()
        {
            Instance = this;
            TameTheLight();   // in Awake: before the AtmosphereDirector reads the lamp's glow in its Start
        }
        void OnDestroy() { if (Instance == this) Instance = null; HoldResults = false; }

        void Start()
        {
            var d = GameObject.Find("BedroomDoor");
            if (d != null) _door = d.transform;
            Build();
        }

        /// <summary>
        /// The light shafts and dust motes of older generated scenes washed the view out (white glare with dark streaks
        /// when looking past them): switch them off, and keep the bloom and the glowing materials gentle - even if the
        /// scene was not regenerated yet.
        /// </summary>
        void TameTheLight()
        {
            foreach (var name in new[] { "Moon Shafts", "Sun Shafts", "Dust Motes" })
            {
                var go = GameObject.Find(name);
                if (go != null) go.SetActive(false);
            }
            var vol = FindFirstObjectByType<UnityEngine.Rendering.Volume>();
            if (vol != null && vol.profile != null)
            {
                if (vol.profile.TryGet<UnityEngine.Rendering.Universal.Bloom>(out var bloom))
                {
                    bloom.intensity.Override(Mathf.Min(bloom.intensity.value, 0.25f));
                    bloom.threshold.Override(Mathf.Max(bloom.threshold.value, 1.25f));
                }
            }
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                float cap = m.name.StartsWith("M_PaperLantern") ? 0.9f : m.name.StartsWith("M_WindowNight") ? 0.55f : m.name.StartsWith("M_FairyBulb") ? 2.2f : m.name.StartsWith("M_Hallway") ? 0.7f : -1f;
                if (cap < 0f) continue;
                var c = m.GetColor("_EmissionColor");
                if (c.maxColorComponent > cap) m.SetColor("_EmissionColor", c * (cap / c.maxColorComponent));
            }
        }

        // ------------------------------------------------------------------ building kids
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

        static readonly Color[] Shirt = { new Color(0.95f, 0.45f, 0.4f), new Color(0.4f, 0.7f, 0.95f), new Color(0.98f, 0.85f, 0.35f), new Color(0.5f, 0.82f, 0.5f), new Color(0.78f, 0.6f, 0.92f), new Color(0.98f, 0.65f, 0.35f), new Color(0.95f, 0.95f, 0.92f) };
        static readonly Color[] Short = { new Color(0.24f, 0.3f, 0.48f), new Color(0.75f, 0.55f, 0.35f), new Color(0.3f, 0.45f, 0.3f), new Color(0.55f, 0.2f, 0.25f), new Color(0.35f, 0.35f, 0.38f) };
        static readonly Color[] Shoe = { new Color(0.9f, 0.2f, 0.18f), new Color(0.95f, 0.95f, 0.95f), new Color(0.2f, 0.4f, 0.85f), new Color(0.15f, 0.15f, 0.17f), new Color(0.98f, 0.75f, 0.2f) };
        static readonly Color[] Hair = { new Color(0.22f, 0.13f, 0.07f), new Color(0.08f, 0.06f, 0.05f), new Color(0.75f, 0.55f, 0.25f), new Color(0.55f, 0.25f, 0.1f) };
        static readonly Color[] Skin = { new Color(0.95f, 0.78f, 0.66f), new Color(0.84f, 0.64f, 0.5f), new Color(0.62f, 0.44f, 0.33f), new Color(0.98f, 0.84f, 0.74f) };

        Kid MakeKid(int seed, float headHeight, string name)
        {
            var a = SecretJengaAssets.Instance;
            if (a.cowboyModel == null || a.cowboyAnimator == null) return null;
            var go = Instantiate(a.cowboyModel, transform, false);
            go.name = name;
            var k = new Kid { Model = go.transform };
            k.Head = FindDeep(k.Model, "Head");
            k.Spine = FindDeep(k.Model, "spine_01");
            k.ThighL = FindDeep(k.Model, "thigh_l");
            k.ThighR = FindDeep(k.Model, "thigh_r");
            k.UpperL = FindDeep(k.Model, "upperarm_l");
            k.UpperR = FindDeep(k.Model, "upperarm_r");
            k.LowerR = FindDeep(k.Model, "lowerarm_r");
            k.HandR = FindDeep(k.Model, "hand_r");
            k.Spine2 = FindDeep(k.Model, "spine_02");
            k.Pelvis = FindDeep(k.Model, "pelvis");
            var footL = FindDeep(k.Model, "foot_l");
            var footR = FindDeep(k.Model, "foot_r");
            if (k.Head == null || k.Pelvis == null) { Destroy(go); return null; }

            k.Anim = go.GetComponentInChildren<Animator>();
            if (k.Anim == null) k.Anim = go.AddComponent<Animator>();
            k.Anim.runtimeAnimatorController = a.cowboyAnimator;
            k.Anim.applyRootMotion = false;
            k.Anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Proportions(k);

            // facing: from the seated pose (feet in front of the pelvis), like the players
            k.Model.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            k.Model.localScale = Vector3.one;
            k.Anim.Play("Sitting_Idle_Loop", 0, 0f);
            k.Anim.Update(0f);
            var fwd = Vector3.forward;
            if (footL != null && footR != null)
            {
                fwd = (footL.position + footR.position) * 0.5f - k.Pelvis.position;
                fwd.y = 0f;
                fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            }
            k.FacingFix = Quaternion.FromToRotation(fwd, Vector3.forward);
            // size: from the standing pose
            k.Anim.Play("Idle_Loop", 0, 0f);
            k.Anim.Update(0f);
            float hh = k.Head.position.y;
            k.Scale = hh > 0.01f ? headHeight / hh : 20f;
            k.Model.localScale = Vector3.one * k.Scale;

            // clothes
            var r = new System.Random(seed);
            bool cap = r.Next(2) == 0;
            var skin = RuntimeArt.MakeLit(Skin[r.Next(Skin.Length)], 0.3f);
            var shirt = RuntimeArt.MakeLit(Shirt[r.Next(Shirt.Length)], 0.15f);
            var shorts = RuntimeArt.MakeLit(Short[r.Next(Short.Length)], 0.12f);
            var shoes = RuntimeArt.MakeLit(Shoe[r.Next(Shoe.Length)], 0.5f);
            var capM = RuntimeArt.MakeLit(Shirt[r.Next(Shirt.Length)] * 0.85f, 0.3f);
            var hair = RuntimeArt.MakeLit(Hair[r.Next(Hair.Length)], 0.25f);
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    if (n.Contains("Eye") || n.Contains("Mouth") || n.Contains("Socks") || n.Contains("CapBrim")) continue;
                    mats[i] = n.Contains("Skin") ? skin : n.Contains("Shirt") || n.Contains("Stripe") ? shirt : n.Contains("Shorts") ? shorts :
                              n.Contains("Shoes") ? shoes : n.Contains("Cap") ? capM : n.Contains("Hair") ? hair : mats[i];
                }
                rend.sharedMaterials = mats;
                if (rend is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                if (rend.gameObject.name.StartsWith("KidCap")) rend.gameObject.SetActive(cap);
                if (rend.gameObject.name.StartsWith("KidHair")) rend.gameObject.SetActive(!cap);
            }
            return k;
        }

        static void Proportions(Kid k)
        {
            if (k.Spine != null) k.Spine.localScale = Vector3.one * KidSpine;
            if (k.Head != null) k.Head.localScale = Vector3.one * (KidHeadScale / KidSpine);
            if (k.ThighL != null) k.ThighL.localScale = Vector3.one * KidThigh;
            if (k.ThighR != null) k.ThighR.localScale = Vector3.one * KidThigh;
        }

        void Play(Kid k, string clip, float fade = 0.25f, float speed = 1f)
        {
            if (k == null || k.Anim == null || k.Clip == clip) return;
            k.Clip = clip;
            k.Anim.speed = speed;
            if (fade <= 0f) k.Anim.Play(clip, 0, 0f);
            else k.Anim.CrossFadeInFixedTime(clip, fade);
        }

        /// <summary>Feet on the floor at pos, facing dir.</summary>
        static void PlaceStanding(Kid k, Vector3 pos, Vector3 dir)
        {
            dir.y = 0f;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.forward;
            k.Model.SetPositionAndRotation(new Vector3(pos.x, F, pos.z), Quaternion.LookRotation(dir.normalized, Vector3.up) * k.FacingFix);
        }

        /// <summary>Sitting: the pelvis over seat (x, z), the model root on the floor (+ lift), facing dir.</summary>
        static void PlaceSeated(Kid k, Vector3 seat, Vector3 dir, float lift = 0f)
        {
            dir.y = 0f;
            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up) * k.FacingFix;
            k.Model.SetPositionAndRotation(new Vector3(seat.x, F + lift, seat.z), rot);
            k.Anim.Update(0f);
            var off = k.Pelvis.position - k.Model.position;
            off.y = 0f;
            k.Model.position -= off;
        }

        void Build()
        {
            if (_built) return;
            _built = true;
            // the kids in the room (seeds fixed so every player sees the same kids)
            var bed1 = MakeKid(11, StandingHead, "Kid_Bed1");
            if (bed1 == null) return;   // no character model: an empty (but cosy) room
            Play(bed1, "Sitting_Talking_Loop", 0f, 0.95f);
            PlaceSeated(bed1, new Vector3(-RoomHalf + 20.5f, 0f, -2f), new Vector3(1f, 0f, 0.15f));
            bed1.BaseClip = bed1.Clip;
            _kids.Add(bed1);

            var bed2 = MakeKid(23, StandingHead, "Kid_Bed2");
            Play(bed2, "Sitting_Nodding_Loop", 0f, 1.05f);
            PlaceSeated(bed2, new Vector3(-RoomHalf + 20.5f, 0f, 12f), new Vector3(1f, 0f, -0.35f));
            bed2.BaseClip = bed2.Clip;
            _kids.Add(bed2);

            var floorKid = MakeKid(37, StandingHead, "Kid_Floor");
            Play(floorKid, "GroundSit_Idle_Loop", 0f);
            PlaceStanding(floorKid, new Vector3(26f, 0f, 22f), new Vector3(-0.3f, 0f, 1f));
            floorKid.BaseClip = floorKid.Clip;
            _kids.Add(floorKid);

            var builder = MakeKid(41, StandingHead, "Kid_Blocks");
            Play(builder, "Fixing_Kneeling", 0f, 0.9f);
            PlaceStanding(builder, new Vector3(-13f, 0f, 32.5f), new Vector3(0.1f, 0f, 1f));
            builder.BaseClip = builder.Clip;
            _kids.Add(builder);

            var lounger = MakeKid(53, StandingHead, "Kid_Beanbag");
            Play(lounger, "Sitting_Idle03_Loop", 0f, 0.85f);
            PlaceSeated(lounger, new Vector3(36.5f, 0f, 36.5f), new Vector3(-1f, 0f, -1f), 1.5f);
            lounger.BaseClip = lounger.Clip;
            _kids.Add(lounger);

            var walker = MakeKid(67, StandingHead, "Kid_Wanderer");
            walker.Walker = true;
            walker.Angle = 50f;
            walker.From = 50f;
            walker.To = 130f;
            walker.Wait = 2f;
            Play(walker, "Idle_LookAround_Loop", 0f);
            walker.BaseClip = "Idle_LookAround_Loop";
            _kids.Add(walker);

            // the big kid with the squeaky hammer waits outside the door
            _big = MakeKid(97, BigKidHead, "Kid_WithHammer");
            if (_big != null)
            {
                _hammer = BuildHammer();
                _big.Model.gameObject.SetActive(false);
                _hammer.gameObject.SetActive(false);
            }

            BuildStars();
            BuildOverlay();
            var go = new GameObject("Birdies");
            go.transform.SetParent(transform, false);
            _birdies = go.AddComponent<AudioSource>();
            _birdies.loop = true;
            _birdies.playOnAwake = false;
            _birdies.spatialBlend = 0.75f;
            _birdies.minDistance = 10f;
            _birdies.maxDistance = 140f;
        }

        Transform BuildHammer()
        {
            var root = new GameObject("SqueakyHammer").transform;
            root.SetParent(transform, false);
            void Piece(string n, Mesh m, Color c, float smooth)
            {
                var g = new GameObject(n);
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = m;
                g.AddComponent<MeshRenderer>().sharedMaterial = RuntimeArt.MakeLit(c, smooth);
            }
            Piece("Handle", KidsRoomMeshes.HammerHandle(), new Color(0.98f, 0.8f, 0.15f), 0.75f);
            Piece("Head", KidsRoomMeshes.HammerHead(), new Color(0.92f, 0.18f, 0.16f), 0.8f);
            Piece("Caps", KidsRoomMeshes.HammerCaps(), new Color(0.2f, 0.45f, 0.95f), 0.8f);
            return root;
        }

        // ------------------------------------------------------------------ stars, BONK!, flash
        static Texture2D _starTex;
        static Texture2D StarTexture()
        {
            if (_starTex != null) return _starTex;
            const int S = 64;
            _starTex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "TLB_Star" };
            var px = new Color[S * S];
            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx) + Mathf.PI / 2f;
                float edge = Mathf.Lerp(0.42f, 0.95f, Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2.5f)), 3f));   // five points
                float alpha = Mathf.Clamp01((edge - r) * 14f);
                px[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
            _starTex.SetPixels(px);
            _starTex.Apply();
            return _starTex;
        }

        void BuildStars()
        {
            _starRing = new GameObject("BonkStars").transform;
            _starRing.SetParent(transform, false);
            var quad = SaloonMeshes.Quad(1.6f, 1.6f, "TLB_StarQuad");
            var mat = RuntimeArt.MakeGhost(new Color(1f, 0.88f, 0.25f, 1f));
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", StarTexture());
            mat.mainTexture = StarTexture();
            for (int i = 0; i < 5; i++)
            {
                var g = new GameObject("Star");
                g.transform.SetParent(_starRing, false);
                g.AddComponent<MeshFilter>().sharedMesh = quad;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _stars.Add(g.transform);
            }
            _starRing.gameObject.SetActive(false);
        }

        void BuildOverlay()
        {
            var go = new GameObject("BonkOverlay", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            go.transform.SetParent(transform, false);
            _overlay = go.GetComponent<Canvas>();
            _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlay.sortingOrder = 40;
            var sc = go.GetComponent<UnityEngine.UI.CanvasScaler>();
            sc.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;

            var flash = new GameObject("Flash", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
            flash.transform.SetParent(go.transform, false);
            var frt = (RectTransform)flash.transform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.offsetMin = frt.offsetMax = Vector2.zero;
            flash.GetComponent<UnityEngine.UI.Image>().color = new Color(1f, 1f, 0.92f, 1f);
            flash.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            _flashGroup = flash.GetComponent<CanvasGroup>();
            _flashGroup.alpha = 0f;
            _flashGroup.blocksRaycasts = false;

            var t = new GameObject("Bonk", typeof(RectTransform), typeof(UnityEngine.UI.Text), typeof(CanvasGroup));
            t.transform.SetParent(go.transform, false);
            _bonkText = t.GetComponent<UnityEngine.UI.Text>();
            var a = SecretJengaAssets.Instance;
            _bonkText.font = a.fontTitle != null ? a.fontTitle : RuntimeArt.Font;
            _bonkText.fontSize = 150;
            _bonkText.alignment = TextAnchor.MiddleCenter;
            _bonkText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _bonkText.verticalOverflow = VerticalWrapMode.Overflow;
            _bonkText.color = new Color(1f, 0.86f, 0.2f);
            _bonkText.raycastTarget = false;
            _bonkText.text = "BONK!";
            var o = t.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = new Color(0.55f, 0.12f, 0.1f);
            o.effectDistance = new Vector2(6f, -6f);
            var sh = t.AddComponent<UnityEngine.UI.Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.6f);
            sh.effectDistance = new Vector2(8f, -12f);
            var rt = (RectTransform)t.transform;
            rt.sizeDelta = new Vector2(900, 260);
            _bonkGroup = t.GetComponent<CanvasGroup>();
            _bonkGroup.alpha = 0f;
            _bonkGroup.blocksRaycasts = false;
        }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            var game = NetworkGameManager.Instance;
            var phase = game != null && game.IsSpawned ? game.Phase.Value : GamePhase.Lobby;
            if (phase != _lastPhase)
            {
                var prev = _lastPhase;
                _lastPhase = phase;
                if (phase == GamePhase.GameOver && prev == GamePhase.Playing && game.LoserClientId.Value != ulong.MaxValue && _big != null)
                {
                    if (_scene != null) StopCoroutine(_scene);
                    _scene = StartCoroutine(BonkScene(game.LoserClientId.Value));
                }
                else if (phase != GamePhase.GameOver) ResetScene();
            }

            UpdateKids();
            UpdateDoor();
            UpdateStars();
        }

        void ResetScene()
        {
            if (_scene != null) { StopCoroutine(_scene); _scene = null; }
            HoldResults = false;
            if (PlayerAvatars.Instance != null) PlayerAvatars.Instance.ClearKnockouts();
            var cc = CameraController.Instance;
            if (cc != null) { cc.KO = 0f; cc.CineWeight = 0f; }
            _bonked = ulong.MaxValue;
            if (_starRing != null) _starRing.gameObject.SetActive(false);
            if (_birdies != null) _birdies.Stop();
            if (_big != null) { _big.Model.gameObject.SetActive(false); _hammer.gameObject.SetActive(false); }
            _doorTarget = 0f;
        }

        void UpdateKids()
        {
            float dt = Time.deltaTime;
            foreach (var k in _kids)
            {
                if (k.CheerUntil > 0f && Time.time > k.CheerUntil)
                {
                    k.CheerUntil = 0f;
                    Play(k, k.BaseClip);
                }
                if (!k.Walker) continue;
                // the wanderer strolls along an arc on the north side of the room, stops, looks around, walks back
                bool watching = HoldResults;
                if (k.Wait > 0f || watching || k.CheerUntil > 0f)
                {
                    k.Wait -= dt;
                    if (k.CheerUntil <= 0f) Play(k, "Idle_LookAround_Loop");
                    var at = Pos(k.Angle, 28f);
                    var face = watching && _bonkedHead != Vector3.zero ? _bonkedHead - at : -at;
                    PlaceStanding(k, at, Vector3.Slerp(Facing(k), face, 1f - Mathf.Exp(-3f * dt)));
                    if (k.Wait <= 0f && !watching && k.CheerUntil <= 0f) { var tmp = k.From; k.From = k.To; k.To = tmp; }
                    continue;
                }
                Play(k, "Walk_Loop", 0.25f, 0.85f);
                float speed = 16f / 28f * Mathf.Rad2Deg;          // ~16 units per second along the arc
                float step = Mathf.Sign(k.To - k.Angle) * speed * dt;
                if (Mathf.Abs(k.To - k.Angle) <= Mathf.Abs(step)) { k.Angle = k.To; k.Wait = Random.Range(2.5f, 5f); }
                else k.Angle += step;
                var p = Pos(k.Angle, 28f);
                var tangent = Pos(k.Angle + Mathf.Sign(step) * 2f, 28f) - p;
                PlaceStanding(k, p, Vector3.Slerp(Facing(k), tangent, 1f - Mathf.Exp(-8f * dt)));
            }
        }

        static Vector3 Facing(Kid k) => (k.Model.rotation * Quaternion.Inverse(k.FacingFix)) * Vector3.forward;
        static Vector3 Pos(float angleDeg, float r) => new Vector3(Mathf.Cos(angleDeg * Mathf.Deg2Rad) * r, 0f, Mathf.Sin(angleDeg * Mathf.Deg2Rad) * r);

        void UpdateDoor()
        {
            if (_door == null) return;
            _doorAngle = Mathf.Lerp(_doorAngle, _doorTarget, 1f - Mathf.Exp(-5f * Time.deltaTime));
            _door.localRotation = Quaternion.Euler(0f, -_doorAngle, 0f);
        }

        Vector3 _bonkedHead;

        void UpdateStars()
        {
            if (_starRing == null || !_starRing.gameObject.activeSelf) return;
            var cam = Camera.main;
            Vector3 center = _bonkedHead + Vector3.up * 1.6f;
            float radius = 3.2f;
            bool mine = PlayerAvatars.Instance != null && PlayerAvatars.Instance.IsLocal(_bonked);
            if (mine && cam != null)
            {
                // your own stars spin in front of your eyes
                center = cam.transform.position + cam.transform.forward * 7f + cam.transform.up * 1.6f;
                radius = 2.6f;
            }
            float t = Time.time * 2.4f;
            for (int i = 0; i < _stars.Count; i++)
            {
                float a = t + i * Mathf.PI * 2f / _stars.Count;
                var p = center + new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a * 2f) * 0.35f, Mathf.Sin(a) * radius);
                if (mine && cam != null) p = center + cam.transform.right * Mathf.Cos(a) * radius + cam.transform.up * Mathf.Sin(a) * radius * 0.35f + cam.transform.forward * Mathf.Sin(a) * 0.8f;
                _stars[i].position = p;
                if (cam != null) _stars[i].rotation = Quaternion.LookRotation(p - cam.transform.position) * Quaternion.Euler(0f, 0f, t * 90f + i * 40f);
            }
        }

        // ------------------------------------------------------------------ the squeaky hammer
        void Step(Vector3 at)
        {
            var steps = SecretJengaAssets.Instance.steps;
            if (steps == null || steps.Length == 0) return;
            var c = steps[Random.Range(0, steps.Length)];
            if (c != null) AudioManager.Instance?.PlayAt(c, at, 0.55f, Random.Range(0.92f, 1.08f));
        }

        void Sfx(AudioClip c, Vector3 at, float vol, float pitch = 1f)
        {
            if (c != null) AudioManager.Instance?.PlayAt(c, at, vol, pitch);
        }

        // ------------------------------------------------------------------ the big kid's arm (after the animation)
        void LateUpdate()
        {
            // the clips key every bone every frame, so the kid proportions go back on after each animation update
            foreach (var k in _kids) Proportions(k);
            if (_big == null || !_big.Model.gameObject.activeInHierarchy) return;
            Proportions(_big);
            if (_big.UpperR == null || _big.LowerR == null || _big.HandR == null) return;

            var fwd = Facing(_big);
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            // lean back to wind up, forward into the swing
            if (_big.Spine2 != null && Mathf.Abs(_bend) > 0.01f)
                _big.Spine2.rotation = Quaternion.AngleAxis(_bend, right) * _big.Spine2.rotation;

            var S = _big.UpperR.position;
            Vector3 grip, up;
            if (_carry)
            {
                // the hammer rests on the shoulder: fist in front of the chest, the head bobbing behind the shoulder
                grip = S + Vector3.down * 2.4f + fwd * 3.4f + right * 0.3f + Vector3.up * Mathf.Sin(Time.time * 9f) * 0.15f;
                up = (Vector3.up * 0.8f - fwd * 0.5f + right * 0.2f).normalized;
            }
            else
            {
                grip = _grip;
                up = _hammerUp;
            }
            var toGrip = grip - S;
            var wristTarget = grip - (toGrip.sqrMagnitude > 1e-4f ? toGrip.normalized : fwd) * 1.3f;
            var pole = S + Vector3.down * 6f + right * 4f - fwd * 2f;
            TwoBoneIK(_big.UpperR, _big.LowerR, _big.HandR, wristTarget, pole);

            // the fist closes around the handle just past the wrist; the handle runs along "up"
            var W = _big.HandR.position;
            var D = (W - _big.LowerR.position).normalized;
            var fist = W + D * 1.3f;
            // turn the hand so the knuckles face along the swing
            _big.HandR.rotation = Quaternion.FromToRotation(D, Vector3.Slerp(D, up, 0.35f)) * _big.HandR.rotation;
            var side = Vector3.ProjectOnPlane(right, up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(fwd, up);
            // the hammer head's axis lies in the swing plane, so one rubber face lands on the head
            _hammer.SetPositionAndRotation(fist - up * 2.2f, Quaternion.LookRotation(-side.normalized, up));
        }

        /// <summary>Classic two-bone IK: bend at the elbow towards the pole so the wrist reaches the target.</summary>
        static void TwoBoneIK(Transform upper, Transform lower, Transform end, Vector3 target, Vector3 pole)
        {
            var A = upper.position;
            float la = Vector3.Distance(A, lower.position), lb = Vector3.Distance(lower.position, end.position);
            if (la < 1e-4f || lb < 1e-4f) return;
            var toT = target - A;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 0.01f, la + lb - 0.01f);
            var dir = toT.sqrMagnitude > 1e-6f ? toT.normalized : Vector3.down;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
            var n = Vector3.Cross(dir, pole - A);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dir, Vector3.up);
            var elbowDir = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, n.normalized) * dir;
            upper.rotation = Quaternion.FromToRotation(lower.position - A, elbowDir) * upper.rotation;
            var B = lower.position;
            lower.rotation = Quaternion.FromToRotation(end.position - B, (A + dir * d) - B) * lower.rotation;
        }

        IEnumerator BonkScene(ulong loser)
        {
            HoldResults = true;
            _bonked = loser;
            var avatars = PlayerAvatars.Instance;
            var cc = CameraController.Instance;
            if (avatars == null || !avatars.TryGetHeadTop(loser, out var headTop, out int seat))
            {
                HoldResults = false;
                yield break;
            }
            bool mine = avatars.IsLocal(loser);
            _bonkedHead = headTop;

            yield return new WaitForSeconds(1.0f);   // everyone sees the tower come down first

            // the door creaks open
            var a = SecretJengaAssets.Instance;
            Vector3 doorPos = _door != null ? _door.position + _door.right * 7f : new Vector3(-22f, F, -RoomHalf + 1f);
            Sfx(a.doorCreak, doorPos + Vector3.up * 14f, 0.9f);
            _doorTarget = 100f;
            yield return new WaitForSeconds(0.45f);

            // the big kid walks in, hammer on the shoulder
            var outward = SeatLayout.Outward(seat);
            float seatAngle = SeatLayout.AngleDeg(seat);
            var start = new Vector3(doorPos.x, 0f, -RoomHalf + 3f);
            var inside = new Vector3(doorPos.x, 0f, -RoomHalf + 15f);
            float enterAngle = Mathf.Atan2(inside.z, inside.x) * Mathf.Rad2Deg;
            var path = new List<Vector3> { start, inside };
            float delta = Mathf.DeltaAngle(enterAngle, seatAngle);
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(delta) / 10f));
            for (int i = 0; i <= n; i++) path.Add(Pos(enterAngle + delta * i / n, WalkRadius));
            var stand = outward * StandRadius;
            path.Add(stand);

            _carry = true;
            _bend = 0f;
            _big.Model.gameObject.SetActive(true);
            _hammer.gameObject.SetActive(true);
            Play(_big, "Walk_Loop", 0f, 1f);
            PlaceStanding(_big, start, Vector3.forward);
            _stepTimer = 0f;
            yield return WalkPath(path, cc, mine);

            // behind the chair: turn to the loser, wind up (lean back, hammer high over the head), a beat... BONK
            Play(_big, "Idle_Loop", 0.25f, 1f);
            var inward = -outward;
            var right = Vector3.Cross(Vector3.up, inward).normalized;
            const float turn = 0.35f, raise = 0.5f, hold = 0.4f, swing = 0.17f;
            float e = 0f;
            bool whooshed = false;
            Vector3 raisedUp = (-inward * 0.75f + Vector3.up * 0.65f).normalized;
            Vector3 hitUp = (inward - Vector3.up * 0.2f).normalized;
            while (e < turn + raise + hold + swing)
            {
                e += Time.deltaTime;
                PlaceStanding(_big, stand, Vector3.Slerp(Facing(_big), inward, 1f - Mathf.Exp(-10f * Time.deltaTime)));
                if (avatars.TryGetHeadTop(loser, out var ht, out _)) headTop = ht;
                _bonkedHead = headTop;
                var S = _big.UpperR != null ? _big.UpperR.position : stand + Vector3.up * 24f;
                var carryGrip = S + Vector3.down * 2.4f + inward * 3.4f + right * 0.3f;
                var carryUp = (Vector3.up * 0.8f - inward * 0.5f + right * 0.2f).normalized;
                var raisedGrip = S + Vector3.up * 6.8f - inward * 1.2f + right * 0.8f;
                var hitGrip = headTop + Vector3.up * 2.0f - hitUp * 7.6f;
                _carry = false;
                if (e < turn)
                {
                    _grip = carryGrip;
                    _hammerUp = carryUp;
                }
                else if (e < turn + raise + hold)
                {
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e - turn) / raise));
                    _grip = S + Vector3.Slerp(carryGrip - S, raisedGrip - S, k);
                    _hammerUp = Vector3.Slerp(carryUp, raisedUp, k).normalized;
                    _bend = -14f * k + Mathf.Sin(Mathf.Clamp01((e - turn - raise) / hold) * Mathf.PI) * -3f;
                }
                else
                {
                    float k = Mathf.Clamp01((e - turn - raise - hold) / swing);
                    k = k * k * (3f - 2f * k) * 0.3f + k * k * 0.7f;   // accelerating
                    _grip = S + Vector3.Slerp(raisedGrip - S, hitGrip - S, k);
                    _hammerUp = Vector3.Slerp(raisedUp, hitUp, k).normalized;
                    _bend = Mathf.Lerp(-14f, 20f, k);
                    if (!whooshed && k > 0.15f) { whooshed = true; Sfx(a.whoosh, S, 0.7f); }
                }
                LookAt(cc, mine, headTop, 0.85f);
                yield return null;
            }

            // BONK
            Sfx(a.bonk, headTop, 1f);
            Sfx(a.squeak, headTop, 0.9f);
            if (cc != null) cc.Shake(mine ? 0.5f : 0.18f, mine ? 0.5f : 0.3f);
            ShowBonk(headTop, mine);
            if (_birdies != null && a.birdiesLoop != null)
            {
                _birdies.clip = a.birdiesLoop;
                _birdies.volume = 0.45f * GameSettings.SfxVolume;
                _birdies.transform.position = headTop;
                _birdies.Play();
            }
            _starRing.gameObject.SetActive(true);
            foreach (var k in _kids)
            {
                k.CheerUntil = Time.time + Random.Range(1.8f, 2.6f);
                if (!k.Clip.StartsWith("Sitting") && !k.Clip.StartsWith("GroundSit")) Play(k, "Celebration", 0.2f, Random.Range(0.95f, 1.1f));
            }

            e = 0f;
            var hitPose = _grip;
            bool cheering = false;
            while (e < 2.4f)
            {
                e += Time.deltaTime;
                float ko = Mathf.Clamp01(e / 0.28f);
                avatars.SetKnockedOut(loser, ko);
                if (mine && cc != null) cc.KO = ko;
                if (avatars.TryGetHeadTop(loser, out var ht, out _)) _bonkedHead = ht;
                var S = _big.UpperR != null ? _big.UpperR.position : stand + Vector3.up * 24f;
                if (e < 0.15f)
                {
                    // the head of the hammer squashes on the head for a moment
                    _grip = hitPose;
                }
                else if (e < 0.75f)
                {
                    // bounce back up and straighten
                    float b = Mathf.SmoothStep(0f, 1f, (e - 0.15f) / 0.6f);
                    _grip = S + Vector3.Slerp(hitPose - S, Vector3.up * 5.5f + inward * 2.5f + right * 1.2f, b);
                    _hammerUp = Vector3.Slerp(hitUp, Vector3.up, b).normalized;
                    _bend = Mathf.Lerp(20f, 0f, b);
                }
                else
                {
                    // cheer: the hammer pumped up in the air
                    if (!cheering) { cheering = true; Play(_big, "Celebration", 0.2f, 1f); }
                    float pump = Mathf.Abs(Mathf.Sin((e - 0.75f) * 6.5f));
                    _grip = S + Vector3.up * (5.5f + pump * 1.6f) + inward * 1.5f + right * 1.4f;
                    _hammerUp = (Vector3.up + right * 0.25f).normalized;
                    _bend = 0f;
                }
                LookAt(cc, mine, _bonkedHead, Mathf.Lerp(0.8f, 0f, Mathf.Clamp01((e - 1.4f) / 0.9f)));
                yield return null;
            }
            if (cc != null) cc.CineWeight = 0f;
            HoldResults = false;   // the results can come in now

            // the big kid walks back out and shuts the door
            _carry = true;
            Play(_big, "Walk_Loop", 0.25f, 1f);
            path.Reverse();
            yield return WalkPath(path, null, false);
            _big.Model.gameObject.SetActive(false);
            _hammer.gameObject.SetActive(false);
            _doorTarget = 0f;
            Sfx(a.doorCreak, doorPos + Vector3.up * 14f, 0.6f, 1.15f);
            _scene = null;
        }

        /// <summary>Walks the big kid along the path (smooth turns, steps in time with the walk cycle).</summary>
        IEnumerator WalkPath(List<Vector3> path, CameraController cc, bool mine)
        {
            const float speed = 22f;
            for (int i = 1; i < path.Count; i++)
            {
                var from = path[i - 1];
                var to = path[i];
                float len = Vector3.Distance(from, to);
                float t = 0f;
                while (t < len)
                {
                    t += speed * Time.deltaTime;
                    var p = Vector3.Lerp(from, to, Mathf.Clamp01(t / Mathf.Max(0.01f, len)));
                    // look a little ahead so the turns round off
                    var ahead = i + 1 < path.Count ? Vector3.Lerp(to - from, path[i + 1] - to, Mathf.Clamp01(t / Mathf.Max(0.01f, len)) * 0.5f) : to - from;
                    PlaceStanding(_big, p, Vector3.Slerp(Facing(_big), ahead, 1f - Mathf.Exp(-7f * Time.deltaTime)));
                    _stepTimer -= Time.deltaTime;
                    if (_stepTimer <= 0f) { _stepTimer = 0.42f; Step(p); }
                    if (cc != null) LookAt(cc, mine, _big.Head.position, 0.55f);
                    yield return null;
                }
            }
        }

        /// <summary>Turn the other players' cameras towards the scene (not the loser's: they cannot look behind them).</summary>
        static void LookAt(CameraController cc, bool mine, Vector3 at, float weight)
        {
            if (cc == null) return;
            cc.CineFocus = at;
            cc.CineWeight = mine ? 0f : Mathf.Lerp(cc.CineWeight, weight, 1f - Mathf.Exp(-3f * Time.deltaTime));
        }

        void ShowBonk(Vector3 at, bool mine)
        {
            if (_bonkText == null) return;
            var rt = (RectTransform)_bonkText.transform;
            var cam = Camera.main;
            Vector2 pos = Vector2.zero;
            if (!mine && cam != null)
            {
                var sp = cam.WorldToScreenPoint(at + Vector3.up * 4f);
                if (sp.z > 0f)
                {
                    var canvasRt = (RectTransform)_overlay.transform;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, sp, null, out pos);
                }
            }
            rt.anchoredPosition = pos + new Vector2(0f, mine ? 120f : 40f);
            rt.localRotation = Quaternion.Euler(0f, 0f, Random.Range(-12f, 12f));
            _bonkText.fontSize = mine ? 190 : 120;
            StartCoroutine(Pop(rt, _bonkGroup));
            if (mine) StartCoroutine(Flash());
        }

        IEnumerator Pop(RectTransform rt, CanvasGroup g)
        {
            float e = 0f;
            while (e < 1.3f)
            {
                e += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(e / 0.18f);
                rt.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, UI.UITween.EaseOutBack(k));
                g.alpha = e < 1f ? 1f : 1f - (e - 1f) / 0.3f;
                yield return null;
            }
            g.alpha = 0f;
        }

        IEnumerator Flash()
        {
            float e = 0f;
            while (e < 0.5f)
            {
                e += Time.unscaledDeltaTime;
                _flashGroup.alpha = 0.85f * (1f - e / 0.5f);
                yield return null;
            }
            _flashGroup.alpha = 0f;
        }
    }
}
