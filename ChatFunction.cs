using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Azure.Identity;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using OpenAI.Responses;
using System.Text.RegularExpressions;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;

/// step 1: router (nano) -> step 2: search -> step 3: answer (mini)
/// step 2: search results are used to build context for the answer agent, and also returned as sources for citation buttons in the UI
/// step 3: answer agent returns the final answer, which is returned to the user along with the sources
/// step 4: feedback is collected via a separate endpoint (FeedbackFunction.cs) and stored in Azure Table Storage for later analysis
/// step 5: feedback can be used to improve the search index and the answer agent over time
/// step 6: the router agent can be improved to better classify questions and route them to the appropriate agent (small talk vs search)
/// step 7: the answer agent can be improved to better use the context and provide more accurate and helpful answers
/// step 8: the search index can be improved to provide more relevant results and better context for the answer agent

public class ChatFunction
{
    private const string AgentEndpoint =
        "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

    private const string AnswerAgentName = "BCFS-Agent";
    private const string AnswerAgentVersion = "9";

    private const string RouterAgentName = "BCFS-Query-Agent";
    private const string RouterAgentVersion = "3";

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")]
        HttpRequestData req)
    {
        var stopwatch = Stopwatch.StartNew();
        ///number of documents in the current directory and subdirectories
        int documentCount = Directory.GetFiles(
            Directory.GetCurrentDirectory(),
            "*.pdf",
            SearchOption.AllDirectories
        ).Length;

        Console.WriteLine($"Document count: {documentCount}");

        var response = req.CreateResponse(HttpStatusCode.OK);
        // ★ CHANGED: default to JSON now (was text/plain)

        try
        {
            // -----------------------------
            // Read Request
            // -----------------------------
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await WriteJson(response, "Error: Request body was empty.", null, "error");
                return response;
            }

            string question = string.Empty;
            string userName = string.Empty;
            string email = string.Empty;

            try
            {
                using JsonDocument json = JsonDocument.Parse(requestBody);
                JsonElement root = json.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    question = GetJsonString(root, "question");
                    userName = GetJsonString(root, "userName");
                    if (string.IsNullOrWhiteSpace(userName))
                    {
                        userName = GetJsonString(root, "username");
                    }

                    email = GetJsonString(root, "email");
                    if (string.IsNullOrWhiteSpace(email))
                    {
                        email = GetJsonString(root, "userEmail");
                        if (string.IsNullOrWhiteSpace(email))
                        {
                            email = GetJsonString(root, "user_email");
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore parse failures here; we still validate question below.
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                userName = GetAuthenticatedUserName(req);
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                email = GetAuthenticatedUserEmail(req);
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                await WriteJson(response, "Error: 'question' was missing or empty.", null, "error");
                return response;
            }

            // -----------------------------
            // Shared Foundry Project Client
            // -----------------------------
            AIProjectClient projectClient = new(
                endpoint: new Uri(AgentEndpoint),
                tokenProvider: new DefaultAzureCredential()
            );

            // =========================================================
            // STEP 1: ROUTER (Nano)
            // =========================================================
            var routerClient = projectClient.OpenAI
                .GetProjectResponsesClientForAgent(
                    new AgentReference(RouterAgentName, RouterAgentVersion));

            ResponseResult routerResponse = routerClient.CreateResponse(question);
            string routerRaw = routerResponse.GetOutputText();

            RouterDecision decision = ParseRouterDecision(routerRaw);

            // -----------------------------
            // STEP 1a: SMALL TALK
            // -----------------------------
            if (decision != null && !decision.NeedsSearch)
            {
                stopwatch.Stop();

                string reply = string.IsNullOrWhiteSpace(decision.Reply)
                    ? "Hi! How can I help you with BCFS programs today?"
                    : decision.Reply;

                // ★ CHANGED: return JSON (empty sources for small talk)
                await WriteJson(response, reply, new List<object>(), "small-talk",
                                stopwatch.ElapsedMilliseconds);
                return response;
            }

            string searchQuery =
                (decision != null && !string.IsNullOrWhiteSpace(decision.SearchQuery))
                    ? decision.SearchQuery
                    : question;

            // =========================================================
            // STEP 2: Azure AI Search
            // =========================================================
            string searchEndpoint = "https://cisaisearchservice.search.windows.net";
            string indexName = "bcfs-manual-indexer";
            string? searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrWhiteSpace(searchKey))
            {
                await WriteJson(response, "SEARCH_KEY is missing.", null, "error");
                return response;
            }

            var searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(searchKey));

            // =========================================================
            // ★ CHANGED: Match Search Explorer behavior
            //   - Semantic ranking (uses your semantic configuration)
            //   - Vector query against content_embedding
            //   - Top 20 instead of 10
            //   - Explicit Select so we always get the fields we use
            // =========================================================
            var searchOptions = new SearchOptions
            {
                Size = 20,
                QueryType = SearchQueryType.Semantic,
                SemanticSearch = new SemanticSearchOptions
                {
                    SemanticConfigurationName = "bcfs-manual-indexer-semantic-configuration"
                }
            };

            // Fields we actually read below
            searchOptions.Select.Add("document_title");
            searchOptions.Select.Add("content_text");
            searchOptions.Select.Add("content_path");

            // ★ Vector query (text-to-vector). This mirrors the "kind": "text"
            //   vectorQuery you ran in Search Explorer.
            searchOptions.VectorSearch = new VectorSearchOptions();
            searchOptions.VectorSearch.Queries.Add(
                new VectorizableTextQuery(searchQuery)
                {
                    KNearestNeighborsCount = 20,
                    Fields = { "content_embedding" }
                });

            Console.WriteLine($"Search Query: {searchQuery}");

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(searchQuery, searchOptions);

            var results = searchResults.GetResults().ToList();

            double topScore = results.Any()
                ? results.Max(r => r.Score ?? 0.0)
                : 0.0;

            // -----------------------------
            // Build Context + Sources
            // ★ CHANGED: also collect sources for citations
            // -----------------------------
            StringBuilder contextBuilder = new();
            var sources = new List<object>();

            var seenDocumentTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string firstManualTitle = string.Empty;

            foreach (var result in results)
            {
                // ★ CHANGED: log the reranker score so we can see ranking in logs
                Console.WriteLine($"RerankerScore: {result.SemanticSearch?.RerankerScore}");
                Console.WriteLine(result.Document["document_title"]);
                Console.WriteLine(result.Document["content_text"]);  // sample testing retrieval output

                if (result.Document.TryGetValue(
                        "content_text",
                        out var text))
                {
                    string chunk =
                        text?.ToString() ?? "";

                    // Keep every chunk in the model context.
                    contextBuilder.AppendLine(chunk);
                    contextBuilder.AppendLine();

                    string title =
                        result.Document.TryGetValue(
                            "document_title",
                            out var t)
                            ? t?.ToString() ?? "Document"
                            : "Document";

                    string path =
                        result.Document.TryGetValue(
                            "content_path",
                            out var p)
                            ? p?.ToString() ?? ""
                            : "";

                    int pageNumber =
                        ExtractPageNumber(chunk) ?? 1;

                    /*
                    * Only add the first/highest-ranked chunk
                    * from each unique PDF as a citation button.
                    */
                    if (seenDocumentTitles.Add(title))
                    {
                        if (string.IsNullOrWhiteSpace(firstManualTitle))
                        {
                            firstManualTitle = !string.IsNullOrWhiteSpace(title)
                                ? title
                                : path;
                        }

                        sources.Add(new
                        {
                            title = title,
                            path = path,
                            pageNumber = pageNumber,
                            snippet = chunk.Length > 200
                                ? chunk.Substring(0, 200)
                                : chunk
                        });
                    }
                }
            }

            string context = contextBuilder.ToString();
            bool hasContext = !string.IsNullOrWhiteSpace(context);

            // =========================================================
            // STEP 3: ANSWER (Mini)
            // ★ CHANGED: cleaned-up prompt (removed stray line numbers)
            // =========================================================

            string prompt = $@"
                HANDBOOK CONTEXT:
                    {(hasContext ? context : "No relevant excerpts found.")}

                USER QUESTION:
                {question}";

            var answerClient = projectClient.OpenAI
                .GetProjectResponsesClientForAgent(
                    new AgentReference(AnswerAgentName, AnswerAgentVersion));

            ResponseResult agentResponse = answerClient.CreateResponse(prompt);

            stopwatch.Stop();
            string answer = agentResponse.GetOutputText();
            long responseTimeMs = stopwatch.ElapsedMilliseconds;
            int searchResultCount = results.Count;
            bool hadCitations = sources.Count > 0;
            string? questionCategory = decision?.QuestionCategory;

            // Save only AI Search questions and answers.
            // Small talk returns earlier, so it never reaches this code.
            FeedbackRecordReference? feedbackRecord = null;

            try
            {
                feedbackRecord =
                    await FeedbackFunction.SaveSearchResponseAsync(
                        question: question,
                        answer: answer,
                        sourceTitles: seenDocumentTitles,
                        program: firstManualTitle,
                        userName: userName,
                        email: email,
                        searchResultCount: searchResultCount,
                        topScore: topScore,
                        hadCitations: hadCitations,
                        responseTimeMs: responseTimeMs,
                        questionCategory: questionCategory
                    );
            }
            catch (Exception feedbackException)
            {
                // Feedback storage should never break the chatbot response.
                Console.WriteLine(
                    "Unable to save feedback record: " +
                    feedbackException.Message
                );
            }

            // Return the answer, sources, and feedback identifiers.
            await WriteJson(
                response,
                answer,
                sources,
                "search",
                responseTimeMs,
                searchQuery,
                searchResultCount,
                topScore,
                hadCitations,
                questionCategory,
                feedbackRecord?.ResponseId,
                feedbackRecord?.PartitionKey,
                feedbackRecord?.FeedbackStatus
            );

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            response.StatusCode = HttpStatusCode.InternalServerError;

            // ★ CHANGED: error as JSON so frontend .json() won't break
            await WriteJson(response, "Sorry, something went wrong. " + ex.Message,
                            null, "error", stopwatch.ElapsedMilliseconds);
            return response;
        }
    }

    // ★ NEW helper: writes the standard JSON envelope
        private static async Task WriteJson(
            HttpResponseData response,
            string answer,
            List<object>? sources,
            string path,
            long executionMs = 0,
            string rewrittenQuery = "",
            int searchResults = 0,
            double topScore = 0.0,
            bool hadCitations = false,
            string? questionCategory = null,
            string? feedbackId = null,
            string? feedbackPartition = null,
            string? feedbackStatus = null)
        {
            bool didSearch =
                string.Equals(
                    path,
                    "search",
                    StringComparison.OrdinalIgnoreCase
                );

            string payload =
                JsonSerializer.Serialize(
                    new
                    {
                        answer = answer,

                        sources =
                            sources ?? new List<object>(),

                        didSearch = didSearch,

                        feedbackId = feedbackId,

                        feedbackPartition =
                            feedbackPartition,

                        feedbackStatus =
                            feedbackStatus,

                        debug = new
                        {
                            path = path,

                            rewrittenQuery =
                                rewrittenQuery,

                            searchResults =
                                searchResults,

                            topScore = topScore,

                            hadCitations = hadCitations,

                            questionCategory = questionCategory,

                            executionMs =
                                executionMs
                        }
                    }
                );

            response.Headers.Add(
                "Content-Type",
                "application/json; charset=utf-8"
            );

            await response.WriteStringAsync(payload);
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            return string.Empty;
        }

    private static int? ExtractPageNumber(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

    Match match = Regex.Match(
        text,
        @"\bPage\s+(\d+)\s+of\s+\d+\b",
        RegexOptions.IgnoreCase
    );

    if (match.Success &&
        int.TryParse(match.Groups[1].Value, out int pageNumber) &&
        pageNumber > 0)
    {
        return pageNumber;
    }

    return null;
}


    // -----------------------------
    // Router JSON parsing 
    // -----------------------------
    private static RouterDecision? ParseRouterDecision(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        string cleaned = raw.Trim();
        if (cleaned.StartsWith("```"))
        {
            int firstBrace = cleaned.IndexOf('{');
            int lastBrace = cleaned.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        try
        {
            return JsonSerializer.Deserialize<RouterDecision>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return new RouterDecision { NeedsSearch = true, SearchQuery = null };
        }
    }

    private class RouterDecision
    {
        [JsonPropertyName("needsSearch")]
        public bool NeedsSearch { get; set; }

        [JsonPropertyName("reply")]
        public string? Reply { get; set; }

        [JsonPropertyName("searchQuery")]
        public string? SearchQuery { get; set; }

        [JsonPropertyName("questionCategory")]
        public string? QuestionCategory { get; set; }
    }
}