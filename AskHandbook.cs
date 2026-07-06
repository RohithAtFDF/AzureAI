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


        try
        {
            AIProjectClient projectClient = new(
                endpoint: new Uri(endpoint),
                tokenProvider: new DefaultAzureCredential());

            AgentReference agentReference = new(
                name: "Texas-Driving-Handbook",
                version: "3");

            ProjectResponsesClient responseClient =
                projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);

            ResponseResult response =
                responseClient.CreateResponse("Hello");

            await res.WriteStringAsync(response.GetOutputText());
        }
        catch (Exception ex)
        {
            await res.WriteStringAsync(ex.ToString());
        }
        
            return res;
        }
    }
}