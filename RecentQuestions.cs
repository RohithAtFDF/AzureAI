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

        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            var unauthorized = req.CreateResponse(HttpStatusCode.Unauthorized);
            return unauthorized;
        }

        var questions = new List<object>();

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(
            filter: $"Email eq '{user.Email.Replace("'", "''")}'"))
        {
            if (entity.TryGetValue("Question", out var questionObj))
            {
                questions.Add(new
                {
                    Question = questionObj?.ToString(),
                    Timestamp = entity.Timestamp
                });
            }
        }

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            Email = user.Email,
            Count = questions.Count,
            Queries = questions
        });

        return response;
    }
}