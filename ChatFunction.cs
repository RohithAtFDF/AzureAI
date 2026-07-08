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
        var response = req.CreateResponse(HttpStatusCode.OK);

        try
        {
            // -----------------------------
            // Read User Question
            // -----------------------------
            // 1. Read the raw request body text (make sure 'req' matches your function's input parameter name)
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

            // 2. Safely parse the JSON into a dictionary
            var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            // 3. Assign the value to your EXISTING question variable (DO NOT put 'string' in front of it)
            string question = data != null && data.ContainsKey("question") ? data["question"] : "";

            if (string.IsNullOrWhiteSpace(question))
            {
                response.WriteString("Question is empty.");
                return response;
            }

            // -----------------------------
            // Azure AI Search
            // -----------------------------
            var searchEndpoint =
                "https://cisaisearchservice.search.windows.net";

            var indexName =
                "bcfs-manual-indexer";

            var searchKey =
                Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrEmpty(searchKey))
            {
                response.WriteString(
                    "SEARCH_KEY is null or empty. Check Function App settings."
                );
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
                // Ensures the engine looks for ANY of the words, preventing full sentences from returning null
                SearchMode = SearchMode.Any, 
                
                // Performance Optimization: Limits results to 5 on the server side 
                // so you don't pull down extra data before doing .Take(5)
                Size = 5 
            };

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(question, searchOptions);

            // -----------------------------
            // Build Context
            // -----------------------------
            StringBuilder contextBuilder = new();

            // Streamlined foreach since we already limited the size to 5 in searchOptions
            foreach (var result in searchResults.GetResults())
            {
                if (result.Document.ContainsKey("chunk"))
                {
                    contextBuilder.AppendLine(result.Document["chunk"]?.ToString());
                    contextBuilder.AppendLine();
                }
            }

            string context = contextBuilder.ToString();

            if (string.IsNullOrWhiteSpace(context))
            {
                response.WriteString(
                    "Search worked, but no results were found."
                );
                return response;
            }


            //  debug lines  


            var debugResults = searchResults.GetResults().ToList();

            if (debugResults.Count == 0)
            {
                response.WriteString($"Debug: Azure AI Search returned exactly 0 documents for query: '{question}'");
                return response;
            }
            else 
            {
                var firstDocKeys = string.Join(", ", debugResults[0].Document.Keys);
                response.WriteString($"Debug: Success! Found {debugResults.Count} docs. However, your index fields are: [{firstDocKeys}]. Checking for 'chunk' might be failing.");
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