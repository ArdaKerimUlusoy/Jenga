using SecretJenga.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Seated first-person camera. The player never leaves their chair: the head is anchored at the seat's eye
    /// position and can only look around the tabletop and lean a little.
    ///   Mouse                 look around directly (cursor locked, the hand aims at the screen centre).
    ///                         While you pull or carry a block the view holds still and the mouse moves the hand.
    ///   hold Left Alt         free the cursor (to click the HUD)
    ///   A / D  (or Q / E)     lean left / right around the table edge (limited)
    ///   W / S  or mouse wheel lean in towards the tower / sit back
    ///   Middle mouse          re-center the view
    /// The default view always frames the tower and follows its height: as the tower grows you sit up straighter,
    /// and while YOU carry a block you rise out of the chair and look down on the top, so the free slots are clearly
    /// visible. Subtle breathing and a slight drift towards your hand; collapse shake is short and small.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        [Header("Look limits (degrees, relative to looking at the tower)")]
        public float maxYaw = 110f;
        public float minPitch = -38f;
        public float maxPitch = 40f;
        [Header("Lean")]
        [Tooltip("How far (degrees around the table) the head can lean sideways.")]
        public float maxLeanAngle = 24f;
        [Tooltip("How far (units) the head can lean in towards the tower.")]
        public float maxLeanIn = 4.5f;
        public float fieldOfView = 62f;

        Camera _cam;
        float _yaw, _pitch, _lean, _leanIn, _leanTarget, _leanInTarget, _shakeTime, _shakeAmount, _focusY = 2f, _rise, _towerTop, _turnLean;
        bool _locked;
        Vector3 _focusShift;
        int _seat;

        /// <summary>Extra pitch set by gestures (tilting the head back while drinking). Negative = look up.</summary>
        public float GesturePitch { get; set; }

        /// <summary>Cinematic look (the squeaky-hammer scene): blend the view towards this point, 0..1.</summary>
        public Vector3 CineFocus { get; set; }
        public float CineWeight { get; set; }
        /// <summary>0..1: you got bonked - the head drops forward onto the table and tilts.</summary>
        public float KO { get; set; }

        /// <summary>Seat the camera currently uses (local player's seat, or 0 in menus).</summary>
        public int Seat => _seat;

        void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            if (_cam != null) { _cam.fieldOfView = fieldOfView; _cam.nearClipPlane = 0.05f; }
        }

        void OnDestroy()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (Instance == this) Instance = null;
        }

        public void Shake(float amount, float duration)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
            _shakeTime = Mathf.Max(_shakeTime, duration);
        }

        public void Recenter()
        {
            _yaw = _pitch = 0f;
            _leanTarget = 0f;
            _leanInTarget = 0f;
        }

        void LateUpdate()
        {
            var game = NetworkGameManager.Instance;
            bool inRoom = game != null && game.IsSpawned;
            bool inRound = inRoom && game.Phase.Value != GamePhase.Lobby;
            float dt = Time.unscaledDeltaTime;
            float sens = GameSettings.MouseSensitivity;

            int seat = 0;
            if (inRoom && game.LocalPlayer.HasValue) seat = game.LocalPlayer.Value.SeatIndex;
            if (seat != _seat) { _seat = seat; Recenter(); }

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool inputAllowed = inRound && !UIManager.BlocksGameInput;

            // cursor: locked while playing (the mouse looks), free in menus, overlays or while Alt is held
            bool playingPhase = inRoom && (game.Phase.Value == GamePhase.Playing || game.Phase.Value == GamePhase.Countdown);
            bool altFree = kb != null && kb.leftAltKey.isPressed;
            bool wantLock = playingPhase && !UIManager.BlocksGameInput && !altFree && Application.isFocused;
            if (wantLock != _locked)
            {
                _locked = wantLock;
                Cursor.lockState = wantLock ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !wantLock;
            }

            if (inputAllowed && mouse != null)
            {
                var bic = BlockInteractionController.Instance;
                bool handHasMouse = bic != null && bic.Handling;   // pulling / carrying: the mouse moves the hand, not the head
                if (_locked && !handHasMouse)
                {
                    var d = mouse.delta.ReadValue();
                    _yaw += d.x * 0.075f * sens;
                    _pitch -= d.y * 0.065f * sens;
                }
                if (mouse.middleButton.wasPressedThisFrame) Recenter();
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _leanInTarget += Mathf.Sign(scroll) * 0.12f;
            }
            if (inputAllowed && kb != null)
            {
                float side = 0f;
                if (kb.aKey.isPressed || kb.qKey.isPressed || kb.leftArrowKey.isPressed) side -= 1f;
                if (kb.dKey.isPressed || kb.eKey.isPressed || kb.rightArrowKey.isPressed) side += 1f;
                _leanTarget += side * 1.6f * dt;
                if (Mathf.Abs(side) < 0.01f) _leanTarget = Mathf.MoveTowards(_leanTarget, 0f, 0.35f * dt); // settle back into the chair
                float inward = 0f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) inward += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) inward -= 1f;
                _leanInTarget += inward * 1.2f * dt;
            }
            if (!inRound)
            {
                // menus / lobby: a slow, uneasy drift of the head
                _leanTarget = Mathf.Sin(Time.time * 0.11f) * 0.35f;
                _leanInTarget = 0.15f + Mathf.Sin(Time.time * 0.07f) * 0.1f;
                _yaw = Mathf.Lerp(_yaw, 0f, dt);
                _pitch = Mathf.Lerp(_pitch, 0f, dt);
            }

            _yaw = Mathf.Clamp(_yaw, -maxYaw, maxYaw);
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            _leanTarget = Mathf.Clamp(_leanTarget, -1f, 1f);
            _leanInTarget = Mathf.Clamp01(_leanInTarget);
            _lean = Mathf.Lerp(_lean, _leanTarget, 1f - Mathf.Exp(-6f * dt));
            _leanIn = Mathf.Lerp(_leanIn, _leanInTarget, 1f - Mathf.Exp(-6f * dt));

            // tower height (smoothed) - used for the head height and for what we look at
            float towerTop = JengaDims.TableTopY + GameRules.DefaultStartLayers * JengaDims.BlockHeight;
            if (inRoom && game.Tower != null && game.Tower.LayerCount.Value > 0)
                towerTop = JengaDims.TableTopY + game.Tower.LayerCount.Value * JengaDims.BlockHeight;
            _towerTop = Mathf.Lerp(_towerTop <= 0f ? towerTop : _towerTop, towerTop, 1f - Mathf.Exp(-2f * dt));

            bool carryingMine = false, handlingMine = false;
            if (inRoom && game.Turns != null && game.Turns.IsLocalPlayersTurn)
            {
                var st0 = game.Turns.Stage.Value;
                carryingMine = st0 == TurnStage.Carrying;
                handlingMine = st0 == TurnStage.Dragging || carryingMine;
            }
            // rise out of the chair while placing on top (eased, so it never snaps)
            _rise = Mathf.Lerp(_rise, carryingMine ? 1f : 0f, 1f - Mathf.Exp(-3.2f * dt));
            // on your own turn you lean in over the table
            bool myTurnNow = inRoom && game.Turns != null && game.Turns.IsLocalPlayersTurn && game.Phase.Value == GamePhase.Playing;
            _turnLean = Mathf.Lerp(_turnLean, myTurnNow ? 1f : 0f, 1f - Mathf.Exp(-2.5f * dt));

            // head position: the seat's eye point, leaned around the table edge and towards the tower
            var eye = SeatLayout.EyePosition(_seat);
            // sit up as the tower grows: always keep the eyes a bit above the top of the tower
            eye.y = Mathf.Max(eye.y, _towerTop + 3.2f);
            // standing up to place: eyes well above the top, and a step closer
            eye.y = Mathf.Lerp(eye.y, Mathf.Max(eye.y, _towerTop + 7.5f), _rise);
            var leanRot = Quaternion.AngleAxis(-_lean * maxLeanAngle, Vector3.up);
            var pos = leanRot * new Vector3(eye.x, 0f, eye.z);
            var inward3 = -pos.normalized;
            pos += inward3 * (_leanIn * maxLeanIn + _rise * 1.8f + _turnLean * 2.4f);
            pos.y = eye.y - _leanIn * 1.2f;

            // breathing
            float breathe = Mathf.Sin(Time.time * 1.6f) * 0.03f;
            pos.y += breathe;

            // what we look at: the tower, following its height (the very top while a block is carried)
            float focusY = Mathf.Clamp(_towerTop * 0.55f, 1.2f, Mathf.Max(1.2f, _towerTop));
            Vector3 shiftTarget = Vector3.zero;
            if (inRoom && game.Turns != null)
            {
                var st = game.Turns.Stage.Value;
                if (st == TurnStage.Carrying)
                {
                    var pc = game.Turns.PlaceCenter.Value;
                    focusY = Mathf.Max(focusY, pc.y - 0.6f);
                    if (carryingMine) shiftTarget = new Vector3(pc.x, 0f, pc.z);
                }
                else if (handlingMine)
                {
                    var hp = game.Turns.CursorPoint.Value;
                    focusY = Mathf.Lerp(focusY, hp.y, 0.35f);
                    shiftTarget = new Vector3(hp.x, 0f, hp.z) * 0.18f; // drift slightly towards your hand
                }
            }
            _focusY = Mathf.Lerp(_focusY, focusY, 1f - Mathf.Exp(-2.5f * dt));
            _focusShift = Vector3.Lerp(_focusShift, shiftTarget, 1f - Mathf.Exp(-3f * dt));
            var focus = new Vector3(0f, _focusY, 0f) + _focusShift;

            var baseRot = Quaternion.LookRotation(focus - pos, Vector3.up);
            var e = baseRot.eulerAngles;
            var rot = Quaternion.Euler(e.x + _pitch + GesturePitch, e.y + _yaw, 0f);

            if (_shakeTime > 0f)
            {
                _shakeTime -= dt;
                float k = _shakeAmount * Mathf.Clamp01(_shakeTime * 2f);
                pos += Random.insideUnitSphere * k;
                rot *= Quaternion.Euler(Random.Range(-1f, 1f) * k * 6f, Random.Range(-1f, 1f) * k * 6f, 0f);
                if (_shakeTime <= 0f) _shakeAmount = 0f;
            }

            if (CineWeight > 0.001f && (CineFocus - pos).sqrMagnitude > 0.01f)
                rot = Quaternion.Slerp(rot, Quaternion.LookRotation(CineFocus - pos, Vector3.up), Mathf.Clamp01(CineWeight));
            if (KO > 0.001f)
            {
                // bonked: the head goes down onto the table, a little sideways
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(KO));
                pos += inward3 * (2.2f * k) - Vector3.up * (5.2f * k);
                rot = Quaternion.Slerp(rot, Quaternion.LookRotation(inward3 * 0.35f - Vector3.up, Vector3.up), k * 0.8f) * Quaternion.Euler(0f, 0f, 24f * k);
            }

            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
