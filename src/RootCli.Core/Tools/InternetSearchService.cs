
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RootCli.Core.Tools;

public static class InternetSearchService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    static InternetSearchService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("RootCli/0.1 (+local agent)");
    }

    public static string Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "internet_search requires query.";
        }

        query = query.Trim();
        if (Uri.TryCreate(query, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return FetchUrl(uri.ToString());
        }

        try
        {
            var url = "https://api.duckduckgo.com/?q=" + Uri.EscapeDataString(query) +
                      "&format=json&no_html=1&skip_disambig=1";
            var json = Http.GetStringAsync(url).GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.AppendLine("Search: " + query);

            var abstractText = root.TryGetProperty("AbstractText", out var abs) ? abs.GetString() : null;
            var abstractUrl = root.TryGetProperty("AbstractURL", out var abu) ? abu.GetString() : null;
            if (!string.IsNullOrWhiteSpace(abstractText))
            {
                sb.AppendLine();
                sb.AppendLine(abstractText);
                if (!string.IsNullOrWhiteSpace(abstractUrl))
                {
                    sb.AppendLine(abstractUrl);
                }
            }

            if (root.TryGetProperty("RelatedTopics", out var topics) &&
                topics.ValueKind == JsonValueKind.Array)
            {
                var count = 0;
                foreach (var topic in topics.EnumerateArray())
                {
                    if (count >= 8)
                    {
                        break;
                    }

                    if (topic.TryGetProperty("Text", out var textEl))
                    {
                        var text = textEl.GetString();
                        var link = topic.TryGetProperty("FirstURL", out var u) ? u.GetString() : "";
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            sb.AppendLine();
                            sb.AppendLine("- " + text);
                            if (!string.IsNullOrWhiteSpace(link))
                            {
                                sb.AppendLine("  " + link);
                            }

                            count++;
                        }
                    }
                }
            }

            var result = sb.ToString().Trim();
            if (result == "Search: " + query)
            {
                return result + "\n\n(No DuckDuckGo instant answer. Try a more specific query or a direct URL.)";
            }

            return Truncate(result, 12000);
        }
        catch (Exception ex)
        {
            return "internet_search failed: " + ex.Message;
        }
    }

    private static string FetchUrl(string url)
    {
        try
        {
            using var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var contentType = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("text", StringComparison.OrdinalIgnoreCase) ||
                contentType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(contentType))
            {
                var text = WebUtility.HtmlDecode(StripTags(body));
                return "URL: " + url + "\nStatus: " + (int)resp.StatusCode + "\n\n" + Truncate(text.Trim(), 12000);
            }

            return "URL: " + url + "\nStatus: " + (int)resp.StatusCode + "\n(binary/non-text content-type: " + contentType + ")";
        }
        catch (Exception ex)
        {
            return "Failed to fetch URL: " + ex.Message;
        }
    }

    private static string StripTags(string html) =>
        Regex.Replace(html ?? "", "<[^>]+>", " ");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
