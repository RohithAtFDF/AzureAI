using System.Net;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class FeedbackReportFunction
{
    [Function("FeedbackReport")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Function,
            "get",
            Route = "feedback/report")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);

        await response.WriteAsJsonAsync(new[]
        {
            new {
                message = "Feedback report endpoint works"
            }
        });

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