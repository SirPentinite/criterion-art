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
using System.Net;
using System.Text;
using System.Text.Json;
using System.IO;
using SkiaSharp;
using System.Runtime.InteropServices;
using System.Reflection;

namespace CriterionArt.Provider;

internal record FlareSolverrResult(string Html, CookieCollection Cookies, string? UserAgent);

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
        var userAgent = solution.TryGetProperty("userAgent", out var uaProp) ? uaProp.GetString() : null;

        var cookies = new CookieCollection();
		foreach (var c in solution.GetProperty("cookies").EnumerateArray())
		{
			var name = c.GetProperty("name").GetString();
			var value = c.GetProperty("value").GetString();
			var domain = c.GetProperty("domain").GetString();

			if (!string.IsNullOrEmpty(name))
			{
				cookies.Add(new Cookie(name, value, "/", domain));
			}
		}

        return new FlareSolverrResult(html, cookies, userAgent);
    }
}

public class CriterionImageProvider : IRemoteImageProvider
{
    static CriterionImageProvider()
    {
        var pluginDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        var skiaPath = Path.Combine(pluginDir, "libSkiaSharp.so");
        if (File.Exists(skiaPath))
        {
            NativeLibrary.Load(skiaPath);
        }
    }
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CriterionImageProvider> _logger;
    private CookieContainer _sharedCookies;
    private readonly HttpClient _persistentClient;
    private Dictionary<string, string> _cache = new();
	private readonly string _cacheFile = "/var/lib/jellyfin/data/plugins/criterion-cache.json";
	
	public CriterionImageProvider(IHttpClientFactory httpClientFactory, ILogger<CriterionImageProvider> logger)
	{
		_httpClientFactory = httpClientFactory;
		_logger = logger;
		_sharedCookies = new CookieContainer();

		LoadCache();

		var handler = new HttpClientHandler { CookieContainer = _sharedCookies };
		_persistentClient = new HttpClient(handler);
		_persistentClient.Timeout = TimeSpan.FromSeconds(90);
		_persistentClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
		_persistentClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
		_persistentClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
	}

    private HttpClient CreateClient(CookieContainer cookies)
    {
        var handler = new HttpClientHandler { CookieContainer = cookies };
        var client = new HttpClient(handler);
		client.Timeout = TimeSpan.FromSeconds(90);
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
		client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
		client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
		return client;
	}

    public string Name => "Criterion Collection";

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        => new[] { ImageType.Primary, ImageType.Box };
	
	private void LoadCache()
	{
		var dir = Path.GetDirectoryName(_cacheFile);
		if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
		{
			Directory.CreateDirectory(dir);
		}

		if (File.Exists(_cacheFile))
		{
			try
			{
				var json = File.ReadAllText(_cacheFile);
				var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
				_cache.Clear();
				foreach (var kvp in loaded)
				{
					_cache[kvp.Key] = kvp.Value;
				}
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "CriterionArt: failed to load cache");
			}
		}
	}

	private void SaveCache()
	{
		try
		{
			var dir = Path.GetDirectoryName(_cacheFile);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
			{
				Directory.CreateDirectory(dir);
			}

			var json = JsonSerializer.Serialize(_cache);
			File.WriteAllText(_cacheFile, json);
			_logger.LogInformation("CriterionArt: cache saved to {CacheFile}", _cacheFile);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "CriterionArt: failed to save cache");
		}
	}

	public async Task<IEnumerable<RemoteImageInfo>> GetImages(
		BaseItem item, CancellationToken cancellationToken)
	{
		_logger.LogInformation("CriterionArt: PLUGIN LOOKUP CALLED");
		_logger.LogInformation("CriterionArt: cache file = {CacheFile}", _cacheFile);

		var results = new List<RemoteImageInfo>();
		_sharedCookies = new CookieContainer();

		// Check cache first
		var cacheKey = $"{item.Name}:{item.ProductionYear}";
		if (_cache.TryGetValue(cacheKey, out var cachedUrl))
		{
			_logger.LogInformation("CriterionArt: cache hit for {Title}", item.Name);
			return new[] { new RemoteImageInfo { ProviderName = Name, Url = cachedUrl, Type = ImageType.Primary } };
		}

		var client = CreateClient(_sharedCookies);
		_logger.LogInformation("CriterionArt: fetching homepage/session");

		using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		cts.CancelAfter(TimeSpan.FromSeconds(30));

		try
		{
			var (snapshotJson, shouldClearCookies) = await CriterionLivewireClient.GetSearchSnapshotAsync(
				client, item.Name, cts.Token, _logger);

			if (shouldClearCookies && snapshotJson is null)
			{
				_logger.LogWarning("CriterionArt: Cloudflare challenge, lookup skipped");
				return results;
			}

			if (snapshotJson is null) return results;

			var candidates = CriterionSearchParser.ParseSnapshot(snapshotJson);
			_logger.LogInformation("CriterionArt: parsed {Count} candidates", candidates.Count);

			var match = CriterionSearchParser.FindBestMatch(candidates, item.Name, item.ProductionYear);
			_logger.LogInformation("CriterionArt: match={Match}", match?.Title ?? "none");

			if (match is null) return results;

			results.Add(new RemoteImageInfo { ProviderName = Name, Url = match.ImageUrl, Type = ImageType.Primary });

			// Cache successful result
			_cache[cacheKey] = match.ImageUrl;
			SaveCache();

			return results;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "CriterionArt: lookup failed");
			return results;
		}
	}
	
	public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken ct)
	{
		var client = _httpClientFactory.CreateClient();
		var bytes = await client.GetByteArrayAsync(url, ct);

		using var original = SKBitmap.Decode(bytes);
		if (original is null)
		{
			// Decode failed — fall back to passing the original bytes through unchanged
			var fallback = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
			{
				Content = new ByteArrayContent(bytes)
			};
			fallback.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
			return fallback;
		}

		const double targetRatio = 2.0 / 3.0; // width:height
		var currentRatio = (double)original.Width / original.Height;

		int canvasWidth = original.Width;
		int canvasHeight = original.Height;

		if (currentRatio > targetRatio)
			canvasHeight = (int)Math.Round(original.Width / targetRatio);
		else if (currentRatio < targetRatio)
			canvasWidth = (int)Math.Round(original.Height * targetRatio);

		using var surface = SKSurface.Create(new SKImageInfo(canvasWidth, canvasHeight));
		var canvas = surface.Canvas;
		canvas.Clear(SKColors.Black); // padding color — swap for SKColors.Transparent if preferred

		float x = (canvasWidth - original.Width) / 2f;
		float y = (canvasHeight - original.Height) / 2f;
		canvas.DrawBitmap(original, x, y, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));

		using var image = surface.Snapshot();
		using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

		var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK)
		{
			Content = new ByteArrayContent(data.ToArray())
		};
		response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
		return response;
	}
	
} 
