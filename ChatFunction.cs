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

public class ChatFunction
{
    private const string AgentEndpoint =
        "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

    private const string AnswerAgentName = "BCFS-Agent";
    private const string AnswerAgentVersion = "5";

    private const string RouterAgentName = "BCFS-Query-Agent";
    private const string RouterAgentVersion = "2";

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")]
        HttpRequestData req)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = req.CreateResponse(HttpStatusCode.OK);
        // ★ CHANGED: default to JSON now (was text/plain)
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");

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

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            string question =
                data != null && data.TryGetValue("question", out var q) ? q : "";

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

            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.Any,
                Size = 5
            };

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(searchQuery, searchOptions);

            var results = searchResults.GetResults().ToList();

            // -----------------------------
            // Build Context + Sources
            // ★ CHANGED: also collect sources for citations
            // -----------------------------
            StringBuilder contextBuilder = new();
            var sources = new List<object>();

            foreach (var result in results)
            {
                if (result.Document.TryGetValue("content_text", out var text))
                {
                    string chunk = text?.ToString() ?? "";
                    contextBuilder.AppendLine(chunk);
                    contextBuilder.AppendLine();

                    // ★ your real index fields: document_title + content_path
                    string title = result.Document.TryGetValue("document_title", out var t)
                        ? t?.ToString() ?? "Document"
                        : "Document";

                    string path = result.Document.TryGetValue("content_path", out var p)
                        ? p?.ToString() ?? ""
                        : "";

                    sources.Add(new
                    {
                        title = title,
                        path = path,
                        snippet = chunk.Length > 200 ? chunk.Substring(0, 200) : chunk
                    });
                }
            }

            string context = contextBuilder.ToString();
            bool hasContext = !string.IsNullOrWhiteSpace(context);

            // =========================================================
            // STEP 3: ANSWER (Mini)
            // ★ CHANGED: cleaned-up prompt (removed stray line numbers)
            // =========================================================
            string prompt =
$@"HANDBOOK CONTEXT:
{(hasContext ? context : "No relevant excerpts found.")}

USER QUESTION:
{question}";

            var answerClient = projectClient.OpenAI
                .GetProjectResponsesClientForAgent(
                    new AgentReference(AnswerAgentName, AnswerAgentVersion));

            ResponseResult agentResponse = answerClient.CreateResponse(prompt);

            stopwatch.Stop();

            string answer = agentResponse.GetOutputText();

            // ★ CHANGED: return JSON with answer + sources (no text footer)
            await WriteJson(response, answer, sources, "search",
                            stopwatch.ElapsedMilliseconds, searchQuery, results.Count);
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
        int searchResults = 0)
    {
        string payload = JsonSerializer.Serialize(new
        {
            answer = answer,
            sources = sources ?? new List<object>(),
            debug = new
            {
                path = path,
                rewrittenQuery = rewrittenQuery,
                searchResults = searchResults,
                executionMs = executionMs
            }
        });

        await response.WriteStringAsync(payload);
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
    }
}