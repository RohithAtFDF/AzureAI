using System.Net;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class RecentQuestions
{
    private readonly TableClient _tableClient;

    public RecentQuestions()
    {
        _tableClient = new TableClient(
            Environment.GetEnvironmentVariable("AzureWebJobsStorage"),
            "ChatFeedback");
    }

    [Function("RecentQuestions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
        Route = "recent-questions")]
        HttpRequestData req)
    {
        var user = AuthUserExtractor.GetUser(req);

        var questions = new List<string>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            if (entity.TryGetValue("Question", out var question))
            {
                questions.Add(question?.ToString() ?? "");
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        

        await response.WriteAsJsonAsync(new
        {
            count = questions.Count,
            queries = questions.Take(10)
        });

        return response;
    }
}