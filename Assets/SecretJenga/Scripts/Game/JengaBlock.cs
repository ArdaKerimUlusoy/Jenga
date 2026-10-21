using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>
    /// One networked Jenga block. Physics runs only on the host (NetworkRigidbody makes client copies kinematic,
    /// NetworkTransform replicates the host pose, rotation and interpolation), so every client sees exactly one
    /// authoritative tower state. Visual state (wood variant, hold highlight) is replicated with NetworkVariables;
    /// the hover highlight is purely local.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class JengaBlock : NetworkBehaviour
    {
        public static readonly Dictionary<ulong, JengaBlock> All = new Dictionary<ulong, JengaBlock>();

        public enum HoldMode : byte { None = 0, Held = 1, Carried = 2 }
        public enum HoverMode : byte { None = 0, Valid = 1, Invalid = 2 }

        public readonly NetworkVariable<byte> Wood = new NetworkVariable<byte>();
        public readonly NetworkVariable<int> Layer = new NetworkVariable<int>(-1);
        public readonly NetworkVariable<HoldMode> Hold = new NetworkVariable<HoldMode>(HoldMode.None);
        public readonly NetworkVariable<byte> HolderColor = new NetworkVariable<byte>();

        /// <summary>Server-only: slot index (0..2) inside the logical layer.</summary>
        public int Slot { get; set; } = -1;

        public Rigidbody Rb { get; private set; }
        public BoxCollider Col { get; private set; }
        MeshRenderer _renderer;
        MaterialPropertyBlock _mpb;
        HoverMode _hover;
        Vector3 _lastPos;
        float _lastSpeed, _hitCooldown;
        Vector2 _uvOffset;

        static PhysicsMaterial _normalMat, _slipperyMat;

        public static PhysicsMaterial NormalMaterial
        {
            get
            {
                if (_normalMat == null)
                    _normalMat = new PhysicsMaterial("SJ_Wood") { staticFriction = 0.55f, dynamicFriction = 0.42f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
                return _normalMat;
            }
        }

        /// <summary>Used on the block while it is being pushed/pulled so loaded blocks can still be extracted.</summary>
        public static PhysicsMaterial SlipperyMaterial
        {
            get
            {
                if (_slipperyMat == null)
                    _slipperyMat = new PhysicsMaterial("SJ_WoodHeld") { staticFriction = 0.16f, dynamicFriction = 0.12f, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
                return _slipperyMat;
            }
        }

        static readonly Dictionary<int, PhysicsMaterial> _heldCache = new Dictionary<int, PhysicsMaterial>();

        /// <summary>Held-block material with a custom friction (used by the Steady Grip card). Cached per value.</summary>
        public static PhysicsMaterial HeldMaterial(float friction)
        {
            int key = Mathf.RoundToInt(friction * 1000f);
            if (!_heldCache.TryGetValue(key, out var m) || m == null)
            {
                m = new PhysicsMaterial("TLB_WoodHeld_" + key) { staticFriction = friction * 1.3f, dynamicFriction = friction, bounciness = 0f, frictionCombine = PhysicsMaterialCombine.Minimum, bounceCombine = PhysicsMaterialCombine.Minimum };
                _heldCache[key] = m;
            }
            return m;
        }

        /// <summary>Horizontal long axis of the block (local X).</summary>
        public Vector3 LongAxis
        {
            get
            {
                var r = transform.right;
                r.y = 0;
                return r.sqrMagnitude < 1e-4f ? Vector3.right : r.normalized;
            }
        }

        void Awake()
        {
            Rb = GetComponent<Rigidbody>();
            Col = GetComponent<BoxCollider>();
            _renderer = GetComponentInChildren<MeshRenderer>();
            _mpb = new MaterialPropertyBlock();
        }

        public override void OnNetworkSpawn()
        {
            All[NetworkObjectId] = this;
            var rnd = new System.Random((int)(NetworkObjectId * 7919 % int.MaxValue));
            _uvOffset = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble() * 0.3f);
            Wood.OnValueChanged += OnWoodChanged;
            Hold.OnValueChanged += OnHoldChanged;
            _lastPos = transform.position;

            if (IsServer)
            {
                Rb.mass = JengaDims.BlockMass;
                Rb.solverIterations = 16;
                Rb.solverVelocityIterations = 4;
                Rb.interpolation = RigidbodyInterpolation.Interpolate;
                Rb.linearDamping = 0.05f;
                Rb.angularDamping = 0.2f;
                Rb.maxDepenetrationVelocity = 1.5f;
                Col.sharedMaterial = NormalMaterial;
            }
            ApplyVisuals();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(NetworkObjectId);
            Wood.OnValueChanged -= OnWoodChanged;
            Hold.OnValueChanged -= OnHoldChanged;
        }

        void OnWoodChanged(byte a, byte b) => ApplyVisuals();

        void OnHoldChanged(HoldMode previous, HoldMode current)
        {
            ApplyVisuals();
            if (current == HoldMode.Held && previous == HoldMode.None) AudioManager.Instance?.PlayGrab(transform.position);
        }

        public static JengaBlock Find(ulong id) => All.TryGetValue(id, out var b) ? b : null;

        // ------------------------------------------------------------------ Visuals
        public void SetHover(HoverMode mode)
        {
            if (_hover == mode) return;
            _hover = mode;
            ApplyVisuals();
        }

        void ApplyVisuals()
        {
            if (_renderer == null) return;
            var assets = SecretJengaAssets.Instance;
            if (assets.woodMaterials != null && assets.woodMaterials.Length > 0)
            {
                var wood = assets.woodMaterials[Wood.Value % assets.woodMaterials.Length];
                var end = assets.endGrainMaterial != null ? assets.endGrainMaterial : wood;
                if (wood != null) _renderer.sharedMaterials = new[] { wood, end };
            }

            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_BaseColor", WoodTint());
            _mpb.SetVector("_BaseMap_ST", new Vector4(1f, 1f, _uvOffset.x, _uvOffset.y));
            _mpb.SetColor("_EmissionColor", CurrentEmission());
            _mpb.SetFloat("_Smoothness", 0.28f);
            _renderer.SetPropertyBlock(_mpb);
        }

        /// <summary>Slight per-block variation of aged, slightly dirty wood.</summary>
        Color WoodTint()
        {
            float k = 0.78f + 0.12f * _uvOffset.x;
            return new Color(k, k * 0.96f, k * 0.9f);
        }

        Color CurrentEmission()
        {
            if (Hold.Value != HoldMode.None)
            {
                float pulse = 0.16f + 0.06f * Mathf.Sin(Time.time * 5f);
                return Color.Lerp(Palette.Lamp, Palette.Player(HolderColor.Value), 0.35f) * pulse;
            }
            switch (_hover)
            {
                case HoverMode.Valid: return Palette.Lamp * 0.22f;
                case HoverMode.Invalid: return new Color(0.9f, 0.12f, 0.08f) * 0.3f;
            }
            return Color.black;
        }

        void Update()
        {
            if (!IsSpawned) return;
            if (Hold.Value != HoldMode.None) ApplyVisuals(); // pulse

            // Collision sounds on every peer, derived from replicated motion (clients have no physics contacts).
            var pos = transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            float speed = (pos - _lastPos).magnitude / dt;
            _hitCooldown -= Time.deltaTime;
            if (Hold.Value == HoldMode.None && _hitCooldown <= 0f && _lastSpeed > 0.9f && speed < _lastSpeed * 0.35f)
            {
                AudioManager.Instance?.PlayBlockHit(pos, Mathf.Clamp01(_lastSpeed / 5f));
                _hitCooldown = 0.12f;
            }
            _lastSpeed = Mathf.Lerp(_lastSpeed, speed, 0.6f);
            _lastPos = pos;
        }

        // ------------------------------------------------------------------ Server API
        public void ServerInit(byte wood, int layer, int slot)
        {
            Wood.Value = wood;
            Layer.Value = layer;
            Slot = slot;
        }

        public void ServerSetLayer(int layer, int slot)
        {
            Layer.Value = layer;
            Slot = slot;
        }

        public void ServerBeginHold(byte holderColor, PhysicsMaterial heldMaterial = null)
        {
            HolderColor.Value = holderColor;
            Hold.Value = HoldMode.Held;
            Col.sharedMaterial = heldMaterial != null ? heldMaterial : SlipperyMaterial;
            Rb.constraints = RigidbodyConstraints.FreezeRotation;
            Rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Rb.WakeUp();
        }

        public void ServerEndHold()
        {
            Hold.Value = HoldMode.None;
            Col.sharedMaterial = NormalMaterial;
            Col.isTrigger = false;
            Rb.constraints = RigidbodyConstraints.None;
            Rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        /// <summary>Lifted out of the tower: kinematic, no collisions until placed.</summary>
        public void ServerBeginCarry()
        {
            Hold.Value = HoldMode.Carried;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            Rb.isKinematic = true;
            Col.isTrigger = true;
            Layer.Value = -1;
        }

        public void ServerPlaceAt(Vector3 pos, Quaternion rot)
        {
            Rb.isKinematic = true;
            Rb.position = pos;
            Rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Col.isTrigger = false;
            ServerEndHold();
            Rb.isKinematic = false;
            Rb.linearVelocity = Vector3.zero;
            Rb.angularVelocity = Vector3.zero;
            Rb.WakeUp();
        }

        public void ServerMoveCarried(Vector3 target, Quaternion targetRot, float dt)
        {
            Rb.MovePosition(Vector3.Lerp(Rb.position, target, 1f - Mathf.Exp(-14f * dt)));
            Rb.MoveRotation(Quaternion.Slerp(Rb.rotation, targetRot, 1f - Mathf.Exp(-10f * dt)));
        }
    }
}
