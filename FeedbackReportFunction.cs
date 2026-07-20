using System.Net;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class FeedbackReportFunction
{
    [Function("FeedbackReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "feedback/report")]
        HttpRequestData req)
    {
        TableClient tableClient = await GetTableClientAsync();

        List<object> results = new();

        await foreach (ChatFeedbackEntity entity in tableClient.QueryAsync<ChatFeedbackEntity>())
        {
            results.Add(new
            {
                entity.CreatedUtc,
                entity.Question,
                entity.FeedbackStatus,
                entity.Program,
                entity.SourceTitles
            });
        }

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(results);

        return response;
    }

    private static async Task<TableClient> GetTableClientAsync()
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

        string tableName =
            Environment.GetEnvironmentVariable(
                "FEEDBACK_TABLE_NAME")
            ?? "ChatFeedback";

        var tableClient =
            new TableClient(
                connectionString,
                tableName);

        await tableClient.CreateIfNotExistsAsync();

        return tableClient;
    }
}