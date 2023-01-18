namespace kasthack.ApostlePeter.Models.Vk;

using System;
using System.Linq;

// https://github.com/kasthack-archive/kasthack.vksharp/blob/develop/Sources/kasthack.vksharp/Shared/Token.cs
public record Token(string Value, string? Sign = default, int UserId = 0)
{
    public static string GetOauthUrl(int appId, string privileges, string redirectUri) => string.Format(
                    "https://oauth.vk.com/authorize?client_id={0}&scope={1}&redirect_uri={2}&response_type=token",
                    appId,
                    privileges,
                    Uri.EscapeDataString(redirectUri));

    public static Token FromRedirectUrl(string url)
    {
        const string accessTokenPn = "access_token";
        const string signPn = "secret";
        const string useridPn = "user_id";
        const string errorPn = "error";
        const string errorRPn = "error_reason";
        const string errorDescPn = "error_description";
        var query = new Uri(url)
            .Fragment
            .TrimStart('#')
            .Split('&')
            .Select(a => a.Split('='))
            .Where(a => a.Length == 2)
            .GroupBy(a => a[0])
            .ToDictionary(a => a.Key, a => a.First()[1]);
        if (query.TryGetValue(accessTokenPn, out var accessToken))
        {
            return new Token(
                accessToken,
                query.TryGetValue(signPn, out var sign) ? sign : string.Empty,
                int.Parse(query[useridPn]));
        }

        if (query.TryGetValue(errorPn, out var errorText))
        {
            throw new Exception($"Error: {errorText}\r\nType:{query[errorRPn]}\r\nMessage:{query[errorDescPn].Replace('+', ' ')}");
        }

        throw new FormatException("Can't parse VK response from URL");
    }
}