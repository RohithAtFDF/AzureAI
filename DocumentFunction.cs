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

            string? path = query["path"];

            if (string.IsNullOrWhiteSpace(path))
            {
                var badRequest =
                    req.CreateResponse(HttpStatusCode.BadRequest);

                await badRequest.WriteStringAsync(
                    "The document path is missing."
                );

                return badRequest;
            }

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
                    "Storage configuration is missing."
                );

                return configurationError;
            }

            // content_path may be a complete Blob URL or a blob-relative path.
            string blobName = GetBlobName(
                path,
                containerName
            );

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

    private static string GetBlobName(
        string contentPath,
        string containerName)
    {
        string decodedPath =
            Uri.UnescapeDataString(contentPath).Trim();

        // Handle a complete Blob URL.
        if (Uri.TryCreate(
            decodedPath,
            UriKind.Absolute,
            out Uri? uri))
        {
            string absolutePath =
                uri.AbsolutePath.TrimStart('/');

            string containerPrefix =
                containerName.Trim('/') + "/";

            if (absolutePath.StartsWith(
                containerPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Substring(
                    containerPrefix.Length
                );
            }

            return absolutePath;
        }

        // Handle container-relative paths.
        string normalized =
            decodedPath.Replace("\\", "/").TrimStart('/');

        string prefix =
            containerName.Trim('/') + "/";

        if (normalized.StartsWith(
            prefix,
            StringComparison.OrdinalIgnoreCase))
        {
            normalized =
                normalized.Substring(prefix.Length);
        }

        return normalized;
    }
}