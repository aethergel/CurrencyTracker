using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace CurrencyTracker.Manager.Langs;

public class LanguageUpdater
{
    private const string baseUrl = "https://raw.githubusercontent.com/AtmoOmen/CurrencyTracker/master/CurrencyTracker/Manager/Langs/";
    private const string fallbackBaseUrl = "https://raw.githubusercontents.com/AtmoOmen/CurrencyTracker/master/CurrencyTracker/Manager/Langs/"; // Mainly for CN players

    public static async Task DownloadLanguageFilesAsync()
    {
        var httpClient = new HttpClient();

        if (!Directory.Exists(LanguageManager.LangsDirectory))
            Directory.CreateDirectory(LanguageManager.LangsDirectory);

        foreach (var language in LanguageManager.LanguageNames)
        {
            var success = await TryDownloadLanguageFileAsync(httpClient, baseUrl, language.Language);
            if (!success) await TryDownloadLanguageFileAsync(httpClient, fallbackBaseUrl, language.Language);
        }
    }

    private static async Task<bool> TryDownloadLanguageFileAsync(HttpClient httpClient, string baseUrl, string language)
    {
        var url = $"{baseUrl}{language}.resx";
        var localFilePath = Path.Combine(LanguageManager.LangsDirectory, $"{language}.resx");

        try
        {
            var response = await httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(localFilePath, content);
                DService.Log.Debug($"Successfully downloaded {language} language file.");
                return true;
            }

            DService.Log.Error($"Failed to download {language} language file. Status: {response.StatusCode}");
            return false;
        }
        catch (Exception ex)
        {
            DService.Log.Error($"Error downloading {language} language file. Error: {ex.Message}");
            return false;
        }
    }
}
