using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Diagnostics; // Added for the Stopwatch timer
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

#pragma warning disable OPENAI001

namespace AzureAI
{
    public class ChatFunction
    {
        [Function("ChatFunction")]
        public async Task<HttpResponseData> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req)
        {
            // Start the execution timer immediately
            var stopwatch = Stopwatch.StartNew();

            const string endpoint = "https://rr0076-0257-resource.services.ai.azure.com/api/projects/rr0076-0257";
            const string agentName = "Texas-Driving-Handbook";
            const string agentVersion = "3";

            var res = req.CreateResponse(HttpStatusCode.OK);
            res.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            try
            {
                // -----------------------------
                // Read & Parse User Question
                // -----------------------------
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    stopwatch.Stop();
                    await res.WriteStringAsync($"Error: Inbound request body is completely empty.\n⏱️ Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
                    return res;
                }

                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
                string question = data != null && data.ContainsKey("question") ? data["question"] : "";

                if (string.IsNullOrWhiteSpace(question))
                {
                    stopwatch.Stop();
                    await res.WriteStringAsync($"Error: The 'question' key was missing or empty in the JSON payload.\n⏱️ Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
                    return res;
                }

                // -----------------------------
                // Azure AI Search Setup
                // -----------------------------
                var searchEndpoint = "https://cisaisearchservice.search.windows.net";
                var indexName = "bcfs-manual-indexer";
                var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

                if (string.IsNullOrEmpty(searchKey))
                {
                    stopwatch.Stop();
                    await res.WriteStringAsync($"SEARCH_KEY is null or empty. Check Function App settings.\n⏱️ Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
                    return res;
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
                    stopwatch.Stop();
                    await res.WriteStringAsync($"Search worked, but no matching text segments contained 'content_text'.\n⏱️ Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
                    return res;
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

                // Stop the timer right before finalizing the output
                stopwatch.Stop();

                // Append the time performance metric to the final string
                string finalOutput = prompt + $"\n--------------------------------------------------\n⏱️ Total Execution Time: {stopwatch.ElapsedMilliseconds} ms";
                
                await res.WriteStringAsync(finalOutput);
            }
            catch (JsonException)
            {
                stopwatch.Stop();
                await res.WriteStringAsync($"Error: Inbound payload is not valid JSON. Ensure keys and values are enclosed in double quotes.\n⏱️ Time Elapsed: {stopwatch.ElapsedMilliseconds} ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                await res.WriteStringAsync($"{ex}\n\n⏱️ Execution Failed After: {stopwatch.ElapsedMilliseconds} ms");
            }

            return res;
        }
    }
}