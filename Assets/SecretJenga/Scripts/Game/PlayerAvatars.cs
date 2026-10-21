using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Seated figures and their hands. Every other player sits in their chair as a saloon patron (rigged CC0
    /// mannequin with a cowboy hat, animated with seated idle clips) and reaches over the table with two articulated
    /// hands (HandRig). If the character model is missing, a simple hooded body is used instead.
    /// The local player sees only their own two hands (first person, without the upper arm so it never hides the tower).
    ///
    /// Your right hand IS your cursor: it rests flat on the table and slides with the mouse inside your own reach,
    /// extends a finger to touch the block you aim at, flinches back from locked blocks and pinches the end of the
    /// block you pull. While carrying, the block is held by its near end from below-behind, so the top of the tower
    /// and the free slots stay visible. With rising tension the hands tremble and the fingers drum. F knocks.
    /// All of this is shared through HandSync (bots publish their hands from the host). Purely visual.
    ///
    /// Gestures (left hand, number keys): 1 OK sign, 2 thumbs up, 3 middle finger, 4 a sip of beer from the mug in
    /// front of you, 5 wave, 6 "rock on". Everyone at the table sees them; bots make them too.
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
            public Transform Model, BHead, BUpperL, BUpperR, BLowerL, BLowerR;
            public Animator Anim;
            public float ModelScale = 20f;
            public Vector3 PelvisOffset;     // pelvis position relative to the model root (scaled, model space)
            public Quaternion FacingFix = Quaternion.identity;
            public Quaternion HeadExtra = Quaternion.identity;
            // gestures
            public Gesture G;
            public float GStart;
            public Transform Mug;
        }

        static Mesh _mugGlass, _mugBeer, _mugFoam, _mugHandle;
        static Material _mugGlassMat, _mugBeerMat, _mugFoamMat;

        public static float GestureLength(Gesture g) => g == Gesture.Beer ? 3.6f : g == Gesture.Wave ? 2.3f : 2.0f;

        static readonly string[] SitStates = { "Sitting_Idle_Loop", "Sitting_Idle02_Loop", "Sitting_Idle03_Loop", "Sitting_Talking_Loop", "Sitting_Nodding_Loop" };
        static readonly Color[] Shirts = { new Color(0.78f, 0.72f, 0.6f), new Color(0.55f, 0.18f, 0.14f), new Color(0.32f, 0.38f, 0.5f), new Color(0.6f, 0.55f, 0.42f), new Color(0.3f, 0.36f, 0.26f) };
        static readonly Color[] Hats = { new Color(0.33f, 0.22f, 0.13f), new Color(0.12f, 0.1f, 0.09f), new Color(0.55f, 0.42f, 0.28f), new Color(0.24f, 0.17f, 0.12f) };
        static readonly Color[] Skins = { new Color(0.86f, 0.66f, 0.52f), new Color(0.72f, 0.52f, 0.38f), new Color(0.55f, 0.38f, 0.27f), new Color(0.92f, 0.74f, 0.62f) };

        /// <summary>Head-bone height of the seated character above the floor, in game units (1.18 m).</summary>
        const float SeatedHeadHeight = 23.5f;

        readonly Dictionary<ulong, Avatar> _avatars = new Dictionary<ulong, Avatar>();
        readonly List<ulong> _remove = new List<ulong>();
        Mesh _torso, _head;
        Material _skin, _cloth, _hood;

        void Awake()
        {
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
            HandSync.Knocked -= OnKnocked;
            HandSync.GestureMade -= OnGesture;
        }

        void OnGesture(ulong clientId, Gesture g)
        {
            if (!_avatars.TryGetValue(clientId, out var av)) return;
            av.G = g;
            av.GStart = Time.time;
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
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) g = Gesture.ThumbsUp;
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
                    av.LabelText.text = p.IsBot ? p.Name + " (bot)" : p.Name.ToString();
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
            if (!local)
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
            if (local)
            {
                av.Right.FirstPerson = true;
                av.Left.FirstPerson = true;
            }
            return av;
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
                _mugGlassMat = RuntimeArt.MakeLit(new Color(0.78f, 0.74f, 0.62f), 0.95f);
                _mugBeerMat = RuntimeArt.MakeLit(new Color(0.78f, 0.45f, 0.08f), 0.85f);
                _mugFoamMat = RuntimeArt.MakeLit(new Color(0.97f, 0.94f, 0.86f), 0.25f);
            }
            var mug = new GameObject("BeerMug").transform;
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
            Piece("Handle", _mugHandle, _mugGlassMat, new Vector3(0.95f, 1.6f, 0f), new Vector3(90f, 0f, 0f));
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
            av.BUpperR = FindDeep(model, "upperarm_r");
            av.BLowerL = FindDeep(model, "lowerarm_l");
            av.BLowerR = FindDeep(model, "lowerarm_r");
            var footL = FindDeep(model, "foot_l");
            var footR = FindDeep(model, "foot_r");
            if (av.BHead == null || pelvis == null || av.BUpperL == null || av.BUpperR == null) { Destroy(go); return false; }

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
            av.PelvisOffset = av.FacingFix * (pelvisL * av.ModelScale);
            av.PelvisOffset.y = 0f;
            av.Model = model;

            // outfit colours: neckerchief in the player's colour, the rest varied per player
            var shirtC = Shirts[h % Shirts.Length];
            var hatC = Hats[(h / 7) % Hats.Length];
            var skinC = Skins[(h / 13) % Skins.Length];
            skin = RuntimeArt.MakeLit(skinC, 0.3f);
            cloth = RuntimeArt.MakeLit(shirtC, 0.15f);
            var vest = RuntimeArt.MakeLit(Color.Lerp(new Color(0.18f, 0.12f, 0.08f), hatC, 0.3f), 0.35f);
            var pants = RuntimeArt.MakeLit(new Color(0.2f, 0.21f, 0.26f), 0.1f);
            var boots = RuntimeArt.MakeLit(new Color(0.16f, 0.09f, 0.05f), 0.55f);
            var hat = RuntimeArt.MakeLit(hatC, 0.2f);
            var band = RuntimeArt.MakeLit(hatC * 0.4f, 0.4f);
            var scarf = RuntimeArt.MakeLit(Palette.Player(colorIndex), 0.15f);
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    mats[i] = n.Contains("Skin") ? skin : n.Contains("Shirt") ? cloth : n.Contains("Vest") ? vest : n.Contains("Pants") ? pants :
                              n.Contains("Boots") ? boots : n.Contains("HatBand") ? band : n.Contains("Hat") ? hat : n.Contains("Neckerchief") ? scarf :
                              n.Contains("Joints") ? pants : cloth;
                }
                rend.sharedMaterials = mats;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (rend is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
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
                av.BUpperL.localScale = Vector3.one * 0.0001f;
                av.BUpperR.localScale = Vector3.one * 0.0001f;
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
                    else show = shoulderL + f * 4.2f + Vector3.up * 2.2f + rgt * 0.8f;

                    float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / 0.35f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - e) / 0.35f));
                    var palmToOthers = Quaternion.LookRotation(Vector3.up, -f);   // fingers up, palm towards the table
                    var backToOthers = Quaternion.LookRotation(Vector3.up, f);    // fingers up, back of the hand towards the table
                    var thumbUp = Quaternion.LookRotation(f, -rgt);               // left fist, thumb pointing up
                    Vector3 target = show;
                    Quaternion gOrient = palmToOthers;
                    switch (av.G)
                    {
                        case Gesture.Ok: leftPose = HandPoseId.Ok; break;
                        case Gesture.Horns:
                            leftPose = HandPoseId.Horns;
                            target += Vector3.up * Mathf.Abs(Mathf.Sin(e * 8f)) * 0.5f;
                            break;
                        case Gesture.ThumbsUp:
                            leftPose = HandPoseId.ThumbsUp;
                            gOrient = thumbUp;
                            target += Vector3.up * Mathf.Sin(Mathf.Clamp01(e / 0.6f) * Mathf.PI) * 0.4f;
                            break;
                        case Gesture.MiddleFinger:
                            leftPose = HandPoseId.MiddleFinger;
                            gOrient = backToOthers;
                            target += f * Mathf.Sin(Mathf.Clamp01((e - 0.3f) / 0.4f) * Mathf.PI) * 0.6f;   // a little jab
                            break;
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
                // turn the animated head towards what matters (the tower, the held block, a hovering hand)
                var bodyFwd = SeatLayout.Facing(av.Seat) * Vector3.forward;
                var want = (lookAt - av.BHead.position).normalized;
                var delta = Quaternion.FromToRotation(bodyFwd, want);
                delta = Quaternion.RotateTowards(Quaternion.identity, delta, 45f);
                av.HeadExtra = Quaternion.Slerp(av.HeadExtra, delta, 1f - Mathf.Exp(-3f * Time.deltaTime));
                av.BHead.rotation = av.HeadExtra * av.BHead.rotation;
                av.Label.position = av.BHead.position + Vector3.up * 8.5f;
            }
            else
            {
                var headRot = Quaternion.LookRotation((lookAt - headPos).normalized, Vector3.up);
                av.Head.SetPositionAndRotation(headPos, Quaternion.Slerp(av.Head.rotation, headRot, 1f - Mathf.Exp(-3f * Time.deltaTime)));
                av.Torso.localScale = new Vector3(1f, 1f + Mathf.Sin(Time.time * 1.4f + av.Seat) * 0.008f, 1f);
                av.Label.position = headPos + Vector3.up * 2.9f;
            }
            if (Camera.main != null) av.Label.rotation = Quaternion.LookRotation(av.Label.position - Camera.main.transform.position);
        }
    }
}
