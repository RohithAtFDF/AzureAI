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
        var endpoint =
            new Uri("https://cisaisearchservice.search.windows.net");

        var indexName = "rag-1782757069469";


        var credential =
            new AzureKeyCredential("SEARCH_KEY");

        var client =
            new SearchClient(endpoint, indexName, credential);

        SearchResults<SearchDocument> results =
            client.Search<SearchDocument>("stop sign");

        var response =
            req.CreateResponse(HttpStatusCode.OK);

        foreach (SearchResult<SearchDocument> result in results.GetResults())
        {
            response.WriteString(
                result.Document["chunk"].ToString()
            );
            break;
        }

        return response;
    }
}