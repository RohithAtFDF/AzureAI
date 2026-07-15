using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

public class DocumentFunction
{
    [Function("document")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(
            AuthorizationLevel.Anonymous,
            "get",
            Route = "document")]
        HttpRequestData req)
    {
        try
        {
            var query = System.Web.HttpUtility.ParseQueryString(
                req.Url.Query
            );

            string? fileName = query["name"];

            if (string.IsNullOrWhiteSpace(fileName))
            {
                var badRequest =
                    req.CreateResponse(HttpStatusCode.BadRequest);

                await badRequest.WriteStringAsync(
                    "The document name is missing."
                );

                return badRequest;
            }

            // Security: remove any folder supplied by the browser.
            fileName = Path.GetFileName(fileName);

            if (!fileName.EndsWith(
                    ".pdf",
                    StringComparison.OrdinalIgnoreCase))
            {
                var badRequest =
                    req.CreateResponse(HttpStatusCode.BadRequest);

                await badRequest.WriteStringAsync(
                    "Only PDF documents are supported."
                );

                return badRequest;
            }

            // Exact Blob virtual-folder path.
            string blobName = $"BCFS Manuals/{fileName}";

            var serviceUri = new Uri(
                $"https://{storageAccountName}.blob.core.windows.net"
            );

            var blobServiceClient = new BlobServiceClient(
                serviceUri,
                new DefaultAzureCredential()
            );

            BlobContainerClient containerClient =
                blobServiceClient.GetBlobContainerClient(
                    containerName
                );

            BlobClient blobClient =
                containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                var notFound =
                    req.CreateResponse(HttpStatusCode.NotFound);

                await notFound.WriteStringAsync(
                    $"PDF not found: {blobName}"
                );

                return notFound;
            }

            BlobDownloadStreamingResult download =
                await blobClient.DownloadStreamingAsync();

            var response =
                req.CreateResponse(HttpStatusCode.OK);

            response.Headers.Add(
                "Content-Type",
                "application/pdf"
            );

            // Allows the browser to render rather than force download.
            response.Headers.Add(
                "Content-Disposition",
                $"inline; filename=\"{Path.GetFileName(blobName)}\""
            );

            // The native browser PDF viewer may request byte ranges.
            response.Headers.Add(
                "Accept-Ranges",
                "bytes"
            );

            await download.Content.CopyToAsync(
                response.Body
            );

            return response;
        }
        catch (Exception ex)
        {
            var response =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError
                );

            await response.WriteStringAsync(
                $"Unable to retrieve the PDF: {ex.Message}"
            );

            return response;
        }
    }

}