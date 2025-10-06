using Newtonsoft.Json;

namespace UG.Models
{
    public class HTTPResponseMessage
    {
        
    }

    public class AuthenticateResponse : HTTPResponseMessage
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }
}