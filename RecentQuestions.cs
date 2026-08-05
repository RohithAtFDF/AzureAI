using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

public class RecentQuestions
{
    [Function("RecentQuestions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
        Route = "recent-questions")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            queries = new[]
            {
                "Test Question 1",
                "Test Question 2"
            }
        });

        return response;
    }
}