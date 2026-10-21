using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace SecretJenga.Networking
{
    /// <summary>Initializes Unity Gaming Services and signs the player in anonymously (one identity per auth profile).</summary>
    public class MultiplayerServiceManager : MonoBehaviour
    {
        public static MultiplayerServiceManager Instance { get; private set; }

        public bool IsSignedIn => UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;
        public string Profile { get; private set; }
        public string LastError { get; private set; }

        Task _signInTask;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Safe to call many times; concurrent callers share the same task.</summary>
        public Task EnsureSignedInAsync()
        {
            if (IsSignedIn) return Task.CompletedTask;
            if (_signInTask == null || _signInTask.IsFaulted || _signInTask.IsCanceled) _signInTask = SignInInternalAsync();
            return _signInTask;
        }

        async Task SignInInternalAsync()
        {
            LastError = null;
            try
            {
                Profile = GameSettings.ResolveAuthProfile();
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    var options = new InitializationOptions();
                    options.SetProfile(Profile);
                    await UnityServices.InitializeAsync(options);
                }
                else
                {
                    while (UnityServices.State == ServicesInitializationState.Initializing) await Task.Yield();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();

                Debug.Log($"[SecretJenga] Signed in. Profile='{Profile}' PlayerId={AuthenticationService.Instance.PlayerId}");
            }
            catch (Exception e)
            {
                LastError = Describe(e);
                Debug.LogError("[SecretJenga] UGS sign-in failed: " + e);
                throw new Exception(LastError, e);
            }
        }

        public static string Describe(Exception e)
        {
            var msg = e.Message ?? e.GetType().Name;
            var all = e.ToString();
            if (all.Contains("ProjectId") || all.Contains("project id") || all.Contains("not linked") || all.Contains("cloudProjectId"))
                return "This Unity project is not linked to Unity Cloud.\nOpen Edit > Project Settings > Services and link a project, then restart.";
            if (all.Contains("RequestFailedException") && (all.Contains("Network") || all.Contains("Cannot connect")))
                return "Could not reach Unity services. Check your internet connection.";
            if (all.Contains("RateLimited") || all.Contains("Too Many Requests"))
                return "Too many requests. Please wait a few seconds and try again.";
            return msg;
        }
    }
}
