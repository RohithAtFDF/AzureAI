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

        var questions = new List<(DateTime Timestamp, string Question)>();

        await foreach (TableEntity entity in _tableClient.QueryAsync<TableEntity>())
        {
            if (
                entity.TryGetValue("Email", out var emailObj) &&
                emailObj?.ToString()?.Equals(
                    user.Email,
                    StringComparison.OrdinalIgnoreCase) == true &&
                entity.TryGetValue("Question", out var questionObj)
            )
            {
                var timestamp =
                    entity.Timestamp?.UtcDateTime ??
                    DateTime.MinValue;

                questions.Add((
                    timestamp,
                    questionObj?.ToString() ?? ""
                ));
            }
        }

        var latest = questions
            .OrderByDescending(x => x.Timestamp)
            .Select(x => x.Question)
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct()
            .Take(10)
            .ToList();

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            queries = latest
        });

        return response;
    }
}