using SecretJenga.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Local mouse interaction (Input System). Picks/hover-highlights blocks, sends grab / drag / release / place
    /// requests to the host and renders the placement ghosts and the other players' cursor marker.
    /// It never moves blocks itself: the host simulates, everyone sees the replicated result.
    /// </summary>
    public class BlockInteractionController : MonoBehaviour
    {
        public static BlockInteractionController Instance { get; private set; }

        public Camera cam;
        public float sendRate = 30f;

        public string Hint { get; private set; } = "";

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
            _cursor.AddComponent<MeshFilter>().sharedMesh = ProceduralMeshes.UVSphere(0.09f, 10, 16, "SJ_Cursor");
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
                return;
            }

            var turns = game.Turns;
            var stage = turns.Stage.Value;
            bool myTurn = turns.IsLocalPlayersTurn;
            UpdateGhosts(turns, stage, myTurn);
            UpdateRemoteCursor(game, turns, stage, myTurn);

            if (!myTurn)
            {
                SetHover(null);
                Hint = stage == TurnStage.Settling ? "Waiting for the tower to settle..." : $"{game.PlayerName(turns.ActiveClientId.Value)} is playing...";
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null || UIManager.BlocksGameInput) { SetHover(null); return; }

            Vector2 mp = mouse.position.ReadValue();
            Ray ray = cam.ScreenPointToRay(mp);
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            _sendTimer -= Time.deltaTime;

            switch (stage)
            {
                case TurnStage.WaitingForGrab:
                {
                    _releaseSent = false;
                    Hint = "Your turn! Click a block and drag along it to push or pull it out.";
                    JengaBlock hit = null;
                    RaycastHit info = default;
                    if (!overUI && Physics.Raycast(ray, out info, 200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        hit = info.collider.GetComponentInParent<JengaBlock>();
                    SetHover(hit, game.Tower);
                    if (hit != null && mouse.leftButton.wasPressedThisFrame)
                    {
                        if (!game.Tower.IsExtractableForUI(hit))
                        {
                            UIManager.Instance?.Toast("You can't take blocks from the top layers.", Palette.Bad);
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
                    Hint = "Slide the block out along its length. Release the mouse to let go.";
                    if (mouse.leftButton.isPressed)
                    {
                        if (_sendTimer <= 0f && _dragPlane.Raycast(ray, out float d))
                        {
                            _sendTimer = 1f / sendRate;
                            turns.DragInputServerRpc(ray.GetPoint(d));
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
                    Hint = "Block is out! Move it over the tower and click a glowing slot to place it.";
                    var center = turns.PlaceCenter.Value;
                    var carryPlane = new Plane(Vector3.up, new Vector3(0, center.y + JengaDims.BlockHeight * 2.5f, 0));
                    if (carryPlane.Raycast(ray, out float d))
                    {
                        var p = ray.GetPoint(d);
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
                    Hint = "Nice! Waiting for the tower to settle...";
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

        void SetHover(JengaBlock b, JengaTower tower = null)
        {
            if (_hovered != null && _hovered != b) _hovered.SetHover(JengaBlock.HoverMode.None);
            _hovered = b;
            if (b != null) b.SetHover(tower != null && tower.IsExtractableForUI(b) ? JengaBlock.HoverMode.Valid : JengaBlock.HoverMode.Invalid);
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
                var c = hot ? new Color(0.55f, 1f, 0.6f, 0.45f + 0.2f * pulse) : new Color(1f, 0.88f, 0.6f, 0.18f + 0.08f * pulse);
                _ghostRenderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor("_BaseColor", c);
                _ghostRenderers[i].SetPropertyBlock(_mpb);
            }
        }

        void UpdateRemoteCursor(NetworkGameManager game, TurnManager turns, TurnStage stage, bool myTurn)
        {
            bool show = !myTurn && (stage == TurnStage.Dragging || stage == TurnStage.Carrying);
            _cursor.SetActive(show);
            if (!show) return;
            _cursor.transform.position = Vector3.Lerp(_cursor.transform.position, turns.CursorPoint.Value, 1f - Mathf.Exp(-20f * Time.deltaTime));
            _cursorRenderer.GetPropertyBlock(_mpb);
            var c = game.PlayerColor(turns.ActiveClientId.Value);
            _mpb.SetColor("_BaseColor", c);
            _mpb.SetColor("_EmissionColor", c * 1.5f);
            _cursorRenderer.SetPropertyBlock(_mpb);
        }
    }
}
