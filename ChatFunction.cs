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
        [HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequestData req)
{
        const string endpoint = "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";
        const string agentName = "Texas-Driving-Handbook";
        const string agentVersion = "3";

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain; charset=utf-8");

        try
        {
            // -----------------------------
            // Read & Parse User Question
            // -----------------------------
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                await response.WriteStringAsync("Error: Inbound request body is completely empty.");
                return response;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
            string question = data != null && data.ContainsKey("question") ? data["question"] : "";

            if (string.IsNullOrWhiteSpace(question))
            {
                await response.WriteStringAsync("Error: The 'question' key was missing or empty in the JSON payload.");
                return response;
            }

            // -----------------------------
            // Azure AI Search Setup
            // -----------------------------
            var searchEndpoint = "https://cisaisearchservice.search.windows.net";
            var indexName = "bcfs-manual-indexer";
            var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrEmpty(searchKey))
            {
                await response.WriteStringAsync("SEARCH_KEY is null or empty. Check Function App settings.");
                return response;
            }

            var searchClient = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(searchKey)
            );

                // -----------------------------
                // Azure AI Search Options
                // -----------------------------
            var searchOptions = new SearchOptions
            {
                SearchMode = SearchMode.Any, 
                Size = 5 
            };

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(question, searchOptions);

            // -----------------------------
            // Build Context (Using 'content_text')
            // -----------------------------
            StringBuilder contextBuilder = new();

            foreach (var result in searchResults.GetResults())
            {
                if (result.Document.ContainsKey("content_text"))
                {
                    contextBuilder.AppendLine(result.Document["content_text"]?.ToString());
                    contextBuilder.AppendLine();
                }
            }

            string context = contextBuilder.ToString();

            if (string.IsNullOrWhiteSpace(context))
            {
                await response.WriteStringAsync("Search worked, but no matching text segments contained 'content_text'.");
                return response;
            }
            // -----------------------------
            // Create Prompt
            // -----------------------------
            string prompt = $@"
            You are a BCFS Assistant.

            CONTEXT:

            {context}

            USER QUESTION:

            {question}
            ";
            


            // -----------------------------
            // Foundry Agent
            // -----------------------------
            
            response.WriteString("Found search results. About to call agent.");

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
                    .GetProjectResponsesClientForAgent(
                        agentReference
                    );

            ResponseResult agentResponse =
                responseClient.CreateResponse(prompt);

            string answer =
                agentResponse.GetOutputText();

            response.WriteString(answer);

            return response;
        }
        catch (Exception ex)
        {
            response.StatusCode =
                HttpStatusCode.InternalServerError;

            response.WriteString(ex.ToString());

            return response;
        }
    }
}