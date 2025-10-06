using System.Threading;
using System.Threading.Tasks;

namespace UG.Services.HttpService
{
    public interface IHttpService
    {
        public Task<string> GetRequestAsync(string endpoint, CancellationToken cancellationToken, float? timeoutSeconds = null);
        public Task<string> PostRequestAsync(string endpoint, string data, CancellationToken cancellationToken);
        public void SetHeader(string name, string value);
        void SetAuthToken(string bearerToken);
        public void Dispose();
    }
}
