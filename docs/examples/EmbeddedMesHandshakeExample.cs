// HTTP boundary example for an existing MES. The same request sequence is
// usable from .NET Framework 4.5.2 and .NET 6; replace Json(...) with the JSON
// library already used by the MES (for example Newtonsoft.Json on 4.5.2).
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

public static class EmbeddedMesHandshakeExample
{
    public static async Task<string> EstablishAsync(
        string embeddedHostOrigin,
        string parentOrigin,
        string account)
    {
        if (String.IsNullOrEmpty(account) || account.Trim() != account)
            throw new ArgumentException("account must be one non-empty value", nameof(account));

        var cookies = new CookieContainer();
        using (var handler = new HttpClientHandler { CookieContainer = cookies, UseCookies = true })
        using (var client = new HttpClient(handler))
        {
            client.DefaultRequestHeaders.Add("Origin", parentOrigin);
            var suffix = "?account=" + Uri.EscapeDataString(account);
            var challenge = await PostAsync(client, embeddedHostOrigin + "/api/v1/embedded/challenges" + suffix);
            var nonce = ReadString(challenge, "nonce");
            var assertion = await GetAsync(client,
                embeddedHostOrigin + "/api/v1/embedded/assertions?nonce=" + Uri.EscapeDataString(nonce) + "&account=" + Uri.EscapeDataString(account));
            var signedAssertion = ReadString(assertion, "assertion");

            // The exchange also carries account in both query and JSON. The
            // server compares it with the MES current-user response; it is not
            // accepted as a replacement for that response.
            var body = "{\"assertion\":\"" + JsonEscape(signedAssertion)
                + "\",\"account\":\"" + JsonEscape(account) + "\"}";
            return await PostAsync(client, embeddedHostOrigin + "/api/v1/embedded/exchanges" + suffix, body);
        }
    }

    private static async Task<string> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        return await RequireSuccess(response);
    }

    private static Task<string> PostAsync(HttpClient client, string url) => PostAsync(client, url, null);

    private static async Task<string> PostAsync(HttpClient client, string url, string body)
    {
        using (var content = body == null ? null : new StringContent(body, Encoding.UTF8, "application/json"))
        {
            var response = await client.PostAsync(url, content);
            return await RequireSuccess(response);
        }
    }

    private static async Task<string> RequireSuccess(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Embedded handshake rejected: " + (int)response.StatusCode + " " + body);
        return body;
    }

    // Keep this tiny example dependency-free. Production code should use its
    // existing JSON parser and validate the complete response contract.
    private static string ReadString(string json, string property)
    {
        var marker = "\"" + property + "\":\"";
        var start = json.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException("Missing " + property);
        start += marker.Length;
        var end = json.IndexOf('"', start);
        if (end < 0) throw new InvalidOperationException("Invalid " + property);
        return json.Substring(start, end - start);
    }

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
