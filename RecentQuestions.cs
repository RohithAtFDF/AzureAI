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

        var allQuestions = new List<object>();
        var authIdentity = AuthUserExtractor.GetUser(req);

        var email = authIdentity?.Email ?? string.Empty;
        Console.WriteLine($"Email retrieved from autheuserextractpr: {email}");

        try
        {
                    await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            Console.WriteLine("ENTITY FOUND");
        var filter = $"Email eq '{email}'";

        await foreach (var entity in _tableClient.QueryAsync<TableEntity>(filter))
            {
                if (entity.TryGetValue("Question", out var question))
                {
                    Console.WriteLine($"Question = {question}");
}

            }

            allQuestions.Add(entity);
        }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to read feedback table: {ex.Message}");
        }

        await response.WriteAsJsonAsync(new
        {
            Count = allQuestions.Count,
            Questions = allQuestions,
            Email = email
        });

        return response;
    }
}