using System.Globalization;
using System.Windows;

namespace Alls.Bootstrapper.Services;

internal sealed class LocalizationService
{
    private const string ResourcePrefix = "Resources/Strings.";
    private readonly Dictionary<string, ResourceDictionary> languageDictionaries = new(StringComparer.OrdinalIgnoreCase);
    private string defaultLanguage = "zh-CN";

    public void Configure(string? requestedLanguage)
    {
        var language = Normalize(requestedLanguage);
        defaultLanguage = language;
        var culture = CultureInfo.GetCultureInfo(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--)
        {
            if (dictionaries[index].Source?.OriginalString.Contains(ResourcePrefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                dictionaries.RemoveAt(index);
            }
        }

        dictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"{ResourcePrefix}{language}.xaml", UriKind.Relative)
        });
    }

    public string Get(string key)
    {
        return Application.Current.TryFindResource(key) as string ?? key;
    }

    public string Get(string key, string? requestedLanguage)
    {
        var language = string.IsNullOrWhiteSpace(requestedLanguage)
            ? defaultLanguage
            : Normalize(requestedLanguage);
        if (language.Equals(defaultLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return Get(key);
        }

        if (!languageDictionaries.TryGetValue(language, out var dictionary))
        {
            dictionary = new ResourceDictionary
            {
                Source = new Uri($"{ResourcePrefix}{language}.xaml", UriKind.Relative)
            };
            languageDictionaries[language] = dictionary;
        }

        return dictionary.Contains(key) ? dictionary[key] as string ?? Get(key) : Get(key);
    }

    private static string Normalize(string? language)
    {
        if (language?.StartsWith("ja", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "ja-JP";
        }

        if (language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "en-US";
        }

        return "zh-CN";
    }
}
