using UG.Services.HttpService;
using System.Threading.Tasks;
using System.Threading;
using UG.Models;
using UG.Services.Network.APIService.Endpoints;

namespace UG.Services.UGApiService
{
    public class UGApiServiceV3
    {
        private readonly IHttpService _httpService;
        private readonly Settings.UGSDKSettings _settings;

        #region Request Handlers
        private readonly AuthenticateEndpoint _authEndpoint;
        private readonly HealthEndpoint _healthEndpoint;
        #endregion

        public UGApiServiceV3(Settings.UGSDKSettings settings, IHttpService httpService)
        {
            _httpService = httpService;
            _settings = settings;

            // Initialize endpoint handlers
            _authEndpoint = new AuthenticateEndpoint(_httpService);
            _healthEndpoint = new HealthEndpoint(_httpService, _settings);
        }

        public async Task<string> Health(CancellationToken cancellationToken = default)
        {
            string result = await _healthEndpoint.SendRequest(cancellationToken);
            UGLog.Log($"[UGServerService] Request result {result}");
            return result;
        }

        public async Task<AuthenticateResponse> Auth(AuthenticateRequest request, 
            CancellationToken cancellationToken = default)
        {
            var result = await _authEndpoint.SendRequest(request, cancellationToken);
            UGLog.Log($"Auth result: {result}");
            return result;
        }

        public void Dispose()
        {
            _httpService.Dispose();
        }
    }
}