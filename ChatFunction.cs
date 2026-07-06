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

#pragma warning disable OPENAI001

public class ChatFunction
{
    private const string AgentEndpoint =
        "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

    private const string AgentName = "Texas-Driving-Handbook";
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
            string question =
                await new StreamReader(req.Body).ReadToEndAsync();

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
                "texas-driver-manual-txt-format";

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

            SearchResults<SearchDocument> searchResults =
                searchClient.Search<SearchDocument>(question);

            // -----------------------------
            // Build Context
            // -----------------------------
            StringBuilder contextBuilder = new();

            foreach (var result in searchResults
                         .GetResults()
                         .Take(5))
            {
                if (result.Document.ContainsKey("chunk"))
                {
                    contextBuilder.AppendLine(
                        result.Document["chunk"]?.ToString()
                    );

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

            // -----------------------------
            // Create Prompt
            // -----------------------------
            string prompt = $@"
            You are a Texas Driver Handbook Assistant.

            Rules:

            1. Answer using ONLY the provided context.
            2. Analyze the user's question carefully.
            3. If the answer is not contained in the context, respond exactly:
            'I could not find information related to this question in the available documents.'
            4. Do not make assumptions.
            5. Do not use outside knowledge.
            6. Do not perform web searches.

            CONTEXT:

            {context}

            USER QUESTION:

            {question}
            ";
            
            var key = Environment.GetEnvironmentVariable("FOUNDRY_AGENT_KEY");

            response.WriteString(
                string.IsNullOrEmpty(key)
                    ? "FOUNDRY_AGENT_KEY missing"
                    : $"FOUNDRY_AGENT_KEY exists. Length={key.Length}"
            );


            // -----------------------------
            // Foundry Agent
            // -----------------------------
            
            response.WriteString("Found search results. About to call agent.");

            var agentClient = new AgentClient(
                new Uri(AgentEndpoint),
                new AzureKeyCredential(key)
            );

            // Create a request to the agent
            var agentRequest = new AgentRequest(
                AgentName,
                AgentVersion,
                prompt
            );  
            // Send the request to the agent and get the response
            var agentResponse = await agentClient.GetResponseAsync(agentRequest);
            // Write the agent's response to the HTTP response
            response.WriteString(agentResponse.Content);
            
            // Return the response
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