using System.Text.RegularExpressions;
using AirGlance.App.Services;
using Xunit;

namespace AirGlance.App.Tests;

/// <summary>
/// The catalog is one dictionary with an English and a Chinese half, so a key can only be
/// missing both or neither. What it cannot prevent is a half that is empty, a Chinese
/// string that dropped a <c>{0}</c>, or a key that no longer matches the <c>L.</c> shape
/// the XAML resource dictionary expects.
/// </summary>
public sealed partial class LocalizationCatalogTests
{
    [Fact]
    public void Every_key_has_both_languages_and_the_same_placeholders()
    {
        var failures = new List<string>();

        foreach (var (key, value) in LocalizationService.CatalogForTest)
        {
            if (!key.StartsWith("L.", StringComparison.Ordinal))
            {
                failures.Add($"{key}: key must start with 'L.'");
            }

            if (string.IsNullOrWhiteSpace(value.En))
            {
                failures.Add($"{key}: English text is empty");
            }

            if (string.IsNullOrWhiteSpace(value.Zh))
            {
                failures.Add($"{key}: Chinese text is empty");
            }

            var english = Placeholders(value.En);
            var chinese = Placeholders(value.Zh);
            if (!english.SequenceEqual(chinese))
            {
                failures.Add(
                    $"{key}: placeholders differ, en=[{string.Join(", ", english)}] " +
                    $"zh=[{string.Join(", ", chinese)}]");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(LocalizationService.English, "Previous image")]
    [InlineData(LocalizationService.ChineseSimplified, "上一张")]
    public void Keys_resolve_in_both_languages(string language, string expected)
    {
        using var localization = new LocalizationService();
        localization.Apply(language);

        Assert.Equal(expected, localization.T("L.PreviousImage"));
    }

    [Theory]
    [InlineData(LocalizationService.English)]
    [InlineData(LocalizationService.ChineseSimplified)]
    public void Every_key_resolves_and_never_falls_back_to_itself(string language)
    {
        using var localization = new LocalizationService();
        localization.Apply(language);

        var missing = LocalizationService.CatalogForTest.Keys
            .Where(key => string.Equals(localization.T(key), key, StringComparison.Ordinal))
            .ToList();

        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(LocalizationService.English)]
    [InlineData(LocalizationService.ChineseSimplified)]
    public void Format_substitutes_every_placeholder(string language)
    {
        using var localization = new LocalizationService();
        localization.Apply(language);

        foreach (var (key, value) in LocalizationService.CatalogForTest)
        {
            var count = Placeholders(value.En).Count;
            if (count == 0)
            {
                continue;
            }

            var arguments = Enumerable.Range(1, count).Cast<object>().ToArray();
            var formatted = localization.Format(key, arguments);

            Assert.DoesNotContain("{", formatted.Replace("{{", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
            foreach (var argument in arguments)
            {
                Assert.Contains(argument.ToString()!, formatted, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void An_unknown_key_falls_back_to_the_key()
    {
        using var localization = new LocalizationService();
        localization.Apply(LocalizationService.English);

        Assert.Equal("L.NotARealKey", localization.T("L.NotARealKey"));
    }

    private static List<string> Placeholders(string text) =>
        PlaceholderPattern()
            .Matches(text)
            .Select(match => match.Value)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToList();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex PlaceholderPattern();
}
