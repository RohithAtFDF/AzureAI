using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class FeedbackFunction
{
    private const string DefaultTableName = "ChatFeedback";

    private static readonly Lazy<Task<TableClient>> TableClientTask =
        new Lazy<Task<TableClient>>(CreateTableClientAsync);

    public static async Task<FeedbackRecordReference> SaveSearchResponseAsync(
        string question,
        string answer,
        IEnumerable<string> sourceTitles,
        string program = "")
    {
        TableClient tableClient = await GetTableClientAsync();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        string partitionKey = now.ToString("yyyyMM");
        string responseId = Guid.NewGuid().ToString("N");

        string[] uniqueSources = sourceTitles
            .Where(title => !string.IsNullOrWhiteSpace(title))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var entity = new ChatFeedbackEntity
        {
            PartitionKey = partitionKey,
            RowKey = responseId,
            Question = LimitText(question, 8000),
            Answer = LimitText(answer, 30000),
            FeedbackStatus = "none",
            SearchPerformed = true,
            CreatedUtc = now,
            FeedbackUpdatedUtc = null,
            SourceTitles = JsonSerializer.Serialize(uniqueSources),
            Program = LimitText(program, 500)
        };

        await tableClient.AddEntityAsync(entity);

        return new FeedbackRecordReference
        {
            ResponseId = responseId,
            PartitionKey = partitionKey,
            FeedbackStatus = "none"
        };
    }

    [Function("SaveChatFeedback")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "post",
            Route = "feedback")]
        HttpRequestData req)
    {
        try
        {
            string requestBody;

            using (var reader = new StreamReader(req.Body))
            {
                requestBody = await reader.ReadToEndAsync();
            }

            FeedbackRequest? request =
                JsonSerializer.Deserialize<FeedbackRequest>(
                    requestBody,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (request == null)
            {
                return await CreateJsonResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    new
                    {
                        error = "The feedback request is invalid."
                    });
            }

            if (string.IsNullOrWhiteSpace(request.ResponseId) ||
                string.IsNullOrWhiteSpace(request.PartitionKey))
            {
                return await CreateJsonResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    new
                    {
                        error =
                            "ResponseId and PartitionKey are required."
                    });
            }

            string status = request.Status
                .Trim()
                .ToLowerInvariant();

            bool validStatus =
                status == "none" ||
                status == "liked" ||
                status == "disliked";

            if (!validStatus)
            {
                return await CreateJsonResponseAsync(
                    req,
                    HttpStatusCode.BadRequest,
                    new
                    {
                        error =
                            "Status must be none, liked, or disliked."
                    });
            }

            TableClient tableClient =
                await GetTableClientAsync();

            var updateEntity = new TableEntity(
                request.PartitionKey,
                request.ResponseId)
            {
                ["FeedbackStatus"] = status,
                ["FeedbackUpdatedUtc"] =
                    DateTimeOffset.UtcNow
            };

            await tableClient.UpdateEntityAsync(
                updateEntity,
                ETag.All,
                TableUpdateMode.Merge);

            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.OK,
                new
                {
                    saved = true,
                    feedbackStatus = status
                });
        }
        catch (RequestFailedException ex)
            when (ex.Status == 404)
        {
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.NotFound,
                new
                {
                    error =
                        "The feedback record was not found."
                });
        }
        catch (JsonException)
        {
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.BadRequest,
                new
                {
                    error =
                        "The feedback request body is not valid JSON."
                });
        }
        catch (Exception ex)
        {
            return await CreateJsonResponseAsync(
                req,
                HttpStatusCode.InternalServerError,
                new
                {
                    error = "Unable to save feedback.",
                    detail = ex.Message
                });
        }
    }

    private static async Task<TableClient> CreateTableClientAsync()
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
                "Feedback storage is not configured. " +
                "Configure FEEDBACK_STORAGE_CONNECTION_STRING " +
                "or AzureWebJobsStorage.");
        }

        string? configuredTableName =
            Environment.GetEnvironmentVariable(
                "FEEDBACK_TABLE_NAME");

        string tableName =
            string.IsNullOrWhiteSpace(configuredTableName)
                ? DefaultTableName
                : configuredTableName;

        var tableClient = new TableClient(
            connectionString,
            tableName);

        await tableClient.CreateIfNotExistsAsync();

        return tableClient;
    }

    private static Task<TableClient> GetTableClientAsync()
    {
        return TableClientTask.Value;
    }

    private static string LimitText(
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= maximumLength)
        {
            return value;
        }

        return value.Substring(0, maximumLength);
    }

    private static async Task<HttpResponseData>
        CreateJsonResponseAsync(
            HttpRequestData req,
            HttpStatusCode statusCode,
            object payload)
    {
        HttpResponseData response =
            req.CreateResponse(statusCode);

        response.Headers.Add(
            "Content-Type",
            "application/json; charset=utf-8");

        string json =
            JsonSerializer.Serialize(payload);

        await response.WriteStringAsync(json);

        return response;
    }
}

public class ChatFeedbackEntity : ITableEntity
{
    public string PartitionKey { get; set; } =
        string.Empty;

    public string RowKey { get; set; } =
        string.Empty;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Question { get; set; } =
        string.Empty;

    public string Answer { get; set; } =
        string.Empty;

    public string FeedbackStatus { get; set; } =
        "none";

    public bool SearchPerformed { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset? FeedbackUpdatedUtc
    {
        get;
        set;
    }

    public string SourceTitles { get; set; } =
        "[]";

    public string Program { get; set; } =
        string.Empty;
}

public class FeedbackRequest
{
    public string ResponseId { get; set; } =
        string.Empty;

    public string PartitionKey { get; set; } =
        string.Empty;

    public string Status { get; set; } =
        string.Empty;
}

public class FeedbackRecordReference
{
    public string ResponseId { get; set; } =
        string.Empty;

    public string PartitionKey { get; set; } =
        string.Empty;

    public string FeedbackStatus { get; set; } =
        "none";
}