using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker.Http;

public class AuthUserExtractor
{
    public static AuthIdentity? GetUser(HttpRequestData req)
    {
        var principal = GetClientPrincipal(req);
        if (principal == null)
        {
            return null;
        }

        string userName = GetClaimValue(principal, "name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name");
        if (string.IsNullOrWhiteSpace(userName))
        {
            userName = principal.userDetails ?? string.Empty;
        }

        string email = GetClaimValue(principal, "preferred_username", "email", "emails", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress");
        if (string.IsNullOrWhiteSpace(email))
        {
            email = principal.userDetails ?? string.Empty;
        }

        return new AuthIdentity
        {
            UserId = principal.userId ?? string.Empty,
            UserName = userName,
            Email = email
        };
    }

    private static ClientPrincipal? GetClientPrincipal(HttpRequestData req)
    {
        if (!req.Headers.TryGetValues("x-ms-client-principal", out var values))
        {
            return null;
        }

        string encoded = values.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return null;
        }

        try
        {
            byte[] decodedBytes = Convert.FromBase64String(encoded);
            string json = Encoding.UTF8.GetString(decodedBytes);
            return JsonSerializer.Deserialize<ClientPrincipal>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static string GetClaimValue(ClientPrincipal principal, params string[] claimTypes)
    {
        foreach (var type in claimTypes)
        {
            var claim = principal.claims?.FirstOrDefault(c => string.Equals(c.typ, type, StringComparison.OrdinalIgnoreCase));
            if (claim != null && !string.IsNullOrWhiteSpace(claim.val))
            {
                return claim.val!;
            }
        }

        return string.Empty;
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
    [JsonPropertyName("auth_typ")]
    public string? AuthType { get; set; }

    [JsonPropertyName("claims")]
    public List<ClientClaim>? claims { get; set; }

    [JsonPropertyName("name_type")]
    public string? NameType { get; set; }

    [JsonPropertyName("role_type")]
    public string? RoleType { get; set; }

    [JsonPropertyName("claims_version")]
    public string? ClaimsVersion { get; set; }

    [JsonPropertyName("principal_id")]
    public string? PrincipalId { get; set; }

    [JsonPropertyName("user_id")]
    public string? userId { get; set; }

    [JsonPropertyName("user_details")]
    public string? userDetails { get; set; }
}

public class ClientClaim
{
    [JsonPropertyName("typ")]
    public string? typ { get; set; }

    [JsonPropertyName("val")]
    public string? val { get; set; }
}
