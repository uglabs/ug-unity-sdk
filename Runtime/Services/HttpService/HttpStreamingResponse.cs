using System.Collections.Generic;
using System.Threading.Tasks;

namespace UG.Services.HttpService
{
    public class Headers : Dictionary<string, string>
    {

    };

    public class HttpStreamingResponse
    {
        public Dictionary<string, string> Headers { get; set; }
        public Task ResultLinesTask { get; set; }
        public Task CompletionTask { get; set; }

        public HttpStreamingResponse(Dictionary<string, string> headers, Task resultLinesTask, Task completionTask)
        {
            Headers = headers;
            ResultLinesTask = resultLinesTask;
            CompletionTask = completionTask;
        }
    }
}