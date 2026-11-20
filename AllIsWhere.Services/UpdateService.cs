using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AllIsWhere.Services;

public static class UpdateService
{
	private const string LatestReleaseUrl = "https://api.github.com/repos/mertdmrc8/AllIsWhere-Updates/releases/latest";

	private const string SetupAssetName = "AllIsWhere-Setup.exe";

	private static readonly HttpClient Client = CreateClient();

	public static async Task<AppUpdate?> FindUpdateAsync(Version currentVersion, CancellationToken token = default(CancellationToken))
	{
		using HttpResponseMessage response = await Client.GetAsync("https://api.github.com/repos/mertdmrc8/AllIsWhere-Updates/releases/latest", HttpCompletionOption.ResponseHeadersRead, token);
		response.EnsureSuccessStatusCode();
		AppUpdate? result;
		await using (Stream stream = await response.Content.ReadAsStreamAsync(token))
		{
			using JsonDocument jsonDocument = await JsonDocument.ParseAsync(stream, default, token);
			JsonElement rootElement = jsonDocument.RootElement;
			JsonElement value2;
			if (rootElement.TryGetProperty("draft", out var value) && value.GetBoolean())
			{
				result = null;
			}
			else if (rootElement.TryGetProperty("prerelease", out value2) && value2.GetBoolean())
			{
				result = null;
			}
			else
			{
				string text = rootElement.GetProperty("tag_name").GetString() ?? string.Empty;
				if (!Version.TryParse(text.TrimStart(new char[2] { 'v', 'V' }).Split('-', '+')[0], out var result2))
				{
					throw new InvalidDataException("Release tag '" + text + "' is not a valid version.");
				}
				Version version = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
				if (!(result2 <= version))
				{
					foreach (JsonElement item in rootElement.GetProperty("assets").EnumerateArray())
					{
						if (!string.Equals(item.GetProperty("name").GetString(), "AllIsWhere-Setup.exe", StringComparison.Ordinal))
						{
							continue;
						}
						string downloadUrl = item.GetProperty("browser_download_url").GetString() ?? string.Empty;
						string text2 = (item.TryGetProperty("digest", out var value3) ? (value3.GetString() ?? string.Empty) : string.Empty);
						if (!text2.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException("The GitHub release does not provide a SHA-256 digest for the setup asset.");
						}
						string text3 = (rootElement.TryGetProperty("body", out var value4) ? (value4.GetString() ?? string.Empty) : string.Empty);
						if (text3.Length > 1200)
						{
							text3 = text3.Substring(0, 1200) + "…";
						}
						result = new AppUpdate(result2, downloadUrl, text2.Substring(7), text3);
						goto end_IL_01ac;
					}
					throw new FileNotFoundException("The latest release does not contain AllIsWhere-Setup.exe.");
				}
				result = null;
			}
			end_IL_01ac:;
		}
		return result;
	}

	public static async Task<string> DownloadAndVerifyAsync(AppUpdate update, CancellationToken token = default(CancellationToken))
	{
		string destination = Path.Combine(Path.GetTempPath(), $"AllIsWhere-Setup-{update.Version}-{Guid.NewGuid():N}.exe");
		try
		{
			using HttpResponseMessage response = await Client.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, token);
			response.EnsureSuccessStatusCode();
			await using (Stream input = await response.Content.ReadAsStreamAsync(token))
			{
				await using FileStream output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
				await input.CopyToAsync(output, token);
			}
			string result;
			await using (FileStream file = File.OpenRead(destination))
			{
				if (!CryptographicOperations.FixedTimeEquals(await SHA256.HashDataAsync(file, token), Convert.FromHexString(update.Sha256)))
				{
					throw new InvalidDataException("The setup file SHA-256 digest does not match the release metadata.");
				}
				result = destination;
			}
			return result;
		}
		catch
		{
			if (File.Exists(destination))
			{
				File.Delete(destination);
			}
			throw;
		}
	}

	private static HttpClient CreateClient()
	{
		HttpClient httpClient = new HttpClient();
		httpClient.Timeout = TimeSpan.FromSeconds(20L);
		string text = typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
		httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AllIsWhere/" + text);
		httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
		return httpClient;
	}
}
