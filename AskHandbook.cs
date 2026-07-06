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

            AIProjectClient projectClient = new(
                endpoint: new Uri(endpoint),
                tokenProvider: new DefaultAzureCredential());

            AgentReference agentReference = new(
                name: agentName,
                version: agentVersion);

            ProjectResponsesClient responseClient =
                projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);

            ResponseResult response =
                responseClient.CreateResponse("Hello! Tell me a joke.");

            var res = req.CreateResponse(HttpStatusCode.OK);
            await res.WriteStringAsync(response.GetOutputText());

            return res;
        }
    }
}