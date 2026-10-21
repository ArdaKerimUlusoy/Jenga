using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SecretJenga.EditorTools
{
    /// <summary>Tools > Secret Jenga > Validate Setup / Open Setup Guide.</summary>
    public static class SecretJengaSetupValidator
    {
        [MenuItem("Tools/Secret Jenga/Validate Setup", priority = 20)]
        public static void Validate()
        {
            var sb = new StringBuilder();
            int problems = 0;
            void Ok(string s) => sb.AppendLine("OK   " + s);
            void Bad(string s) { sb.AppendLine("FIX  " + s); problems++; }

            if (!string.IsNullOrEmpty(CloudProjectSettings.projectId)) Ok("Linked to Unity Cloud project " + CloudProjectSettings.projectId);
            else Bad("Project is not linked to Unity Cloud (Edit > Project Settings > Services).");

            var reg = AssetDatabase.LoadAssetAtPath<SecretJengaAssets>(SecretJengaProjectGenerator.Root + "/Resources/SecretJengaAssets.asset");
            if (reg == null) Bad("Asset registry missing - run Tools > Secret Jenga > Generate Project.");
            else
            {
                if (reg.blockPrefab != null && reg.blockPrefab.GetComponent<NetworkObject>() != null) Ok("Block prefab (NetworkObject + NetworkTransform + NetworkRigidbody)");
                else Bad("Block prefab missing or broken.");
                if (reg.sessionPrefab != null) Ok("Session prefab"); else Bad("Session prefab missing.");
                if (reg.music != null && reg.blockHits != null && reg.blockHits.Length > 0 && reg.blockHits[0] != null) Ok("Audio clips registered"); else Bad("Audio clips missing (game will be silent).");
                if (reg.panel != null && reg.missionIcons != null && reg.missionIcons.Length == 8 && reg.missionIcons[1] != null) Ok("UI sprites registered"); else Bad("UI sprites missing (runtime fallbacks used).");
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
            }
            else sb.AppendLine("--   Open the game scene to validate the NetworkManager.");

            sb.Insert(0, problems == 0 ? "Everything looks good!\n\n" : problems + " item(s) need attention:\n\n");
            Debug.Log("[SecretJenga] Setup validation\n" + sb);
            EditorUtility.DisplayDialog("Secret Jenga - Validate Setup", sb.ToString(), "OK");
        }

        [MenuItem("Tools/Secret Jenga/Open Setup Guide (README)", priority = 21)]
        public static void OpenGuide()
        {
            var path = SecretJengaProjectGenerator.Root + "/README.md";
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset != null) AssetDatabase.OpenAsset(asset);
            else EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Tools/Secret Jenga/Open Unity Dashboard", priority = 22)]
        public static void OpenDashboard() => Application.OpenURL("https://cloud.unity.com/");
    }
}
