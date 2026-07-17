using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

// this file is used to serve PDF documents from Azure Blob Storage. It is used by the plugin to retrieve documents for the user.

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
            // Expected request:
            // /api/document?name=DocumentName.pdf
            string? fileName = req.Query["name"];

            if (string.IsNullOrWhiteSpace(fileName))
            {
                var badRequest =
                    req.CreateResponse(HttpStatusCode.BadRequest);

                await badRequest.WriteStringAsync(
                    "The document name is missing."
                );

                return badRequest;
            }

            // Prevent callers from supplying arbitrary folder paths.
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
            
           /// to get number of documents. 
            int documentCount = Directory.GetFiles(
                Directory.GetCurrentDirectory(),
                "*.pdf",
                SearchOption.AllDirectories
            ).Length;

            // These values come from Function App environment variables.
            string? storageAccountName =
                Environment.GetEnvironmentVariable(
                    "STORAGE_ACCOUNT_NAME"
                );

            string? containerName =
                Environment.GetEnvironmentVariable(
                    "PDF_CONTAINER_NAME"
                );

            if (string.IsNullOrWhiteSpace(storageAccountName) ||
                string.IsNullOrWhiteSpace(containerName))
            {
                var configurationError =
                    req.CreateResponse(
                        HttpStatusCode.InternalServerError
                    );

                await configurationError.WriteStringAsync(
                    "Storage configuration is missing. Check " +
                    "STORAGE_ACCOUNT_NAME and PDF_CONTAINER_NAME."
                );

                return configurationError;
            }

            // Exact location inside the container:
            // container/BCFS Manuals/document.pdf
            string blobName =
                $"BCFS Manuals/{fileName}";

            var serviceUri = new Uri(
                $"https://{storageAccountName}.blob.core.windows.net"
            );

            var credential =
                new DefaultAzureCredential();

            var blobServiceClient =
                new BlobServiceClient(
                    serviceUri,
                    credential
                );

            BlobContainerClient containerClient =
                blobServiceClient.GetBlobContainerClient(
                    containerName
                );

            BlobClient blobClient =
                containerClient.GetBlobClient(blobName);

            bool exists =
                await blobClient.ExistsAsync();

            if (!exists)
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

            response.Headers.Add(
                "Content-Disposition",
                $"inline; filename=\"{fileName}\""
            );

            response.Headers.Add(
                "Cache-Control",
                "private, max-age=300"
            );

            await download.Content.CopyToAsync(
                response.Body
            );

            return response;
        }
        catch (Exception ex)
        {
            var errorResponse =
                req.CreateResponse(
                    HttpStatusCode.InternalServerError
                );

            await errorResponse.WriteStringAsync(
                $"Unable to retrieve the PDF: {ex.Message}"
            );

            return errorResponse;
        }
    }
}