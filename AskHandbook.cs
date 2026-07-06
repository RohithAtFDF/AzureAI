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
            const string endpoint = "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";
            const string agentName = "Texas-Driving-Handbook";
            const string agentVersion = "3";

        var res = req.CreateResponse(HttpStatusCode.OK);
        try
        {
            AIProjectClient projectClient = new(
                endpoint: new Uri(endpoint),
                tokenProvider: new DefaultAzureCredential());

            // --- TEST 1: Can we talk to the Azure AI Project Hub? ---
            // This calls a control-plane / metadata endpoint. 
            // If this fails with 403, your Azure Function's Managed Identity lacks 
            // basic access to the Azure AI Project ("Azure AI Developer" / "Reader").
            var projectProperties = await projectClient.GetPropertiesAsync();
            // --------------------------------------------------------

            AgentReference agentReference = new(
                name: agentName,
                version: agentVersion);

            ProjectResponsesClient responseClient =
                projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);

            // --- TEST 2: Can we execute inference using the Agent? ---
            // If Test 1 passed but this fails with 403, your Managed Identity is authorized 
            // to view the project, but lacks data-plane permissions ("Azure AI Inference User").
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