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
                const string endpoint =
                    "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

                var client = new AIProjectClient(
                    new Uri(endpoint),
                    new DefaultAzureCredential());

                await res.WriteStringAsync("Project Client Created");
            }
            catch (Exception ex)
            {
                await res.WriteStringAsync(ex.ToString());
            }

            return res;

        }
    }
}