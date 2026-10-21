using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Atmosphere events that must look/sound the same for everyone at the table.</summary>
    public enum AtmosphereEvent : byte { Flicker = 0, DistantMetal = 1, DeepGroan = 2 }

    /// <summary>
    /// Host-side tension model. Tension (0..1) rises with turns played, tower height and wobble and is replicated
    /// at a low rate. The host also schedules sparse atmosphere events (lamp flicker bursts, distant metallic noises)
    /// and broadcasts them with a seed, so every client plays the identical effect at the same moment.
    /// The local AtmosphereDirector turns these into light, sound and post-processing.
    /// </summary>
    public class AtmosphereSync : NetworkBehaviour
    {
        public readonly NetworkVariable<float> Tension = new NetworkVariable<float>();

        [Tooltip("Events per minute at zero / full tension.")]
        public Vector2 flickerPerMinute = new Vector2(0.6f, 7f);
        public Vector2 metalPerMinute = new Vector2(0.4f, 2.2f);
        [Tooltip("Minimum seconds between two events of any kind (keeps the room quiet enough to talk).")]
        public float minGap = 6f;

        NetworkGameManager _game;
        float _smoothed, _syncTimer, _gap;

        void Awake() => _game = GetComponent<NetworkGameManager>();

        public void ServerReset()
        {
            _smoothed = 0f;
            _gap = minGap;
            if (IsServer) Tension.Value = 0f;
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || _game == null) return;
            var phase = _game.Phase.Value;
            float target = 0.08f;
            if (phase == GamePhase.Playing || phase == GamePhase.Countdown)
            {
                // no turn limit any more: tension creeps up with every turn survived and every layer added
                float progress = 1f - Mathf.Exp(-_game.Turns.MovesCompleted.Value / 14f);
                float height = Mathf.Clamp01((_game.Tower.LayerCount.Value - _game.StartLayers.Value) / 8f);
                float wobble = _game.Stability.Wobble.Value;
                bool handling = _game.Turns.Stage.Value == TurnStage.Dragging || _game.Turns.Stage.Value == TurnStage.Carrying;
                target = Mathf.Clamp01(0.12f + 0.42f * progress + 0.18f * height + 0.55f * wobble + (handling ? 0.06f : 0f));
            }
            else if (phase == GamePhase.GameOver) target = 0.2f;

            float dt = Time.deltaTime;
            _smoothed = Mathf.MoveTowards(_smoothed, target, dt * (target > _smoothed ? 0.6f : 0.15f));
            _syncTimer -= dt;
            if (_syncTimer <= 0f)
            {
                _syncTimer = 0.2f;
                if (Mathf.Abs(Tension.Value - _smoothed) > 0.01f) Tension.Value = _smoothed;
            }

            if (phase != GamePhase.Playing) return;
            _gap -= dt;
            if (_gap > 0f) return;
            float pFlicker = Mathf.Lerp(flickerPerMinute.x, flickerPerMinute.y, _smoothed) / 60f * dt;
            float pMetal = Mathf.Lerp(metalPerMinute.x, metalPerMinute.y, _smoothed) / 60f * dt;
            float roll = Random.value;
            if (roll < pFlicker) Fire(AtmosphereEvent.Flicker, Mathf.Lerp(0.3f, 1f, _smoothed));
            else if (roll < pFlicker + pMetal) Fire(_smoothed > 0.6f && Random.value < 0.35f ? AtmosphereEvent.DeepGroan : AtmosphereEvent.DistantMetal, Mathf.Lerp(0.4f, 0.9f, _smoothed));
        }

        void Fire(AtmosphereEvent e, float strength)
        {
            _gap = minGap * Random.Range(0.8f, 1.6f);
            EventClientRpc(e, Random.Range(0, int.MaxValue), strength);
        }

        [ClientRpc]
        void EventClientRpc(AtmosphereEvent e, int seed, float strength)
        {
            AtmosphereDirector.Instance?.PlayEvent(e, seed, strength);
        }
    }
}
