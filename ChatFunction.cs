using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

public class ChatFunction
{
    [Function("chat")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);

        response.WriteString("""
        {
            "message": "Function is working"
        }
        """);

        return response;
    }
}
``
