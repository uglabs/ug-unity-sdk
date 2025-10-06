using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UG.Services.HttpService;
using UG.Models;
using UG.Utils;

namespace UG.Services.Network.APIService.Endpoints
{
    internal class AuthenticateEndpoint
    {
        private readonly IHttpService _httpService;

        public AuthenticateEndpoint(IHttpService httpService)
        {
            _httpService = httpService;
        }

        public async Task<AuthenticateResponse> SendRequest(AuthenticateRequest request, CancellationToken cancellationToken = default)
        {
            string endpoint = "api/auth/login";
            string payload = Json.Serialize(request);
            string jsonResult = await _httpService.PostRequestAsync(endpoint, payload, cancellationToken);
            return Json.Deserialize<AuthenticateResponse>(jsonResult);
        }
    }
} 