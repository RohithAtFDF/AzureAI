using System;
using System.Net;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using OpenAI.Responses;

#pragma warning disable OPENAI001

namespace AzureAI
{
    public class AskHandbook
    {
        [Function("AskHandbook")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            const string endpoint = "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257/agents/Texas-Driving-Handbook/endpoint/protocols/openai/responses";
            const string agentName = "Texas-Driving-Handbook";
            const string agentVersion = "3";

        var res = req.CreateResponse(HttpStatusCode.OK);
        try
        {
            AIProjectClient projectClient = new(
                endpoint: new Uri(endpoint),
                tokenProvider: new DefaultAzureCredential());

            // --- TEST 1: Can we talk to the Azure AI Project? ---
            // This will attempt to list the workspace/project connections.
            // If this throws a 403, your Azure Function's Identity lacks permission 
            // to view the project itself (e.g., needs "Reader" or "Azure AI Developer" role).
            var connections = projectClient.Connections.GetConnections();
            // ----------------------------------------------------

            AgentReference agentReference = new(
                name: agentName,
                version: agentVersion);

            ProjectResponsesClient responseClient =
                projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);

            // --- TEST 2: Can we execute inference on the Agent? ---
            // If Test 1 passes but this line throws a 403, your identity has access to the project 
            // metadata, but lacks data-plane model invocation rights ("Azure AI Inference User" role).
            ResponseResult response = responseClient.CreateResponse("Hello");

            await res.WriteStringAsync(response.GetOutputText());
        }
        catch (Exception ex)
        {
            // This will print the full stack trace showing exactly which method threw the 403
            await res.WriteStringAsync(ex.ToString());
        }
            return res;
        }
    }
}