using UnityEngine;

namespace SecretJenga.Game
{
    /// <summary>Local visual effects: dust puffs on placement, a heavy dust burst and a short, restrained camera jolt on collapse.</summary>
    public class EffectsManager : MonoBehaviour
    {
        public static EffectsManager Instance { get; private set; }

        ParticleSystem _dust;

        void Awake()
        {
            Instance = this;
            _dust = MakeSystem("FX_Dust", false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        ParticleSystem MakeSystem(string name, bool confetti)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = confetti ? 600 : 800;
            if (confetti)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
                main.gravityModifier = 0.5f;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                var colors = new Gradient();
                colors.SetKeys(
                    new[] { new GradientColorKey(Palette.PlayerColors[0], 0f), new GradientColorKey(Palette.PlayerColors[1], 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                main.startColor = new ParticleSystem.MinMaxGradient(colors) { mode = ParticleSystemGradientMode.RandomColor };
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1.2f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
                main.startColor = new Color(0.62f, 0.60f, 0.56f, 0.4f);
                main.gravityModifier = -0.02f;
            }

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = confetti ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
            shape.radius = confetti ? 0.5f : 0.25f;
            if (confetti) { shape.angle = 30f; go.transform.rotation = Quaternion.Euler(-90, 0, 0); }

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(confetti ? 1f : 0.7f, 0f), new GradientAlphaKey(confetti ? 1f : 0.35f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var size = ps.sizeOverLifetime;
            size.enabled = !confetti;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));

            if (confetti)
            {
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
                var noise = ps.noise;
                noise.enabled = true;
                noise.strength = 0.8f;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            var assets = SecretJengaAssets.Instance;
            if (assets.dustMaterial != null && !confetti) r.sharedMaterial = assets.dustMaterial;
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                var m = new Material(shader);
                RuntimeArt.MakeTransparent(m);
                r.sharedMaterial = m;
            }
            r.renderMode = ParticleSystemRenderMode.Billboard;
            return ps;
        }

        public void Puff(Vector3 at)
        {
            if (_dust == null) return;
            _dust.transform.position = at;
            _dust.Emit(14);
        }

        public void Collapse(Vector3 at, int count)
        {
            if (_dust != null)
            {
                for (int i = 0; i < 6; i++)
                {
                    _dust.transform.position = new Vector3(Random.Range(-1.5f, 1.5f), JengaDims.TableTopY + 0.2f, Random.Range(-1.5f, 1.5f));
                    _dust.Emit(25);
                }
                _dust.transform.position = at;
                _dust.Emit(40);
            }
            CameraController.Instance?.Shake(0.12f, 0.6f);
        }
    }
}
