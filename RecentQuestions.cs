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
        Console.WriteLine(
            $"Storage: {Environment.GetEnvironmentVariable("AzureWebJobsStorage")}");

        Console.WriteLine(
            $"Table: ChatFeedback");

            
        var questions = new List<object>();

        var filter = $"Email eq '{user.Email}'";

        Console.WriteLine($"Filter = {filter}");

        int count = 0;

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            count++;

            Console.WriteLine(
                $"PK={entity.PartitionKey}, RK={entity.RowKey}");

            if (count == 5)
                break;
        }

        Console.WriteLine($"Entities found: {count}");

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
