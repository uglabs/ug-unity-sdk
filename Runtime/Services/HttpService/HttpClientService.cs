using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UG.Exceptions;

namespace UG.Services.HttpService
{
    public class HttpClientService : IHttpService
    {
        private HttpClient _httpClient;
        private string _host;
        private string _bearerToken;

        public HttpClientService(string host, string bearerToken = null, float timeoutSeconds = 25)
        {
            _host = host;
            _bearerToken = bearerToken;
            _httpClient = new()
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        public void SetAuthToken(string authToken)
        {
            _bearerToken = authToken;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        public void SetHeader(string name, string value)
        {
            _httpClient.DefaultRequestHeaders.Remove(name);
            _httpClient.DefaultRequestHeaders.Add(name, value);
            UGLog.Log($"SetHeader: {name} = {value}");
        }

        public async Task<string> GetRequestAsync(string endpoint, CancellationToken cancellationToken = default, float? timeoutSeconds = null)
        {
            try
            {
                UGLog.Log($"[HttpService] Request to: {_host}/{endpoint}");

                HttpClient clientToUse = _httpClient;
                HttpClient temporaryClient = null;

                // Create a temporary client if a custom timeout is specified (we can't re-use httpClient with a custom timeout)
                if (timeoutSeconds.HasValue)
                {
                    temporaryClient = new HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(timeoutSeconds.Value)
                    };

                    // Copy authorization header
                    temporaryClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _bearerToken);

                    // Copy other headers
                    foreach (var header in _httpClient.DefaultRequestHeaders)
                    {
                        if (header.Key != "Authorization") // Skip Authorization as we already added it
                        {
                            temporaryClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                        }
                    }

                    clientToUse = temporaryClient;
                    UGLog.Log($"[HttpService] Using custom timeout: {timeoutSeconds.Value} seconds");
                }

                HttpResponseMessage response = await clientToUse.GetAsync($"{_host}/{endpoint}", cancellationToken);
                var result = await response.Content.ReadAsStringAsync();

                // Dispose the temporary client if we created one
                temporaryClient?.Dispose();

                if (response.IsSuccessStatusCode)
                {
                    return result;
                }
                else
                {
                    string errorText = $"[HttpService] Request failed: {_host}/{endpoint} - {response.StatusCode}:{result}";
                    throw new UGSDKException(errorText);
                }
            }
            catch (Exception e)
            {
                UGLog.LogError($"[HttpService] Exception {e.Message} {e.Data.Count}");
                throw;
            }
        }

        public async Task<string> PostRequestAsync(string endpoint, string jsonData, CancellationToken cancellationToken = default)
        {
            try
            {
                UGLog.Log($"[HttpService] Post request to: {_host}/{endpoint}");
                HttpContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                UGLog.Log($"[HttpService] Post data: {jsonData}");

                HttpResponseMessage response = await _httpClient.PostAsync($"{_host}/{endpoint}", content, cancellationToken);
                var result = await response.Content.ReadAsStringAsync();

                SetHeader("traceparent", "");

                if (response.IsSuccessStatusCode)
                {
                    UGLog.Log($"[HttpService] Post request success: {result} code: {(int)response.StatusCode}");
                    return result;
                }
                else
                {
                    string errorText = $"[HttpService] Request failed: {_host}/{endpoint} - {(int)response.StatusCode} {response.StatusCode}:{result} ";
                    throw new UGSDKException(errorText);
                }
            }
            catch (HttpRequestException e)
            {
                UGLog.LogError($"[HttpService] Exception: {e.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}