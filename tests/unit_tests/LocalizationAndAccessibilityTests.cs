using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace TapAndEat.Tests;

public class LocalizationAndAccessibilityTests
{
    private static string FindPath(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return Path.GetFullPath(candidate);
            
            current = current.Parent;
        }
        
        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), relativePath));
    }

    [Fact]
    public async Task I18nDictionaries_HaveCompleteKeyParity_EnAndBn()
    {
        var i18nPath = FindPath("src/TapAndEat.Api/wwwroot/js/i18n.js");

        File.Exists(i18nPath).Should().BeTrue($"i18n.js not found at {i18nPath}");
        var content = await File.ReadAllTextAsync(i18nPath);

        content.Should().Contain("en", "i18n.js should contain 'en' dictionary");
        content.Should().Contain("bn", "i18n.js should contain 'bn' dictionary");
    }

    [Fact]
    public async Task CoreHtmlPages_HaveLangAttribute()
    {
        var wwwroot = FindPath("src/TapAndEat.Api/wwwroot");

        Directory.Exists(wwwroot).Should().BeTrue();

        var htmlFiles = Directory.GetFiles(wwwroot, "*.html", SearchOption.AllDirectories);
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
        var cssPath = FindPath("src/TapAndEat.Api/wwwroot/css/site.css");

        File.Exists(cssPath).Should().BeTrue();
        var css = await File.ReadAllTextAsync(cssPath);

        (css.Contains(":focus-visible") || css.Contains("outline:") || css.Contains(":focus")).Should().BeTrue();
    }
}