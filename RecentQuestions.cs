using System.Collections.Generic;
using System.Linq;
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
        var response = req.CreateResponse(HttpStatusCode.OK);

        var allQuestions = new List<dynamic>();
        var authIdentity = AuthUserExtractor.GetUser(req);

        var email = authIdentity?.Email ?? string.Empty;
        var recentQuestions = new List<dynamic>();

        try
        {
            var filter = $"Email eq '{email}'";

            await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter))
            {
                if (entity.TryGetValue("Question", out var question))
                {
                    allQuestions.Add(new
                    {
                        Question = question?.ToString(),
                        CreatedUtc = entity.TryGetValue("CreatedUtc", out var createdUtc)
                            ? createdUtc
                            : null
                    });
                }
            }

            recentQuestions = allQuestions
                .OrderByDescending(q => q.CreatedUtc)
                .Take(3)
                .ToList();

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to read feedback table: {ex.Message}");
        }

        await response.WriteAsJsonAsync(new
        {
            Count = recentQuestions.Count,
            Questions = recentQuestions,
            Email = email
        });

        return response;
    }
}