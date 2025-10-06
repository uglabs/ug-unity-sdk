using Newtonsoft.Json;

namespace UG.Utils
{
    public static class Json
    {
        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json);
        }

        public static string Serialize<T>(T item)
        {
            return JsonConvert.SerializeObject(item);
        }
    }
}