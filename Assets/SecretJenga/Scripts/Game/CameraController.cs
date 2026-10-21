using SecretJenga.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SecretJenga.Game
{
    /// <summary>
    /// Orbit camera around the tower. Right mouse drag / A-D / arrow keys rotate, mouse wheel zooms.
    /// Follows the tower height; slowly auto-orbits in menus. Supports camera shake.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        public static CameraController Instance { get; private set; }

        public float yaw = 35f;
        public float pitch = 22f;
        public float distance = 10.5f;
        public float minDistance = 5f;
        public float maxDistance = 20f;
        public float minPitch = 2f;
        public float maxPitch = 75f;

        Vector3 _focus;
        float _shakeTime, _shakeAmount, _targetDistance;

        void Awake()
        {
            Instance = this;
            _targetDistance = distance;
            _focus = new Vector3(0, 1.5f, 0);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void Shake(float amount, float duration)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
            _shakeTime = Mathf.Max(_shakeTime, duration);
        }

        void LateUpdate()
        {
            var game = NetworkGameManager.Instance;
            bool inMatch = game != null && game.IsSpawned && game.Phase.Value != GamePhase.Lobby;
            float dt = Time.unscaledDeltaTime;
            float sens = GameSettings.MouseSensitivity;

            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool inputAllowed = inMatch && !UIManager.BlocksGameInput;

            if (inputAllowed && mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    var d = mouse.delta.ReadValue();
                    yaw += d.x * 0.22f * sens;
                    pitch -= d.y * 0.18f * sens;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f) _targetDistance -= Mathf.Sign(scroll) * 0.8f;
            }
            if (inputAllowed && kb != null)
            {
                float k = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) k += 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) k -= 1f;
                yaw += k * 70f * dt;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) pitch += 40f * dt;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) pitch -= 40f * dt;
            }
            if (!inMatch) yaw += 6f * dt; // menu showcase orbit

            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
            distance = Mathf.Lerp(distance, _targetDistance, 1f - Mathf.Exp(-8f * dt));

            float towerTop = JengaDims.TableTopY + 10 * JengaDims.BlockHeight;
            if (game != null && game.IsSpawned && game.Tower != null && game.Tower.LayerCount.Value > 0)
                towerTop = JengaDims.TableTopY + game.Tower.LayerCount.Value * JengaDims.BlockHeight;
            float focusY = Mathf.Max(1.2f, towerTop * 0.6f);
            if (game != null && game.IsSpawned && game.Turns != null && game.Turns.Stage.Value == TurnStage.Carrying)
                focusY = Mathf.Max(focusY, towerTop * 0.85f);
            var desiredFocus = new Vector3(0, focusY, 0);
            _focus = Vector3.Lerp(_focus, desiredFocus, 1f - Mathf.Exp(-3f * dt));

            var rot = Quaternion.Euler(pitch, yaw, 0);
            var pos = _focus + rot * new Vector3(0, 0, -distance);
            if (pos.y < JengaDims.TableTopY + 0.3f) pos.y = JengaDims.TableTopY + 0.3f;

            if (_shakeTime > 0f)
            {
                _shakeTime -= dt;
                pos += Random.insideUnitSphere * _shakeAmount * Mathf.Clamp01(_shakeTime * 2f);
                if (_shakeTime <= 0f) _shakeAmount = 0f;
            }

            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(_focus - pos, Vector3.up));
        }
    }
}
