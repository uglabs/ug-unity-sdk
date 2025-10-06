using System.Threading;
using System.Threading.Tasks;
using UG.Services.HttpService;

namespace UG.Services.UGApiService
{
    internal class HealthEndpoint
    {
        private readonly IHttpService _httpService;
        private readonly Settings.UGSDKSettings _settings;

        public HealthEndpoint(IHttpService httpService, Settings.UGSDKSettings settings)
        {
            _httpService = httpService;
            _settings = settings;
        }

        public async Task<string> SendRequest(CancellationToken cancellationToken = default)
        {
            string jsonResult = await _httpService.GetRequestAsync("health", cancellationToken);
            return jsonResult;
        }
    }
} 