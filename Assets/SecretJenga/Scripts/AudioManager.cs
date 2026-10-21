using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// All game audio. Clips come from the SecretJengaAssets registry (generated WAVs in Assets/SecretJenga/Audio).
    /// Every call is null-safe, so the game runs silently if a clip is missing.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public enum Sfx { UIHover, UIClick, MissionComplete, TurnStart, TimerTick, Win, Lose, Countdown, Error, Place, Grab, Collapse }

        public static AudioManager Instance { get; private set; }

        const int PoolSize = 12;
        AudioSource _music, _slide;
        AudioSource[] _pool;
        int _next;
        float _hitBudget;
        float _slideTarget;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;

            _slide = gameObject.AddComponent<AudioSource>();
            _slide.loop = true;
            _slide.playOnAwake = false;
            _slide.volume = 0f;

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _pool[i] = s;
            }
            GameSettings.Changed += ApplyVolumes;
        }

        void Start()
        {
            var a = SecretJengaAssets.Instance;
            if (a.music != null) { _music.clip = a.music; _music.Play(); }
            if (a.blockSlideLoop != null) { _slide.clip = a.blockSlideLoop; _slide.Play(); }
            ApplyVolumes();
        }

        void OnDestroy()
        {
            GameSettings.Changed -= ApplyVolumes;
            if (Instance == this) Instance = null;
        }

        void ApplyVolumes()
        {
            AudioListener.volume = GameSettings.MasterVolume;
            if (_music != null) _music.volume = GameSettings.MusicVolume * 0.35f;
        }

        void Update()
        {
            _hitBudget = Mathf.Min(6f, _hitBudget + Time.unscaledDeltaTime * 30f);

            // Slide loop volume follows the speed of the block that is currently held.
            float target = 0f;
            var game = Game.NetworkGameManager.Instance;
            if (game != null && game.IsSpawned && game.Turns != null && game.Turns.Stage.Value == TurnStage.Dragging)
            {
                var b = Game.JengaBlock.Find(game.Turns.HeldBlockId.Value);
                if (b != null) target = Mathf.Clamp01(_slideSpeed(b) / 1.2f) * 0.6f;
            }
            _slideTarget = Mathf.Lerp(_slideTarget, target, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
            if (_slide != null) _slide.volume = _slideTarget * GameSettings.SfxVolume;
        }

        Vector3 _lastSlidePos;
        float _slideSpeed(Game.JengaBlock b)
        {
            var p = b.transform.position;
            float s = (p - _lastSlidePos).magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            _lastSlidePos = p;
            return s > 10f ? 0f : s;
        }

        AudioClip ClipFor(Sfx s)
        {
            var a = SecretJengaAssets.Instance;
            switch (s)
            {
                case Sfx.UIHover: return a.uiHover;
                case Sfx.UIClick: return a.uiClick;
                case Sfx.MissionComplete: return a.missionComplete;
                case Sfx.TurnStart: return a.turnStart;
                case Sfx.TimerTick: return a.timerTick;
                case Sfx.Win: return a.win;
                case Sfx.Lose: return a.lose;
                case Sfx.Countdown: return a.countdown;
                case Sfx.Error: return a.error;
                case Sfx.Place: return a.blockPlace;
                case Sfx.Grab: return a.blockGrab;
                case Sfx.Collapse: return a.towerCollapse;
            }
            return null;
        }

        public void Play(Sfx s, float volume = 1f, float pitch = 1f) => PlayClip(ClipFor(s), volume, pitch);

        public void PlayClip(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || _pool == null) return;
            var src = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(volume * GameSettings.SfxVolume);
            src.clip = clip;
            src.Play();
        }

        public void PlayBlockHit(Vector3 at, float intensity)
        {
            if (_hitBudget < 1f) return;
            _hitBudget -= 1f;
            var hits = SecretJengaAssets.Instance.blockHits;
            if (hits == null || hits.Length == 0) return;
            PlayClip(hits[Random.Range(0, hits.Length)], Mathf.Lerp(0.15f, 0.9f, intensity) * Attenuation(at), Random.Range(0.9f, 1.12f));
        }

        public void PlayPlace(Vector3 at) => Play(Sfx.Place, 0.9f * Attenuation(at), Random.Range(0.95f, 1.05f));
        public void PlayGrab(Vector3 at) => Play(Sfx.Grab, 0.6f * Attenuation(at), Random.Range(0.95f, 1.08f));
        public void PlayCollapse() => Play(Sfx.Collapse, 1f);

        static float Attenuation(Vector3 at)
        {
            var cam = Camera.main;
            if (cam == null) return 1f;
            float d = Vector3.Distance(cam.transform.position, at);
            return Mathf.Clamp(1.4f - d / 25f, 0.35f, 1f);
        }
    }
}
