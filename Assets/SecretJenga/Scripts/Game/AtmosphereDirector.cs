using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SecretJenga.Game
{
    /// <summary>
    /// Local presentation of the room's mood. Reads the replicated tension (AtmosphereSync) and the synced events,
    /// then drives the wagon-wheel chandelier (candle noise, flicker bursts, sway), the wall lanterns (gentle
    /// candle flicker, they only dip a little when the chandelier flickers), the room sounds (lamp hum, drone,
    /// heartbeat at high tension, distant metal, chair creaks) and the post-processing (vignette, exposure).
    /// Effects are brief and restrained: the tower must always stay readable.
    /// </summary>
    public class AtmosphereDirector : MonoBehaviour
    {
        public static AtmosphereDirector Instance { get; private set; }

        [Header("Scene references (wired by the generator)")]
        public Light lampLight;
        public Light bulbGlow;
        public Transform lampRig;
        public Renderer bulbRenderer;
        [Tooltip("Other glowing things (lantern glass) that follow the flicker.")]
        public Renderer[] extraBulbs;
        [Tooltip("Wall lanterns: soft independent flicker, only partly affected by the chandelier flicker.")]
        public Light[] roomLights;
        public Volume volume;

        [Header("Tuning")]
        [Tooltip("Lowest lamp intensity multiplier during flicker (never fully dark, blocks must stay visible).")]
        [Range(0.05f, 1f)] public float flickerFloor = 0.22f;
        [Tooltip("Max lamp sway angle (degrees) at full tension.")]
        public float maxSway = 2.2f;

        float _baseLamp, _baseGlow, _flickerMul = 1f, _swayVel, _swayAngle, _swayAngle2, _swayVel2, _tension;
        Color _bulbEmission;
        Color[] _extraEmission;
        float[] _roomBase;
        MaterialPropertyBlock _mpb;
        Vignette _vignette;
        ColorAdjustments _color;
        float _baseVignette, _baseExposure;
        AudioSource _hum, _heart;
        Coroutine _flicker;
        bool _collapsed;

        void Awake()
        {
            Instance = this;
            _mpb = new MaterialPropertyBlock();
            if (lampLight == null) lampLight = FindLight("Lamp Spot Light");
            if (bulbGlow == null) bulbGlow = FindLight("Bulb Glow");
            if (lampRig == null) { var go = GameObject.Find("LampRig"); if (go != null) lampRig = go.transform; }
            if (volume == null) volume = FindFirstObjectByType<Volume>();
            if (lampLight != null) _baseLamp = lampLight.intensity;
            if (bulbGlow != null) _baseGlow = bulbGlow.intensity;
            if (bulbRenderer != null && bulbRenderer.sharedMaterial != null && bulbRenderer.sharedMaterial.HasProperty("_EmissionColor"))
                _bulbEmission = bulbRenderer.sharedMaterial.GetColor("_EmissionColor");
            if (extraBulbs != null)
            {
                _extraEmission = new Color[extraBulbs.Length];
                for (int i = 0; i < extraBulbs.Length; i++)
                    if (extraBulbs[i] != null && extraBulbs[i].sharedMaterial != null && extraBulbs[i].sharedMaterial.HasProperty("_EmissionColor"))
                        _extraEmission[i] = extraBulbs[i].sharedMaterial.GetColor("_EmissionColor");
            }
            if (roomLights != null)
            {
                _roomBase = new float[roomLights.Length];
                for (int i = 0; i < roomLights.Length; i++) _roomBase[i] = roomLights[i] != null ? roomLights[i].intensity : 0f;
            }
            if (volume != null && volume.profile != null)
            {
                volume.profile.TryGet(out _vignette);
                volume.profile.TryGet(out _color);
                if (_vignette != null) _baseVignette = _vignette.intensity.value;
                if (_color != null) _baseExposure = _color.postExposure.value;
            }
            TurnManager.TurnStarted += OnTurnStarted;
            NetworkGameManager.MatchEnded += OnMatchEnded;
        }

        void Start()
        {
            var a = SecretJengaAssets.Instance;
            var at = lampRig != null ? lampRig.position : new Vector3(0, SeatLayout.LampHeight, 0);
            _hum = MakeLoop("LampHum", a.lampHumLoop, at, 1f, 12f);
            _heart = MakeLoop("Heartbeat", a.heartbeatLoop, Vector3.zero, 0f, 0f);
        }

        void OnDestroy()
        {
            TurnManager.TurnStarted -= OnTurnStarted;
            NetworkGameManager.MatchEnded -= OnMatchEnded;
            if (_vignette != null) _vignette.intensity.value = _baseVignette;
            if (_color != null) _color.postExposure.value = _baseExposure;
            if (Instance == this) Instance = null;
        }

        static Light FindLight(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<Light>() : null;
        }

        AudioSource MakeLoop(string name, AudioClip clip, Vector3 pos, float spatial, float minDistance)
        {
            if (clip == null) return null;
            var go = new GameObject("Atmos_" + name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = spatial;
            s.minDistance = Mathf.Max(1f, minDistance);
            s.maxDistance = 80f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.volume = 0f;
            s.Play();
            return s;
        }

        // ------------------------------------------------------------------ Per frame
        void Update()
        {
            var game = NetworkGameManager.Instance;
            bool inGame = game != null && game.IsSpawned;
            float target = inGame && game.Atmosphere != null ? game.Atmosphere.Tension.Value : 0.1f;
            _tension = Mathf.Lerp(_tension, target, 1f - Mathf.Exp(-2f * Time.deltaTime));
            float t = _tension;

            // lamp: subtle electrical noise that grows with tension + flicker bursts
            float noise = (Mathf.PerlinNoise(Time.time * 7.3f, 0.37f) - 0.5f) * 0.12f * t;
            float lampMul = Mathf.Clamp(1f + noise, 0.6f, 1.1f) * _flickerMul;
            if (lampLight != null) lampLight.intensity = _baseLamp * lampMul;
            if (bulbGlow != null) bulbGlow.intensity = _baseGlow * lampMul;
            if (bulbRenderer != null && _bulbEmission.maxColorComponent > 0f)
            {
                bulbRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", _bulbEmission * Mathf.Max(0.05f, lampMul));
                bulbRenderer.SetPropertyBlock(_mpb);
            }

            // wall lanterns: each flame breathes on its own; a chandelier flicker only dims them slightly
            if (roomLights != null && _roomBase != null)
            {
                float shared = Mathf.Lerp(1f, _flickerMul, 0.3f);
                for (int i = 0; i < roomLights.Length; i++)
                {
                    if (roomLights[i] == null) continue;
                    float f = 0.9f + 0.1f * Mathf.PerlinNoise(Time.time * 3.1f + i * 7.7f, i * 1.3f) + 0.04f * Mathf.Sin(Time.time * 11f + i);
                    roomLights[i].intensity = _roomBase[i] * f * shared;
                    if (extraBulbs != null && i < extraBulbs.Length && extraBulbs[i] != null && _extraEmission != null && _extraEmission[i].maxColorComponent > 0f)
                    {
                        extraBulbs[i].GetPropertyBlock(_mpb);
                        _mpb.SetColor("_EmissionColor", _extraEmission[i] * f * shared);
                        extraBulbs[i].SetPropertyBlock(_mpb);
                    }
                }
            }

            // lamp sway: a damped pendulum, gently pushed more as tension rises
            float dt = Time.deltaTime;
            float drive = Mathf.Sin(Time.time * 0.7f) * maxSway * t * 0.6f;
            _swayVel += (-(_swayAngle - drive) * 2.2f - _swayVel * 0.35f) * dt;
            _swayAngle += _swayVel * dt * 4f;
            float drive2 = Mathf.Cos(Time.time * 0.53f) * maxSway * t * 0.4f;
            _swayVel2 += (-(_swayAngle2 - drive2) * 2.0f - _swayVel2 * 0.35f) * dt;
            _swayAngle2 += _swayVel2 * dt * 4f;
            if (lampRig != null) lampRig.localRotation = Quaternion.Euler(_swayAngle, 0f, _swayAngle2);

            // post processing: darker edges and slightly lower exposure as the tower becomes dangerous
            if (_vignette != null) _vignette.intensity.value = Mathf.Lerp(_baseVignette, _baseVignette + 0.18f, t);
            if (_color != null) _color.postExposure.value = _baseExposure - 0.25f * t + (_flickerMul - 1f) * 0.6f;

            // sounds
            if (_hum != null) _hum.volume = GameSettings.SfxVolume * (0.18f + 0.12f * t) * Mathf.Clamp01(_flickerMul + 0.2f);
            if (_heart != null) _heart.volume = GameSettings.SfxVolume * Mathf.Clamp01((t - 0.6f) / 0.4f) * 0.35f;
            AudioManager.Instance?.SetDroneTension(t);
        }

        // ------------------------------------------------------------------ Events
        public void PlayEvent(AtmosphereEvent e, int seed, float strength)
        {
            var a = SecretJengaAssets.Instance;
            var rnd = new System.Random(seed);
            switch (e)
            {
                case AtmosphereEvent.Flicker:
                    StartFlicker(rnd, strength, 0.25f + 0.9f * strength);
                    break;
                case AtmosphereEvent.DistantMetal:
                case AtmosphereEvent.DeepGroan:
                    if (a.distantMetal != null && a.distantMetal.Length > 0)
                    {
                        var clip = a.distantMetal[rnd.Next(a.distantMetal.Length)];
                        float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                        var pos = new Vector3(Mathf.Cos(ang) * 30f, SeatLayout.FloorY + 20f, Mathf.Sin(ang) * 30f);
                        float pitch = e == AtmosphereEvent.DeepGroan ? 0.62f : 0.85f + (float)rnd.NextDouble() * 0.3f;
                        AudioManager.Instance?.PlayAt(clip, pos, 0.55f * strength, pitch);
                    }
                    break;
            }
        }

        void StartFlicker(System.Random rnd, float strength, float duration)
        {
            if (_flicker != null) StopCoroutine(_flicker);
            _flicker = StartCoroutine(FlickerRoutine(rnd, strength, duration));
            var a = SecretJengaAssets.Instance;
            if (a.flicker != null) AudioManager.Instance?.PlayAt(a.flicker, lampRig != null ? lampRig.position : Vector3.up * SeatLayout.LampHeight, 0.4f * strength, 1f);
        }

        IEnumerator FlickerRoutine(System.Random rnd, float strength, float duration)
        {
            float end = Time.time + duration;
            while (Time.time < end)
            {
                bool off = rnd.NextDouble() < 0.45;
                _flickerMul = off ? Mathf.Lerp(1f, flickerFloor, strength) : 0.85f + (float)rnd.NextDouble() * 0.2f;
                yield return new WaitForSeconds(0.025f + (float)rnd.NextDouble() * 0.09f);
            }
            _flickerMul = 1f;
            _flicker = null;
        }

        void OnTurnStarted(ulong clientId)
        {
            // someone shifts in their chair
            var game = NetworkGameManager.Instance;
            var a = SecretJengaAssets.Instance;
            if (game == null || a.chairCreaks == null || a.chairCreaks.Length == 0) return;
            if (!game.TryGetPlayer(clientId, out var p)) return;
            var clip = a.chairCreaks[(int)(clientId + (ulong)game.Turns.TurnNumber.Value) % a.chairCreaks.Length];
            AudioManager.Instance?.PlayAt(clip, SeatLayout.ChairPosition(p.SeatIndex) + Vector3.up * 6f, 0.5f, 0.9f + 0.05f * p.SeatIndex);
        }

        public void OnCollapse()
        {
            _collapsed = true;
            _swayVel += 3.5f;
            _swayVel2 -= 2.5f;
            StartFlicker(new System.Random(12345), 1f, 1.6f);
        }

        void OnMatchEnded(bool won)
        {
            if (won && !_collapsed) { _swayVel *= 0.2f; _swayVel2 *= 0.2f; }
            _collapsed = false;
        }
    }
}
