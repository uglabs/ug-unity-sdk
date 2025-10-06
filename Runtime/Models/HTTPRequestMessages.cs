using Newtonsoft.Json;

namespace UG.Models
{
    public class HTTPRequestMessage
    {
        JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        };
        public string ToJson() => JsonConvert.SerializeObject(this, settings);
    }

    public class AuthenticateRequest : HTTPRequestMessage
    {
        [JsonProperty("api_key")]
        public string ApiKey { get; set; }
        [JsonProperty("team_name")]
        public string TeamName { get; set; }
        [JsonProperty("federated_id")]
        public string FederatedId { get; set; }
    }
}