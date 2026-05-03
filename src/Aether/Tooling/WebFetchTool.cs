using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling;

public sealed class WebFetchTool
{
    internal Func<string, CancellationToken, Task<bool>> IsPrivateHostAsync { get; set; }

    private readonly HttpClient _http;
    private readonly ILogger<WebFetchTool> _logger;
    private const int MaxResponseBytes = 5 * 1024 * 1024; // 5MB
    private const int MaxOutputChars = 100 * 1024; // 100KB
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    // Private IP ranges for SSRF prevention
    private static readonly IPNetwork[] PrivateNetworks =
    {
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("127.0.0.0/8"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("0.0.0.0/8"),
    };

    public WebFetchTool(HttpClient http, ILogger<WebFetchTool> logger)
    {
        _http = http;
        _logger = logger;
        IsPrivateHostAsync = DefaultIsPrivateHostAsync;
    }

    public async Task<string> ExecuteAsync(string url, CancellationToken ct)
    {
        // Validate URL
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("web_fetch: invalid URL format.");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new InvalidOperationException("web_fetch: only http and https URLs are allowed.");

        // SSRF check: resolve host and verify not private
        try
        {
            var isPrivate = await IsPrivateHostAsync(uri.Host, ct);
            if (isPrivate)
                throw new InvalidOperationException("web_fetch: cannot fetch private network addresses.");
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"web_fetch: DNS resolution failed for '{uri.Host}': {ex.Message}");
        }

        // Fetch
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Add("Accept", "text/html, text/plain");

        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            response.EnsureSuccessStatusCode();

            // Check content length header
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw new InvalidOperationException("web_fetch: response exceeds 5MB limit.");

            using var stream = await response.Content.ReadAsStreamAsync(cts.Token);

            var buffer = new byte[MaxResponseBytes + 1];
            var totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, totalRead,
                       Math.Min(8192, buffer.Length - totalRead), cts.Token)) > 0)
            {
                totalRead += read;
                if (totalRead > MaxResponseBytes)
                    throw new InvalidOperationException("web_fetch: response exceeds 5MB limit.");
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            var isHtml = contentType.Contains("html") || contentType.Contains("xml");

            var encoding = System.Text.Encoding.UTF8;
            try
            {
                var charset = response.Content.Headers.ContentType?.CharSet;
                if (!string.IsNullOrEmpty(charset))
                    encoding = System.Text.Encoding.GetEncoding(charset);
            }
            catch { }

            var raw = encoding.GetString(buffer, 0, totalRead);
            var text = isHtml ? StripHtml(raw) : raw;

            if (text.Length > MaxOutputChars)
                text = text[..MaxOutputChars] + "\n[Output truncated at 100KB]";

            return text;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("web_fetch: request timed out after 15s.");
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException($"web_fetch: request failed - {ex.Message}");
        }
    }

    private async Task<bool> DefaultIsPrivateHostAsync(string host, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        foreach (var addr in addresses)
        {
            foreach (var network in PrivateNetworks)
            {
                if (network.Contains(addr))
                    return true;
            }
        }
        return false;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        // Remove scripts, styles, nav, footer, header
        html = Regex.Replace(html, @"<script[^>]*>.*?</script>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<style[^>]*>.*?</style>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<nav[^>]*>.*?</nav>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<footer[^>]*>.*?</footer>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<header[^>]*>.*?</header>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Replace block elements with newlines
        html = Regex.Replace(html, @"</?(?:p|div|br|h[1-6]|li|tr|article|section)[^>]*/?>", "\n", RegexOptions.IgnoreCase);

        // Strip remaining HTML tags
        html = Regex.Replace(html, @"<[^>]+>", "");

        // Decode HTML entities
        html = WebUtility.HtmlDecode(html);

        // Collapse whitespace
        html = Regex.Replace(html, @"\n{3,}", "\n\n");
        html = Regex.Replace(html, @"[ \t]{2,}", " ");

        return html.Trim();
    }
}
