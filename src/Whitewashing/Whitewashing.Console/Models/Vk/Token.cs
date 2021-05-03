namespace Whitewashing.Console.Models.Vk
{
    using System;
    using System.Linq;

    //https://github.com/kasthack-archive/kasthack.vksharp/blob/develop/Sources/kasthack.vksharp/Shared/Token.cs

    public class Token
    {
        public string Value { get; }
        public string Sign { get; }
        public int UserId { get; }

        public Token(string value, string sign = null, int userId = 0)
        {
            this.Value = value;
            this.Sign = sign;
            this.UserId = userId;
        }

        public static string GetOauthUrl(int appId, string privileges, string redirectUri) => string.Format(
                        "https://oauth.vk.com/authorize?client_id={0}&scope={1}&redirect_uri={2}&response_type=token",
                        appId,
                        privileges,
                        Uri.EscapeUriString(redirectUri)
                    );

        public static Token FromRedirectUrl(string url)
        {
            const string accessTokenPn = @"access_token";
            const string signPn = @"secret";
            const string useridPn = @"user_id";
            const string errorPn = @"error";
            const string errorRPn = @"error_reason";
            const string errorDescPn = @"error_description";
            var query = new Uri(url)
                .Fragment
                .TrimStart('#')
                .Split('&')
                .Select(a => a.Split('='))
                .Where(a => a.Length == 2)
                .GroupBy(a => a[0])
                .ToDictionary(a => a.Key, a => a.First()[1]);
            if (query.ContainsKey(accessTokenPn))
            {
                return new Token(
                    query[accessTokenPn],
                    query.TryGetValue(signPn, out var sign) ? sign : "",
                    int.Parse(query[useridPn])
                );
            }

            if (query.ContainsKey(errorPn))
            {
                throw new Exception($"Error: {query[errorPn]}\r\nType:{query[errorRPn]}\r\nMessage:{query[errorDescPn].Replace('+', ' ')}");
            }

            throw new FormatException("Can't parse VK response from URL");
        }
    }
}