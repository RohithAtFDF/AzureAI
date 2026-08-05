using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Azure.Data.Tables;

public class RecentQuestions
{
    private readonly TableServiceClient _tableServiceClient;

    public RecentQuestions(TableServiceClient tableServiceClient)
    {
        _tableServiceClient = tableServiceClient;
    }

    [FunctionName("RecentQuestions")]
    public async Task<IActionResult> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "recent-questions")]
        HttpRequest req)
    {
        var user = AuthUserExtractor.GetUser(req);

        var tableClient =
            _tableServiceClient.GetTableClient("ChatFeedback");

        var questions = new List<string>();

        await foreach (var entity in tableClient.QueryAsync<TableEntity>())
        {
            if (
                entity.TryGetValue("Email", out var emailObj) &&
                emailObj?.ToString()
                    ?.Equals(user.Email, StringComparison.OrdinalIgnoreCase) == true &&
                entity.TryGetValue("Question", out var questionObj)
            )
            {
                questions.Add(questionObj?.ToString() ?? "");
            }
        }

        var latest = questions
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Distinct()
            .TakeLast(10)
            .Reverse()
            .ToList();

        return new OkObjectResult(new
        {
            queries = latest
        });
    }
}