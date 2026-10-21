using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// Server-side tower monitor. Every physics step it measures how much the tower is moving (wobble) and detects
    /// fallen blocks (lying on the table, flung away or tipped over). A fallen block ends the match as a team loss.
    /// The block currently being manipulated (or the one touched this turn) is excluded from the collapse test.
    /// </summary>
    public class TowerStabilityChecker : NetworkBehaviour
    {
        /// <summary>0..1 smoothed wobble, replicated ~10x per second for the HUD stability meter.</summary>
        public readonly NetworkVariable<float> Wobble = new NetworkVariable<float>();

        public const float WobbleSpeedForMax = 0.8f;

        /// <summary>Server: unsmoothed wobble of the current physics step (0..1).</summary>
        public float RawWobble { get; private set; }
        /// <summary>Server: highest linear speed among tower blocks this step.</summary>
        public float MaxSpeed { get; private set; }

        NetworkGameManager _game;
        float _smoothed, _syncTimer, _collapseTimer;
        bool _collapseReported;

        void Awake() => _game = GetComponent<NetworkGameManager>();

        public void ServerReset()
        {
            _collapseTimer = 0;
            _collapseReported = false;
            _smoothed = 0;
            RawWobble = 0;
            MaxSpeed = 0;
            if (IsServer) Wobble.Value = 0;
        }

        void FixedUpdate()
        {
            if (!IsSpawned || !IsServer || _game == null) return;
            var phase = _game.Phase.Value;
            if (phase != GamePhase.Playing && phase != GamePhase.Countdown)
            {
                if (_collapseReported && phase == GamePhase.Lobby) ServerReset();
                return;
            }
            if (phase == GamePhase.Countdown) { _collapseReported = false; _collapseTimer = 0; }

            var excluded = _game.Turns.ExcludedBlock;
            float maxSpeed = 0f;
            int fallen = 0;
            Vector3 fallenAt = Vector3.zero;

            foreach (var b in JengaBlock.All.Values)
            {
                if (b == null || b == excluded || b.Hold.Value != JengaBlock.HoldMode.None) continue;
                var rb = b.Rb;
                float s = rb.isKinematic ? 0f : rb.linearVelocity.magnitude + rb.angularVelocity.magnitude * 0.25f;
                if (s > maxSpeed) maxSpeed = s;

                if (IsFallen(b))
                {
                    fallen++;
                    fallenAt = b.transform.position;
                }
            }

            MaxSpeed = maxSpeed;
            RawWobble = Mathf.Clamp01(maxSpeed / WobbleSpeedForMax);
            _smoothed = Mathf.Max(RawWobble, Mathf.Lerp(_smoothed, RawWobble, 1f - Mathf.Exp(-4f * Time.fixedDeltaTime)));
            _syncTimer -= Time.fixedDeltaTime;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 0.1f;
                if (Mathf.Abs(Wobble.Value - _smoothed) > 0.01f) Wobble.Value = _smoothed;
            }

            if (phase != GamePhase.Playing || _collapseReported) return;

            if (fallen > 0) _collapseTimer += Time.fixedDeltaTime;
            else _collapseTimer = 0f;

            if (_collapseTimer >= 0.25f)
            {
                _collapseReported = true;
                string who = _game.PlayerName(_game.Turns.ActiveClientId.Value);
                CollapseClientRpc(fallenAt, fallen);
                _game.ServerEndGame(false, $"The tower collapsed during {who}'s turn!");
            }
        }

        public static bool IsFallen(JengaBlock b)
        {
            var p = b.transform.position;
            var flat = new Vector3(p.x - JengaTower.BaseCenter.x, 0f, p.z - JengaTower.BaseCenter.z);
            if (flat.magnitude > 2.2f) return true;                                                    // flung away from the tower
            if (b.Layer.Value > 0 && p.y < JengaDims.TableTopY + JengaDims.BlockHeight * 0.8f) return true; // lying on the table
            if (Vector3.Angle(b.transform.up, Vector3.up) > 55f) return true;                          // tipped over
            return false;
        }

        [ClientRpc]
        void CollapseClientRpc(Vector3 at, int count)
        {
            AudioManager.Instance?.PlayCollapse();
            EffectsManager.Instance?.Collapse(at, count);
        }
    }
}
