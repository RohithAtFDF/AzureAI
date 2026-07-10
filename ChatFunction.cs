using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using OpenAI.Responses;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

#pragma warning disable OPENAI001

public class ChatFunction
{
    private const string AgentEndpoint =
        "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

    private const string AgentName = "BCFS-Agent";
    private const string AgentVersion = "3";

    [Function("chat")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "chat")]
        HttpRequestData req)
    {
        var stopwatch = Stopwatch.StartNew();

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

        try
        {
            // -----------------------------
            // Read Request
            // -----------------------------
            string requestBody =
                await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await response.WriteStringAsync(
                    "Error: Request body was empty."
                );
                return response;
            }

            var data =
                JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            string question =
                data != null &&
                data.TryGetValue("question", out var q)
                    ? q
                    : "";

            if (string.IsNullOrWhiteSpace(question))
            {
                await response.WriteStringAsync(
                    "Error: 'question' was missing or empty."
                );
                return response;
            }

            // -----------------------------
            // Azure AI Search
            // -----------------------------
            string searchEndpoint =
                "https://cisaisearchservice.search.windows.net";

            string indexName =
                "bcfs-manual-indexer";

            string? searchKey =
                Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrWhiteSpace(searchKey))
            {
                await response.WriteStringAsync(
                    "SEARCH_KEY is missing."
                );
                return response;
            }

            var searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(searchKey)
            );

            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.Any,
                Size = 5
            };

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(
                    question,
                    searchOptions
                );

            var results = searchResults.GetResults().ToList();

            if (results.Count == 0)
            {
                await response.WriteStringAsync(
                    $"No search results found for '{question}'."
                );
                return response;
            }

            // -----------------------------
            // Build Context
            // -----------------------------
            StringBuilder contextBuilder = new();

            foreach (var result in results)
            {
                if (result.Document.TryGetValue("content_text", out var text))
                {
                    contextBuilder.AppendLine(text?.ToString());
                    contextBuilder.AppendLine();
                }
            }

            string context = contextBuilder.ToString();

            if (string.IsNullOrWhiteSpace(context))
            {
                await response.WriteStringAsync(
                    "Search returned documents, but none contained 'content_text'."
                );
                return response;
            }

            // -----------------------------
            // Build Prompt
            // -----------------------------
            string prompt = $@"
            You are a BCFS Assistant.

            Answer ONLY using the handbook context below.

            If the answer is not contained in the handbook, say that the handbook does not contain that information.

            -------------------------
            HANDBOOK CONTEXT
            -------------------------

            {context}

            -------------------------
            USER QUESTION
            -------------------------

            {question}
            ";

            // -----------------------------
            // Azure AI Foundry Agent
            // -----------------------------
            AIProjectClient projectClient =
                new(
                    endpoint: new Uri(AgentEndpoint),
                    tokenProvider: new DefaultAzureCredential()
                );

            AgentReference agentReference =
                new(
                    name: AgentName,
                    version: AgentVersion
                );

            ProjectResponsesClient responseClient =
                projectClient.OpenAI
                    .GetProjectResponsesClientForAgent(agentReference);

            ResponseResult agentResponse =
                responseClient.CreateResponse(prompt);

            stopwatch.Stop();

            string answer =
                agentResponse.GetOutputText();

            answer +=
                $"\n\n--------------------------------------------------" +
                $"\nSearch Results: {results.Count}" +
                $"\nExecution Time: {stopwatch.ElapsedMilliseconds} ms";

            await response.WriteStringAsync(answer);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            response.StatusCode =
                HttpStatusCode.InternalServerError;

            await response.WriteStringAsync(
                ex.ToString() +
                $"\n\nExecution Time: {stopwatch.ElapsedMilliseconds} ms"
            );

            return response;
        }
    }
}