using System;
using System.Net;
using System.Threading.Tasks;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace AzureAI
{
    public class AskHandbook
    {
        [Function("AskHandbook")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            var res = req.CreateResponse(HttpStatusCode.OK);

            try
            {
                const string endpoint =
                    "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";

                await res.WriteStringAsync("Starting...\n");

                var credential = new DefaultAzureCredential();

                TokenRequestContext tokenContext =
                    new TokenRequestContext(
                        new[] { "https://ai.azure.com/.default" });

                var token = credential.GetToken(tokenContext);

                await res.WriteStringAsync($"Token OK. Length={token.Token.Length}\n");

                AIProjectClient projectClient = new(
                    new Uri(endpoint),
                    credential);

                await res.WriteStringAsync("Project Client Created\n");

                AgentReference agentReference = new(
                    "Texas-Driving-Handbook",
                    "3");

                await res.WriteStringAsync("Agent Reference Created\n");

                var responseClient =
                    projectClient.OpenAI.GetProjectResponsesClientForAgent(agentReference);

                await res.WriteStringAsync("Response Client Created\n");

                // Stop before CreateResponse
                await res.WriteStringAsync("Reached end successfully\n");
            }
            catch (Exception ex)
            {
                await res.WriteStringAsync("\nEXCEPTION:\n");
                await res.WriteStringAsync(ex.ToString());
            }

            return res;
        }
    }
}