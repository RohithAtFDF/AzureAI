using System.Net;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class RecentQuestions
{
    private const string DefaultTableName = "ChatFeedback";

    private readonly TableClient _tableClient;

    public RecentQuestions()
    {
        string? connectionString =
            Environment.GetEnvironmentVariable(
                "FEEDBACK_STORAGE_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString =
                Environment.GetEnvironmentVariable(
                    "AzureWebJobsStorage");
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Feedback storage is not configured.");
        }

        string? configuredTableName =
            Environment.GetEnvironmentVariable(
                "FEEDBACK_TABLE_NAME");

        string tableName =
            string.IsNullOrWhiteSpace(configuredTableName)
                ? DefaultTableName
                : configuredTableName;

        _tableClient = new TableClient(
            connectionString,
            tableName);
    }

    [Function("RecentQuestions")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "recent-questions")]
        HttpRequestData req)
    {
        var user = AuthUserExtractor.GetUser(req);

        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            return req.CreateResponse(HttpStatusCode.Unauthorized);
        }

        Console.WriteLine($"User Email = {user.Email}");

        int count = 0;

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            count++;

            Console.WriteLine(
                $"PK={entity.PartitionKey}, RK={entity.RowKey}");

            if (entity.TryGetValue("Email", out var email))
            {
                Console.WriteLine($"Email={email}");
            }

            if (count == 5)
                break;
        }

        Console.WriteLine($"Entities found = {count}");

        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new
        {
            User = user.Email,
            Count = count
        });

        return response;
    }
}