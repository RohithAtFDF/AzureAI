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
        var questions = new List<object>();

       await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            if (
                entity.TryGetValue("Email", out var emailObj) &&
                string.Equals(
                    emailObj?.ToString(),
                    user.Email,
                    StringComparison.OrdinalIgnoreCase
                ) &&
                entity.TryGetValue("Question", out var questionObj)
            )
            {
                questions.Add(new
                {
                    Question = questionObj?.ToString(),
                    Timestamp = entity.Timestamp
                });
            }
        }
        var latest = questions
            .OrderByDescending(x => ((dynamic)x).Timestamp)
            .Take(3)
            .ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);


        await response.WriteAsJsonAsync(new
        {
            count = latest.Count,
            queries = latest
        });

        return response;
    }
}