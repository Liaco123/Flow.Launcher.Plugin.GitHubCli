using System.Reflection;
using System.Xml.Linq;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public class TranslationApiProxy : DispatchProxy
{
    public string Language { get; set; } = "zh-cn";

    internal static IReadOnlyDictionary<string, string> Load(string language)
    {
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Languages", language + ".xaml"));
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        return document.Root!.Elements().ToDictionary(
            element => (string)element.Attribute(xaml + "Key")!,
            element => element.Value);
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod?.Name == nameof(IPublicAPI.GetTranslation))
        {
            var key = (string)args![0]!;
            var language = Language is "zh-cn" or "zh-tw" ? Language : "en";
            return Load(language).TryGetValue(key, out var value) ? value : Load("en")[key];
        }

        throw new NotSupportedException("Unexpected API call: " + targetMethod?.Name);
    }
}
