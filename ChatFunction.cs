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
            var indexName = "texas-driver-manual-txt-format";
            var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY", EnvironmentVariableTarget.Process);

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

            var firstResult = results.GetResults().FirstOrDefault();

            if (firstResult == null)
            {
                response.WriteString("Search worked, but no results were found.");
                return response;
            }

            response.WriteString(
                firstResult.Document["chunk"].ToString()
            );

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