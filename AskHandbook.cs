using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

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
            res.Headers.Add("Content-Type", "text/plain; charset=utf-8");

            try
            {
                // -----------------------------
                // Read & Parse User Question
                // -----------------------------
                string requestBody = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(requestBody))
                {
                    await res.WriteStringAsync("Error: Inbound request body is completely empty.");
                    return res;
                }

                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
                string question = data != null && data.ContainsKey("question") ? data["question"] : "";

                if (string.IsNullOrWhiteSpace(question))
                {
                    await res.WriteStringAsync("Error: The 'question' key was missing or empty in the JSON payload.");
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
                    await res.WriteStringAsync("SEARCH_KEY is null or empty. Check Function App settings.");
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
                // Troubleshooting / Output Results
                // -----------------------------
                var resultsList = new List<SearchDocument>();
                foreach (var result in searchResults.GetResults())
                {
                    resultsList.Add(result.Document);
                }

                if (resultsList.Count == 0)
                {
                    await res.WriteStringAsync($"Debug: Azure AI Search executed successfully but returned 0 documents for the query: '{question}'");
                    return res;
                }

                // Document inspection dump
                var firstDoc = resultsList[0];
                var availableFields = string.Join(", ", firstDoc.Keys);

                string debugOutput = $"SUCCESS! Azure AI Search found {resultsList.Count} matching documents.\n";
                debugOutput += $"Actual field names discovered inside your index: [{availableFields}]\n";
                debugOutput += "--------------------------------------------------\n\n";

                int docIndex = 1;
                foreach (var doc in resultsList)
                {
                    debugOutput += $"[Document #{docIndex}]\n";
                    foreach (var key in doc.Keys)
                    {
                        var valueString = doc[key]?.ToString() ?? "null";
                        
                        // Truncate values slightly if they are massive chunks just for easy reading
                        if (valueString.Length > 250)
                        {
                            valueString = valueString.Substring(0, 250) + "... (truncated)";
                        }
                        
                        debugOutput += $"  -> {key}: {valueString}\n";
                    }
                    debugOutput += "\n";
                    docIndex++;
                }

                await res.WriteStringAsync(debugOutput);
            }
            catch (JsonException)
            {
                await res.WriteStringAsync("Error: Inbound payload is not valid JSON. Ensure keys and values are enclosed in double quotes.");
            }
            catch (Exception ex)
            {
                // This captures all search or connectivity exceptions completely
                await res.WriteStringAsync(ex.ToString());
            }

            return res;
        }
    }
}