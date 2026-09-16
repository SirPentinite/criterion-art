using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CriterionArt.Provider;

internal record FlareSolverrResult(string Html, CookieCollection Cookies);

internal static class FlareSolverrClient
{
    private const string FlareSolverrUrl = "http://localhost:8191/v1";

    public static async Task<FlareSolverrResult?> SolveAsync(
        HttpClient client, string targetUrl, CancellationToken ct)
    {
        var payload = new
        {
            cmd = "request.get",
            url = targetUrl,
            maxTimeout = 60000
        };

        var body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, FlareSolverrUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var solution = doc.RootElement.GetProperty("solution");
        var html = solution.GetProperty("response").GetString() ?? string.Empty;

        var cookies = new CookieCollection();
        foreach (var c in solution.GetProperty("cookies").EnumerateArray())
        {
            cookies.Add(new Cookie(
                c.GetProperty("name").GetString(),
                c.GetProperty("value").GetString(),
                "/",
                c.GetProperty("domain").GetString()));
        }

        return new FlareSolverrResult(html, cookies);
    }
}

public class CriterionImageProvider : IRemoteImageProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CriterionImageProvider> _logger;
	
    public CriterionImageProvider(IHttpClientFactory httpClientFactory, ILogger<CriterionImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
		_logger = logger;
    }
	
	private HttpClient CreateClient()
	{
		var client = _httpClientFactory.CreateClient();
		client.Timeout = TimeSpan.FromSeconds(15);
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
		client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
		client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
		client.DefaultRequestHeaders.Add("Sec-Fetch-Mode", "navigate");
		client.DefaultRequestHeaders.Add("Sec-Fetch-Site", "none");
		client.DefaultRequestHeaders.Add("Sec-Fetch-Dest", "document");
		return client;
	}

    public string Name => "Criterion Collection";

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => new[] { ImageType.Primary, ImageType.Box };

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item, CancellationToken cancellationToken)
    {
        _logger.LogInformation("CriterionArt: starting lookup for {Title}", item.Name);
        var results = new List<RemoteImageInfo>();

        var client = CreateClient();
        _logger.LogInformation("CriterionArt: fetching homepage/session");
        var responseJson = await CriterionLivewireClient.GetSearchResponseJsonAsync(
            client, item.Name, cancellationToken);
        _logger.LogInformation("CriterionArt: got livewire response, length={Len}", responseJson?.Length ?? -1);

        if (responseJson is null) return results;

        var candidates = CriterionSearchParser.ParseLivewireResponse(responseJson);
        _logger.LogInformation("CriterionArt: parsed {Count} candidates", candidates.Count);

        var match = CriterionSearchParser.FindBestMatch(candidates, item.Name, item.ProductionYear);
        _logger.LogInformation("CriterionArt: match={Match}", match?.Title ?? "none");

        if (match is null) return results;

        results.Add(new RemoteImageInfo { ProviderName = Name, Url = match.ImageUrl, Type = ImageType.Primary });
        return results;
    }
	
	public static async Task<string?> GetSearchResponseJsonAsync(
		HttpClient client, CookieContainer cookies, string query, CancellationToken ct)
	{
		var solved = await FlareSolverrClient.SolveAsync(client, "https://www.criterion.com/", ct);
		if (solved is null) return null;

		foreach (Cookie c in solved.Cookies)
			cookies.Add(new Uri("https://www.criterion.com"), c);

		var homeHtml = solved.Html;
		// ... same token/snapshot regex extraction as before, using homeHtml

		// ... same POST to /livewire/update as before —
		// it'll now carry the cf_clearance cookie via the shared CookieContainer
	}

	public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
        => CreateClient().GetAsync(url, ct);
	
} 
