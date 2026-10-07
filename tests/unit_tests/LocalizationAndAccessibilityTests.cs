using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace TapAndEat.Tests;

public class LocalizationAndAccessibilityTests
{
    private static string FindPath(params string[] candidates)
    {
        foreach (var c in candidates)
            if (File.Exists(c) || Directory.Exists(c))
                return Path.GetFullPath(c);
        return Path.GetFullPath(candidates[0]);
    }

    [Fact]
    public async Task I18nDictionaries_HaveCompleteKeyParity_EnAndBn()
    {
        var i18nPath = FindPath(
            Path.Combine(AppContext.BaseDirectory, "../../../src/TapAndEat.Api/wwwroot/js/i18n.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "src/TapAndEat.Api/wwwroot/js/i18n.js"),
            Path.Combine(Directory.GetCurrentDirectory(), "../src/TapAndEat.Api/wwwroot/js/i18n.js"));

        File.Exists(i18nPath).Should().BeTrue($"i18n.js not found at {i18nPath}");
        var content = await File.ReadAllTextAsync(i18nPath);

        var enMatch = Regex.Match(content, @"en:\s*\{([^}]+)\}", RegexOptions.Singleline);
        var bnMatch = Regex.Match(content, @"bn:\s*\{([^}]+)\}", RegexOptions.Singleline);

        enMatch.Success.Should().BeTrue();
        bnMatch.Success.Should().BeTrue();

        var enKeys = Regex.Matches(enMatch.Value, @"""([^""]+)""\s*:").Select(m => m.Groups[1].Value).ToHashSet();
        var bnKeys = Regex.Matches(bnMatch.Value, @"""([^""]+)""\s*:").Select(m => m.Groups[1].Value).ToHashSet();

        enKeys.Count.Should().BeGreaterThanOrEqualTo(30);
        bnKeys.Count.Should().BeGreaterThanOrEqualTo(30);

        enKeys.Except(bnKeys).Should().BeEmpty("every EN key must exist in BN");
    }

    [Fact]
    public async Task CoreHtmlPages_HaveLangAttribute()
    {
        var wwwroot = FindPath(
            Path.Combine(AppContext.BaseDirectory, "../../../src/TapAndEat.Api/wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "src/TapAndEat.Api/wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "../src/TapAndEat.Api/wwwroot"));

        Directory.Exists(wwwroot).Should().BeTrue();

        var htmlFiles = Directory.GetFiles(wwwroot, "*.html");
        htmlFiles.Length.Should().BeGreaterThanOrEqualTo(5);

        foreach (var file in htmlFiles)
        {
            var text = await File.ReadAllTextAsync(file);
            Regex.IsMatch(text, @"<html[^>]*lang=""[^""]+""").Should()
                .BeTrue($"Missing lang attribute in {Path.GetFileName(file)}");
        }
    }

    [Fact]
    public async Task SiteCss_IncludesFocusVisibleOrOutline()
    {
        var cssPath = FindPath(
            Path.Combine(AppContext.BaseDirectory, "../../../src/TapAndEat.Api/wwwroot/css/site.css"),
            Path.Combine(Directory.GetCurrentDirectory(), "src/TapAndEat.Api/wwwroot/css/site.css"),
            Path.Combine(Directory.GetCurrentDirectory(), "../src/TapAndEat.Api/wwwroot/css/site.css"));

        File.Exists(cssPath).Should().BeTrue();
        var css = await File.ReadAllTextAsync(cssPath);

        (css.Contains(":focus-visible") || css.Contains("outline:")).Should().BeTrue();
    }
}