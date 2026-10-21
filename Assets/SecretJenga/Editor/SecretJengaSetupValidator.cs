using System.IO;
using System.Text;
using SecretJenga.Game;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SecretJenga.EditorTools
{
    /// <summary>Tools > The Last Block > Validate Setup / Open Setup Guide.</summary>
    public static class SecretJengaSetupValidator
    {
        [MenuItem("Tools/The Last Block/Validate Setup", priority = 20)]
        public static void Validate()
        {
            var sb = new StringBuilder();
            int problems = 0;
            void Ok(string s) => sb.AppendLine("OK   " + s);
            void Bad(string s) { sb.AppendLine("FIX  " + s); problems++; }
            void Info(string s) => sb.AppendLine("--   " + s);

            if (!string.IsNullOrEmpty(CloudProjectSettings.projectId)) Ok("Linked to Unity Cloud project " + CloudProjectSettings.projectId + " (Online / Relay mode available)");
            else Info("Not linked to Unity Cloud: only Direct (IP / LAN) mode will work. Link it in Edit > Project Settings > Services for Online (Relay) mode.");

            var reg = AssetDatabase.LoadAssetAtPath<SecretJengaAssets>(SecretJengaProjectGenerator.Root + "/Resources/SecretJengaAssets.asset");
            if (reg == null) Bad("Asset registry missing - run Tools > The Last Block > Generate Project.");
            else
            {
                if (reg.blockPrefab != null && reg.blockPrefab.GetComponent<NetworkObject>() != null) Ok("Block prefab (NetworkObject + NetworkTransform + NetworkRigidbody)");
                else Bad("Block prefab missing or broken.");
                if (reg.sessionPrefab != null && reg.sessionPrefab.GetComponent<CardManager>() != null && reg.sessionPrefab.GetComponent<SecretTaskManager>() != null) Ok("Session prefab (turns, tower, tasks, cards, stability, atmosphere)");
                else Bad("Session prefab missing or outdated - run Generate Project.");
                if (reg.tasks != null && reg.tasks.Length > 0) Ok(reg.tasks.Length + " task definitions registered"); else Bad("No task definitions registered (runtime defaults will be used).");
                if (reg.cards != null && reg.cards.Length > 0) Ok(reg.cards.Length + " card definitions registered"); else Bad("No card definitions registered (runtime defaults will be used).");
                if (reg.music != null && reg.lampHumLoop != null && reg.collapseSting != null && reg.blockHits != null && reg.blockHits.Length > 0 && reg.blockHits[0] != null) Ok("Audio clips registered");
                else Bad("Audio clips missing (game will be partly silent). Run Tools~/generate_horror_audio.py, then Generate Project.");
                if (reg.panel != null) Ok("UI sprites registered"); else Bad("UI sprites missing (runtime fallbacks used).");
            }

            if (File.Exists(SecretJengaProjectGenerator.ScenePath)) Ok("Game scene exists");
            else Bad("Game scene missing.");
            bool inBuild = false;
            foreach (var s in EditorBuildSettings.scenes) if (s.path == SecretJengaProjectGenerator.ScenePath && s.enabled) inBuild = true;
            if (inBuild) Ok("Game scene is in Build Settings"); else Bad("Game scene not in Build Settings.");

            if (PlayerSettings.runInBackground) Ok("Run In Background enabled"); else Bad("Enable Player Settings > Run In Background (hosts must keep simulating).");

            var nm = Object.FindFirstObjectByType<NetworkManager>();
            if (EditorSceneManager.GetActiveScene().path == SecretJengaProjectGenerator.ScenePath)
            {
                if (nm == null) Bad("NetworkManager missing from the scene.");
                else
                {
                    if (nm.NetworkConfig.NetworkTransport != null) Ok("NetworkManager transport = " + nm.NetworkConfig.NetworkTransport.GetType().Name); else Bad("NetworkManager has no transport.");
                    if (!nm.NetworkConfig.EnableSceneManagement) Ok("Scene management disabled (single-scene game)"); else Bad("Disable 'Enable Scene Management' on the NetworkManager.");
                    if (nm.NetworkConfig.ConnectionApproval) Ok("Connection approval enabled"); else Bad("Enable Connection Approval on the NetworkManager.");
                }
                if (Object.FindFirstObjectByType<AtmosphereDirector>() != null) Ok("AtmosphereDirector in scene"); else Bad("AtmosphereDirector missing - regenerate the scene.");
                if (Object.FindFirstObjectByType<PlayerAvatars>() != null) Ok("PlayerAvatars in scene"); else Bad("PlayerAvatars missing - regenerate the scene.");
            }
            else Info("Open the game scene to validate the NetworkManager.");

            sb.Insert(0, problems == 0 ? "Everything looks good!\n\n" : problems + " item(s) need attention:\n\n");
            Debug.Log("[TheLastBlock] Setup validation\n" + sb);
            EditorUtility.DisplayDialog("The Last Block - Validate Setup", sb.ToString(), "OK");
        }

        [MenuItem("Tools/The Last Block/Open Setup Guide (README)", priority = 21)]
        public static void OpenGuide()
        {
            var path = SecretJengaProjectGenerator.Root + "/README.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null) AssetDatabase.OpenAsset(asset);
            else EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Tools/The Last Block/Open Unity Dashboard", priority = 22)]
        public static void OpenDashboard() => Application.OpenURL("https://cloud.unity.com/");
    }
}
