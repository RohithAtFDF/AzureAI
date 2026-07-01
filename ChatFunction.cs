using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using System.Net;

public class ChatFunction
{
    [Function("chat")]
    public HttpResponseData Run(
        [HttpTrigger(AuthorizationLevel.Function, "post")]
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);

        try
        {
            var searchEndpoint = "https://cisaisearchservice.search.windows.net";
            var indexName = "rag-1782757069469";
            var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

            if (string.IsNullOrEmpty(searchKey))
            {
                response.WriteString("SEARCH_KEY is null or empty. Check Function App environment variables.");
                return response;
            }

            var client = new SearchClient(
                new Uri(searchEndpoint),
                indexName,
                new AzureKeyCredential(searchKey)
            );

            SearchResults<SearchDocument> results =
                client.Search<SearchDocument>("stop sign");

            foreach (SearchResult<SearchDocument> result in results.GetResults())
            {
                response.WriteString(result.Document.ToString());
                return response;
            }

            response.WriteString("Search worked, but no results were found.");
            return response;
        }
        catch (Exception ex)
        {
            response.StatusCode = HttpStatusCode.InternalServerError;
            response.WriteString(ex.ToString());
            return response;
        }
    }
}