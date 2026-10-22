using SecretJenga.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Local mouse interaction (Input System). Picks/hover-highlights blocks, sends grab / drag / release / place
    /// requests to the host and renders the placement ghosts and the active player's grip marker.
    /// It never moves blocks itself: the host simulates the physics (force-driven pull, see TurnManager), and
    /// everyone sees the replicated result.
    ///   LMB press on a block   grab it          LMB held + move   pull / push it along its length
    ///   LMB release            let go           once it is out    move over the tower, click a free slot to place
    /// </summary>
    public class BlockInteractionController : MonoBehaviour
    {
        public static BlockInteractionController Instance { get; private set; }

        public Camera cam;
        public float sendRate = 30f;

        public string Hint { get; private set; } = "";

        /// <summary>
        /// Where the hand aims, in screen pixels. The cursor is locked and the mouse turns the head, so normally this
        /// is the screen centre; while you pull or carry a block the head holds still and the mouse moves this point.
        /// Holding Left Alt uses the real mouse cursor.
        /// </summary>
        public Vector2 PointerScreen { get; private set; }
        /// <summary>True while the local player pulls or carries a block (the mouse drives the hand, not the view).</summary>
        public bool Handling { get; private set; }
        Vector2 _pointer;
        // While pulling / carrying, the hand target lives in WORLD space and only the mouse moves it. (Ray-casting a
        // fixed screen point every frame made the block crawl on its own whenever the camera drifted or leaned.)
        Vector3 _handWorld;
        TurnStage _handStage = TurnStage.None;

        public enum Prompt { None, Grab, Forbidden, Committed }
        /// <summary>Small prompt next to the cursor (HUD reads this).</summary>
        public Prompt CursorPrompt { get; private set; }
        /// <summary>Point on the block under the cursor (local player's hand reaches towards it).</summary>
        public bool HasHoverPoint { get; private set; }
        public Vector3 HoverPoint { get; private set; }
        public Vector3 HoverNormal { get; private set; } = Vector3.up;
        /// <summary>Where the mouse ray meets the table plane - the resting hand slides here.</summary>
        public bool HasTablePoint { get; private set; }
        public Vector3 TablePoint { get; private set; }

        JengaBlock _hovered;
        Plane _dragPlane;
        float _sendTimer;
        bool _releaseSent;
        int _hoverSlot = -1;
        readonly GameObject[] _ghosts = new GameObject[3];
        readonly MeshRenderer[] _ghostRenderers = new MeshRenderer[3];
        MaterialPropertyBlock _mpb;
        GameObject _cursor;
        MeshRenderer _cursorRenderer;

        void Awake()
        {
            Instance = this;
            _mpb = new MaterialPropertyBlock();
            if (cam == null) cam = Camera.main;
            BuildGhosts();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void BuildGhosts()
        {
            var assets = SecretJengaAssets.Instance;
            var mesh = assets.blockMesh != null ? assets.blockMesh : ProceduralMeshes.JengaBlock();
            var mat = assets.ghostMaterial != null ? assets.ghostMaterial : RuntimeArt.MakeGhost(new Color(1f, 0.9f, 0.6f, 0.35f));
            for (int i = 0; i < 3; i++)
            {
                var g = new GameObject("PlacementGhost_" + i);
                g.transform.SetParent(transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new[] { mat, mat };
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.SetActive(false);
                _ghosts[i] = g;
                _ghostRenderers[i] = mr;
            }

            _cursor = new GameObject("RemoteCursor");
            _cursor.transform.SetParent(transform, false);
            _cursor.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.UVSphere(0.06f, 10, 16, "SJ_Cursor");
            _cursorRenderer = _cursor.AddComponent<MeshRenderer>();
            _cursorRenderer.sharedMaterial = assets.cursorMaterial != null ? assets.cursorMaterial : RuntimeArt.MakeLit(Color.white, 0.8f);
            _cursorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cursor.SetActive(false);
        }

        void Update()
        {
            if (cam == null) cam = Camera.main;
            var game = NetworkGameManager.Instance;
            if (game == null || !game.IsSpawned || game.Phase.Value != GamePhase.Playing || cam == null)
            {
                SetHover(null);
                HideGhosts();
                _cursor.SetActive(false);
                Hint = "";
                CursorPrompt = Prompt.None;
                HasHoverPoint = false;
                HasTablePoint = false;
                Handling = false;
                PointerScreen = _pointer = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                return;
            }

            var turns = game.Turns;
            var stage = turns.Stage.Value;
            bool myTurn = turns.IsLocalPlayersTurn;

            // ---- the aiming point (see PointerScreen)
            {
                var md = Mouse.current;
                var kbd = Keyboard.current;
                var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                if (_pointer == Vector2.zero) _pointer = center;
                Handling = myTurn && (stage == TurnStage.Dragging || stage == TurnStage.Carrying);
                bool alt = kbd != null && kbd.leftAltKey.isPressed;
                if (Handling)
                {
                    var held = JengaBlock.Find(turns.HeldBlockId.Value);
                    if (_handStage != stage)
                    {
                        // start of a pull: the point you grabbed; start of a carry: above the lifted block
                        _handStage = stage;
                        if (stage == TurnStage.Dragging) _handWorld = turns.CursorPoint.Value.sqrMagnitude > 1e-4f ? turns.CursorPoint.Value : (held != null ? held.transform.position : Vector3.zero);
                        else if (held != null) _handWorld = held.transform.position;
                        if (stage == TurnStage.Carrying) _handWorld.y = turns.PlaceCenter.Value.y + JengaDims.BlockHeight * 2.5f;
                    }
                    if (md != null && !alt && !UIManager.BlocksGameInput)
                    {
                        // mouse delta -> movement on the table plane, relative to where you look (camera motion has no effect)
                        var d = md.delta.ReadValue();
                        var fwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                        var rgt = Vector3.Cross(Vector3.up, fwd).normalized;
                        float k = 0.011f * Mathf.Lerp(0.6f, 1.6f, Mathf.InverseLerp(0.3f, 2f, GameSettings.MouseSensitivity));
                        _handWorld += (rgt * d.x + fwd * d.y) * k;
                        var flat = Vector3.ClampMagnitude(new Vector3(_handWorld.x, 0f, _handWorld.z), 12f);
                        _handWorld = new Vector3(flat.x, _handWorld.y, flat.z);
                    }
                    else if (md != null && alt)
                    {
                        // Alt: steer with the real cursor on the same horizontal plane
                        var rr = cam.ScreenPointToRay(md.position.ReadValue());
                        var plane = new Plane(Vector3.up, new Vector3(0f, _handWorld.y, 0f));
                        if (plane.Raycast(rr, out float hd)) _handWorld = rr.GetPoint(hd);
                    }
                    var sp = cam.WorldToScreenPoint(_handWorld);
                    _pointer = sp.z > 0f ? new Vector2(sp.x, sp.y) : center;
                }
                else
                {
                    _handStage = TurnStage.None;
                    if (md != null && alt) _pointer = md.position.ReadValue();
                    else _pointer = Vector2.Lerp(_pointer, center, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
                }
                PointerScreen = _pointer;
            }
            UpdateGhosts(turns, stage, myTurn);
            UpdateRemoteCursor(game, turns, stage, myTurn);

            CursorPrompt = Prompt.None;
            HasHoverPoint = false;
            HasTablePoint = false;
            var m0 = Mouse.current;
            if (m0 != null && !UIManager.BlocksGameInput)
            {
                var r0 = cam.ScreenPointToRay(PointerScreen);
                var table = new Plane(Vector3.up, new Vector3(0f, JengaDims.TableTopY, 0f));
                if (table.Raycast(r0, out float td))
                {
                    var tp = r0.GetPoint(td);
                    var flat = Vector3.ClampMagnitude(new Vector3(tp.x, 0f, tp.z), SeatLayout.TableRadius - 0.6f);
                    TablePoint = new Vector3(flat.x, JengaDims.TableTopY, flat.z);
                    HasTablePoint = true;
                }
                var kb = Keyboard.current;
                if (kb != null && kb.fKey.wasPressedThisFrame && HasTablePoint && stage != TurnStage.Dragging && stage != TurnStage.Carrying)
                    HandSync.KnockLocal(TablePoint);
            }
            if (!myTurn)
            {
                SetHover(null);
                Hint = stage == TurnStage.Settling ? L.T("The tower is settling...", "Kule oturuyor...")
                    : L.T($"{game.PlayerName(turns.ActiveClientId.Value)} reaches for the tower...", $"{game.PlayerName(turns.ActiveClientId.Value)} kuleye uzanıyor...");
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || UIManager.BlocksGameInput) { SetHover(null); return; }

            Vector2 mp = PointerScreen;
            Ray ray = cam.ScreenPointToRay(mp);
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _sendTimer -= Time.deltaTime;

            switch (stage)
            {
                case TurnStage.WaitingForGrab:
                {
                    _releaseSent = false;
                    bool pressured = turns.TouchMoveActive.Value;
                    var committed = pressured ? JengaBlock.Find(turns.CommittedBlockId.Value) : null;
                    Hint = committed != null ? L.T("Pressure: you must finish pulling the block you touched.", "Baskı: dokunduğun bloğu çekip çıkarmak zorundasın.")
                        : pressured ? L.T("Your turn - under PRESSURE: the first block you grab is the one you must take.", "Sıra sende - BASKI altındasın: ilk tuttuğun bloğu almak zorundasın.")
                        : L.T("Your turn. Hold the left mouse button on a block and pull it out along its length.", "Sıra sende. Bir bloğa sol tıkla, basılı tut ve boyuna doğru çekip çıkar.");
                    JengaBlock hit = null;
                    RaycastHit info = default;
                    if (!overUI && Physics.Raycast(ray, out info, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        hit = info.collider.GetComponentInParent<JengaBlock>();
                    bool legal = hit != null && game.Tower.IsExtractableForUI(hit) && (committed == null || hit == committed);
                    SetHover(hit, legal);
                    if (hit != null)
                    {
                        HasHoverPoint = true;
                        HoverPoint = info.point;
                        HoverNormal = info.normal;
                        CursorPrompt = legal ? Prompt.Grab : (committed != null ? Prompt.Committed : Prompt.Forbidden);
                    }
                    if (hit != null && mouse.leftButton.wasPressedThisFrame)
                    {
                        if (!legal)
                        {
                            UIManager.Instance?.Toast(committed != null ? L.Resolve(L.Msg("pressure_touched")) : L.Resolve(L.Msg("not_top_layer")), Palette.Bad);
                            AudioManager.Instance?.Play(AudioManager.Sfx.Error);
                        }
                        else
                        {
                            _dragPlane = new Plane(Vector3.up, info.point);
                            turns.RequestGrabServerRpc(hit.NetworkObjectId, info.point);
                        }
                    }
                    break;
                }
                case TurnStage.Dragging:
                    SetHover(null);
                    Hint = turns.SteadyGripActive.Value ? L.T("Steady Grip: slow, straight pull. Release the mouse to let go.", "Sağlam Tutuş: yavaş ve düz çek. Bırakmak için fareyi bırak.")
                        : L.T("Pull slowly along the block's length. Release the mouse to let go.", "Bloğu boyuna doğru yavaşça çek. Bırakmak için fareyi bırak.");
                    if (mouse.leftButton.isPressed)
                    {
                        if (_sendTimer <= 0f)
                        {
                            _sendTimer = 1f / sendRate;
                            turns.DragInputServerRpc(_handWorld);
                        }
                    }
                    else if (!_releaseSent)
                    {
                        _releaseSent = true;
                        turns.ReleaseServerRpc();
                    }
                    break;
                case TurnStage.Carrying:
                {
                    SetHover(null);
                    Hint = L.T("The block is out. Move it over the tower and click a glowing slot on top to place it.", "Blok çıktı. Kulenin üstüne götür ve parlayan boş yere tıklayarak koy.");
                    {
                        var p = _handWorld;
                        if (_sendTimer <= 0f)
                        {
                            _sendTimer = 1f / sendRate;
                            turns.DragInputServerRpc(p);
                        }
                        _hoverSlot = NearestFreeSlot(turns, p);
                    }
                    if (mouse.leftButton.wasPressedThisFrame && !overUI && _hoverSlot >= 0)
                        turns.PlaceServerRpc(_hoverSlot);
                    break;
                }
                case TurnStage.Settling:
                    SetHover(null);
                    Hint = L.T("Placed. Waiting for the tower to settle...", "Kondu. Kulenin oturması bekleniyor...");
                    break;
            }
        }

        int NearestFreeSlot(TurnManager turns, Vector3 point)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if ((turns.FreeSlotMask.Value & (1 << i)) == 0) continue;
                var s = JengaTower.SlotPosition(turns.PlaceCenter.Value, turns.PlaceRotY.Value, i);
                float dd = new Vector2(s.x - point.x, s.z - point.z).sqrMagnitude;
                if (dd < bestD) { bestD = dd; best = i; }
            }
            return best;
        }

        void SetHover(JengaBlock b, bool legal = false)
        {
            if (_hovered != null && _hovered != b) _hovered.SetHover(JengaBlock.HoverMode.None);
            _hovered = b;
            if (b != null) b.SetHover(legal ? JengaBlock.HoverMode.Valid : JengaBlock.HoverMode.Invalid);
        }

        void HideGhosts()
        {
            foreach (var g in _ghosts) if (g != null && g.activeSelf) g.SetActive(false);
        }

        void UpdateGhosts(TurnManager turns, TurnStage stage, bool myTurn)
        {
            if (stage != TurnStage.Carrying) { HideGhosts(); _hoverSlot = -1; return; }
            var rot = Quaternion.Euler(0, turns.PlaceRotY.Value, 0);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            for (int i = 0; i < 3; i++)
            {
                bool free = (turns.FreeSlotMask.Value & (1 << i)) != 0;
                _ghosts[i].SetActive(free);
                if (!free) continue;
                _ghosts[i].transform.SetPositionAndRotation(JengaTower.SlotPosition(turns.PlaceCenter.Value, turns.PlaceRotY.Value, i), rot);
                bool hot = myTurn && i == _hoverSlot;
                // clearly visible from any seat: free slots glow softly, the one under your hand glows bright
                var c = hot ? new Color(1f, 0.82f, 0.45f, 0.62f + 0.18f * pulse) : new Color(0.95f, 0.85f, 0.62f, 0.26f + 0.12f * pulse);
                _ghostRenderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _ghostRenderers[i].SetPropertyBlock(_mpb);
            }
        }

        void UpdateRemoteCursor(NetworkGameManager game, TurnManager turns, TurnStage stage, bool myTurn)
        {
            bool show = false; // the other player's actual hand now shows the grip (PlayerAvatars/HandRig)
            _cursor.SetActive(show);
            if (!show) return;
            _cursor.transform.position = Vector3.Lerp(_cursor.transform.position, turns.CursorPoint.Value, 1f - Mathf.Exp(-20f * Time.deltaTime));
            _cursorRenderer.GetPropertyBlock(_mpb);
            var c = game.PlayerColor(turns.ActiveClientId.Value);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 0.6f);
            _cursorRenderer.SetPropertyBlock(_mpb);
        }
    }
}
