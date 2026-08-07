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

        try
        {
                    await foreach (var entity in _tableClient.QueryAsync<TableEntity>())
        {
            Console.WriteLine("ENTITY FOUND");

            foreach (var property in entity)
            {
                Console.WriteLine($"{property.Key} = {property.Value}");
                //output some queries
                // ...
                
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
            Questions = allQuestions
        });

        return response;
    }
}