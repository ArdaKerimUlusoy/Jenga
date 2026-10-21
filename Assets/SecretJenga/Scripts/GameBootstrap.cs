using SecretJenga.Networking;
using Unity.Netcode;
using UnityEngine;

namespace SecretJenga
{
    /// <summary>
    /// Scene entry point. Applies physics/time settings tuned for stable stacking, makes sure the network prefabs
    /// are registered on the NetworkManager and starts Unity Services sign-in in the background.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        public SecretJengaAssets assets;

        void Awake()
        {
            if (assets != null) SecretJengaAssets.Instance = assets;

            Application.runInBackground = true;     // keeps a host simulating while its window is unfocused
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 1;

            // Stable Jenga stacking: 100 Hz physics, more solver iterations, low sleep threshold.
            Time.fixedDeltaTime = 0.01f;
            Time.maximumDeltaTime = 0.1f;
            Physics.gravity = new Vector3(0, -9.81f, 0);
            Physics.defaultSolverIterations = 16;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.sleepThreshold = 0.002f;
            Physics.defaultContactOffset = 0.004f;
            Physics.bounceThreshold = 1.5f;
            Physics.defaultMaxDepenetrationVelocity = 2f;

            GameSettings.ApplyDisplay();
            EnsureNetworkPrefabs();
        }

        async void Start()
        {
            if (MultiplayerServiceManager.Instance == null) return;
            try { await MultiplayerServiceManager.Instance.EnsureSignedInAsync(); }
            catch (System.Exception e) { Debug.LogWarning("[SecretJenga] Background sign-in failed (will retry when you go online): " + e.Message); }
        }

        /// <summary>Registers the block/session prefabs at runtime if they are not in the NetworkManager's prefab lists.</summary>
        public static void EnsureNetworkPrefabs()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsListening) return;
            var a = SecretJengaAssets.Instance;
            foreach (var prefab in new[] { a.blockPrefab, a.sessionPrefab })
            {
                if (prefab == null) continue;
                if (!nm.NetworkConfig.Prefabs.Contains(prefab)) nm.AddNetworkPrefab(prefab);
            }
        }
    }
}
