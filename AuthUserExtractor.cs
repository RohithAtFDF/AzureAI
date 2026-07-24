using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;

public static class AuthUserExtractor
{
    public static AuthIdentity? GetUser(HttpRequestData req)
    {
        string principalId =
            GetHeader(req, "X-MS-CLIENT-PRINCIPAL-ID");

        string principalName =
            GetHeader(req, "X-MS-CLIENT-PRINCIPAL-NAME");

        ClientPrincipal? principal = GetClientPrincipal(req);

        if (principal == null &&
            string.IsNullOrWhiteSpace(principalId) &&
            string.IsNullOrWhiteSpace(principalName))
        {
            return null;
        }

        string userName = GetClaimValue(
            principal,
            "name",
            "displayName",
            "given_name",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
            "http://schemas.microsoft.com/identity/claims/displayname");

        string email = GetClaimValue(
            principal,
            "preferred_username",
            "email",
            "emails",
            "upn",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn");

        string userDetails =
            principal?.UserDetails ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email))
        {
            email = !string.IsNullOrWhiteSpace(principalName)
                ? principalName
                : userDetails;
        }

        if (string.IsNullOrWhiteSpace(userName) &&
            !string.IsNullOrWhiteSpace(userDetails) &&
            !IsEmailLike(userDetails))
        {
            userName = userDetails;
        }

        if (string.IsNullOrWhiteSpace(userName) &&
            !string.IsNullOrWhiteSpace(principalName) &&
            !IsEmailLike(principalName))
        {
            userName = principalName;
        }

        userName ??= string.Empty;

        string userId =
            !string.IsNullOrWhiteSpace(principalId)
                ? principalId
                : principal?.UserId
                    ?? principal?.PrincipalId
                    ?? string.Empty;

        return new AuthIdentity
        {
            UserId = userId,
            UserName = userName,
            Email = email
        };
    }

    public static string GetDebugSummary(HttpRequestData req)
    {
        bool hasPrincipal =
            req.Headers.Contains("X-MS-CLIENT-PRINCIPAL");

        bool hasPrincipalId =
            req.Headers.Contains("X-MS-CLIENT-PRINCIPAL-ID");

        bool hasPrincipalName =
            req.Headers.Contains("X-MS-CLIENT-PRINCIPAL-NAME");

        bool hasIdentityProvider =
            req.Headers.Contains("X-MS-CLIENT-PRINCIPAL-IDP");

        return
            $"PrincipalHeader={hasPrincipal}, " +
            $"PrincipalIdHeader={hasPrincipalId}, " +
            $"PrincipalNameHeader={hasPrincipalName}, " +
            $"IdentityProviderHeader={hasIdentityProvider}";
    }

    private static ClientPrincipal? GetClientPrincipal(
        HttpRequestData req)
    {
        string encoded =
            GetHeader(req, "X-MS-CLIENT-PRINCIPAL");

        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            byte[] bytes = Convert.FromBase64String(encoded);
            string json = Encoding.UTF8.GetString(bytes);

            return JsonSerializer.Deserialize<ClientPrincipal>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }
        catch
        {
            return null;
        }
    }

    private static string GetHeader(
        HttpRequestData req,
        string headerName)
    {
        return req.Headers.TryGetValues(
            headerName,
            out IEnumerable<string>? values)
                ? values.FirstOrDefault() ?? string.Empty
                : string.Empty;
    }

    private static string GetClaimValue(
        ClientPrincipal? principal,
        params string[] claimTypes)
    {
        if (principal?.Claims == null)
        {
            return string.Empty;
        }

        foreach (string type in claimTypes)
        {
            ClientClaim? claim =
                principal.Claims.FirstOrDefault(
                    c => string.Equals(
                        c.Type,
                        type,
                        StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(claim?.Value))
            {
                return claim.Value;
            }
        }

        return string.Empty;
    }

    private static bool IsEmailLike(string value)
    {
        return value.Contains("@") || value.Contains("\\\\");
    }
}

public class AuthIdentity
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class ClientPrincipal
{
    // Static Web Apps format

    [JsonPropertyName("identityProvider")]
    public string? IdentityProvider { get; set; }

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("userDetails")]
    public string? UserDetails { get; set; }

    [JsonPropertyName("userRoles")]
    public List<string>? UserRoles { get; set; }

    // App Service Easy Auth format

    [JsonPropertyName("auth_typ")]
    public string? AuthType { get; set; }

    [JsonPropertyName("claims")]
    public List<ClientClaim>? Claims { get; set; }

    [JsonPropertyName("name_typ")]
    public string? NameType { get; set; }

    [JsonPropertyName("role_typ")]
    public string? RoleType { get; set; }

    [JsonPropertyName("principal_id")]
    public string? PrincipalId { get; set; }
}

public class ClientClaim
{
    [JsonPropertyName("typ")]
    public string? Type { get; set; }

    [JsonPropertyName("val")]
    public string? Value { get; set; }
}