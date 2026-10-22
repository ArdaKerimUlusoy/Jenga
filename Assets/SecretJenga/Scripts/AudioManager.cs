using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// All game audio. Clips come from the SecretJengaAssets registry (generated WAVs in Assets/SecretJenga/Audio).
    /// Every call is null-safe, so the game runs silently if a clip is missing.
    ///
    /// Risk is audible: the slide loop follows the speed of the held block (smooth friction), while a separate
    /// wood-strain loop follows the tower's wobble - a dangerous pull creaks and groans, a clean one just slides.
    /// The low drone bed rises slowly with tension but stays quiet enough to talk over.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public enum Sfx { UIHover, UIClick, MissionComplete, TurnStart, TimerTick, Win, Lose, Countdown, Error, Place, Grab, Collapse, CardPlayed }

        public static AudioManager Instance { get; private set; }

        const int PoolSize = 12;
        const int SpatialPoolSize = 6;
        AudioSource _music, _slide, _strain, _piano, _murmur;
        AudioSource[] _pool, _spatial;
        int _next, _nextSpatial;
        float _hitBudget;
        float _slideTarget, _strainTarget, _droneTension;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;

            _piano = MakeLoopSource();
            _murmur = MakeLoopSource();
            _slide = MakeLoopSource();
            _strain = MakeLoopSource();

            _pool = new AudioSource[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                _pool[i] = s;
            }

            _spatial = new AudioSource[SpatialPoolSize];
            for (int i = 0; i < SpatialPoolSize; i++)
            {
                var go = new GameObject("SpatialVoice_" + i);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Logarithmic;
                s.minDistance = 6f;
                s.maxDistance = 90f;
                s.dopplerLevel = 0f;
                _spatial[i] = s;
            }
            GameSettings.Changed += ApplyVolumes;
        }

        AudioSource MakeLoopSource()
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = true;
            s.playOnAwake = false;
            s.volume = 0f;
            return s;
        }

        void Start()
        {
            var a = SecretJengaAssets.Instance;
            if (a.music != null) { _music.clip = a.music; _music.Play(); }
            if (a.saloonPiano != null) { _piano.clip = a.saloonPiano; _piano.Play(); }
            if (a.saloonMurmur != null) { _murmur.clip = a.saloonMurmur; _murmur.Play(); }
            if (a.blockSlideLoop != null) { _slide.clip = a.blockSlideLoop; _slide.Play(); }
            if (a.woodStrainLoop != null) { _strain.clip = a.woodStrainLoop; _strain.Play(); }
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
        }

        /// <summary>Called by the AtmosphereDirector every frame (0..1).</summary>
        public void SetDroneTension(float t) => _droneTension = t;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _hitBudget = Mathf.Min(6f, _hitBudget + dt * 30f);

            if (_music != null)
            {
                // the old drone stays underneath and only comes up when the tower gets dangerous
                _music.volume = GameSettings.MusicVolume * Mathf.Lerp(_piano != null && _piano.clip != null ? 0.05f : 0.22f, 0.45f, _droneTension);
                _music.pitch = Mathf.Lerp(1f, 0.94f, _droneTension);
            }
            // saloon piano and murmur: the room goes quieter as everyone holds their breath
            if (_piano != null) _piano.volume = GameSettings.MusicVolume * Mathf.Lerp(0.42f, 0.1f, _droneTension);
            if (_murmur != null) _murmur.volume = GameSettings.SfxVolume * Mathf.Lerp(0.22f, 0.04f, _droneTension);

            // Slide loop follows the speed of the held block; strain loop follows the tower wobble while it is handled.
            float slide = 0f, strain = 0f;
            var game = Game.NetworkGameManager.Instance;
            if (game != null && game.IsSpawned && game.Turns != null)
            {
                var stage = game.Turns.Stage.Value;
                if (stage == TurnStage.Dragging)
                {
                    var b = Game.JengaBlock.Find(game.Turns.HeldBlockId.Value);
                    if (b != null) slide = Mathf.Clamp01(SlideSpeed(b) / 1.2f) * 0.55f;
                }
                if (stage == TurnStage.Dragging || stage == TurnStage.Carrying || stage == TurnStage.Settling)
                    strain = Mathf.Clamp01((game.Stability.Wobble.Value - 0.08f) / 0.5f);
            }
            _slideTarget = Mathf.Lerp(_slideTarget, slide, 1f - Mathf.Exp(-12f * dt));
            _strainTarget = Mathf.Lerp(_strainTarget, strain, 1f - Mathf.Exp(-6f * dt));
            if (_slide != null) _slide.volume = _slideTarget * GameSettings.SfxVolume;
            if (_strain != null)
            {
                _strain.volume = _strainTarget * 0.7f * GameSettings.SfxVolume;
                _strain.pitch = Mathf.Lerp(0.9f, 0.75f, _strainTarget);
            }
        }

        Vector3 _lastSlidePos;
        float SlideSpeed(Game.JengaBlock b)
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
                case Sfx.CardPlayed: return a.cardPlayed;
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

        /// <summary>One-shot positioned in the room (3D), e.g. distant metal, chair creaks, lamp zaps.</summary>
        public void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || _spatial == null) return;
            var src = _spatial[_nextSpatial];
            _nextSpatial = (_nextSpatial + 1) % _spatial.Length;
            src.transform.position = position;
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

        public void PlayCollapse()
        {
            Play(Sfx.Collapse, 1f);
            PlayClip(SecretJengaAssets.Instance.collapseSting, 0.85f);
        }

        static float Attenuation(Vector3 at)
        {
            var cam = Camera.main;
            if (cam == null) return 1f;
            float d = Vector3.Distance(cam.transform.position, at);
            return Mathf.Clamp(1.4f - d / 25f, 0.35f, 1f);
        }
    }
}
