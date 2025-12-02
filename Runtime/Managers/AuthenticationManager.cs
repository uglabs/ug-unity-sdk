using System;
using System.Threading;
using System.Threading.Tasks;
using UG.Services.UGApiService;
using UG.Settings;
using UG.Services;
using UnityEngine;
using UG.Models;
using UG.Utils;
using UG.Exceptions;

namespace UG.Managers
{
    /// <summary>
    /// Authentication manager for V3
    /// Handles authentication and token management
    /// </summary>
    public class AuthenticationManager
    {
        public bool IsAuthenticated { get; private set; }
        public string BearerToken { get; private set; }
        private readonly SemaphoreSlim _authSemaphore = new SemaphoreSlim(1, 1);

        #region Dependencies
        private readonly UGSDKSettings _settings;
        private readonly UGApiService _ugApiService;
        #endregion

        public string AccessToken { get; private set; }
        public event Action<string> OnAuthenticated;
        public event Action OnAuthenticationFailed;

        private CancellationTokenSource _cancellationTokenSource;

        public AuthenticationManager(UGSDKSettings settings,
            UGApiService ugApiService)
        {
            _settings = settings;
            _ugApiService = ugApiService;
        }

        public void Authenticate(string externalPlayerId)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _ = Task.Run(() => EnsureAuthenticatedAsync(_cancellationTokenSource.Token));
        }

        public async Task EnsureAuthenticatedOrThrowAsync(CancellationToken cancellationToken = default)
        {
            await EnsureAuthenticatedAsync(cancellationToken);
            if (!IsAuthenticated)
            {
                OnAuthenticationFailed?.Invoke();
                throw new UGSDKException(ExceptionConsts.NotAuthenticated);
            }
        }

        private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken = default)
        {
            if (IsAuthenticated)
            {
                return;
            }

            try
            {
                // Check semaphore is available (in case multiple auths are called)
                await _authSemaphore.WaitAsync(cancellationToken);
                // Double-check after acquiring semaphore
                if (IsAuthenticated)
                {
                    return;
                }

                // Create the auth request object with the provided credentials
                var authRequest = new AuthenticateRequest
                {
                    ApiKey = _settings.apiKey,
                    TeamName = _settings.teamName,
                    FederatedId = _settings.federatedId
                };

                var startTime = DateTime.UtcNow;

                // Send the request through UGApiServiceV2
                var response = await _ugApiService.Auth(authRequest, cancellationToken);
                UGLog.Log($"Auth result: {Json.Serialize(response)}");

                BearerToken = response.AccessToken;
                IsAuthenticated = true;

                // Check auth rtt - to understand the initial latency
                float rtt = (float)(DateTime.UtcNow - startTime).TotalMilliseconds;
                UGLog.Log($"Auth RTT: {rtt}");

                AccessToken = response.AccessToken;
                OnAuthenticated?.Invoke(AccessToken);
            }
            catch (Exception ex)
            {
                UGLog.LogError($"Auth failed: {ex}");
                OnAuthenticationFailed?.Invoke();
            }
            finally
            {
                _authSemaphore.Release();
            }
        }
    }
}