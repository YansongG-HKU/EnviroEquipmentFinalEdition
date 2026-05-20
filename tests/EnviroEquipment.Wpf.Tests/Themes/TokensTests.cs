using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using FluentAssertions;
using Xunit;

namespace EnviroEquipment.Wpf.Tests.Themes;

[Trait("Category", "Pkg1")]
public class TokensTests
{
    private static readonly string[] RequiredBrushKeys =
    {
        // Surfaces
        "BrushBg0", "BrushBg1", "BrushBg2", "BrushBg3", "BrushBg4", "BrushBg5",
        // Lines
        "BrushLine1", "BrushLine2", "BrushLine3",
        // Text
        "BrushTxt0", "BrushTxt1", "BrushTxt2", "BrushTxt3", "BrushTxt4",
        // Accent
        "BrushSteel", "BrushSteelDim",
        "BrushCyan", "BrushCyanDim", "BrushCyanBg",
        // Status
        "BrushOk", "BrushOkBg",
        "BrushRun", "BrushRunBg",
        "BrushSched", "BrushSchedBg",
        "BrushPause", "BrushPauseBg",
        "BrushWarn", "BrushWarnBg",
        "BrushAlarm", "BrushAlarmBg", "BrushAlarmStrong",
        "BrushOffline", "BrushOfflineBg",
        // Guide
        "BrushGuide", "BrushGuideBg", "BrushGuideBorder",
        // Series
        "BrushSeriesTemp", "BrushSeriesHumid", "BrushSeriesPress", "BrushSeriesSet",
    };

    private static readonly (string Key, string Hex)[] SpotCheckBrushes =
    {
        ("BrushBg1", "#F6F8FB"),
        ("BrushTxt0", "#0F172A"),
        ("BrushCyan", "#0E7CB5"),
        ("BrushRun", "#0E7CB5"),
        ("BrushAlarm", "#DC2626"),
        ("BrushWarn", "#D97706"),
        ("BrushOk", "#16A34A"),
        ("BrushOffline", "#94A3B8"),
        ("BrushSeriesTemp", "#DC2626"),
        ("BrushSeriesHumid", "#0E7CB5"),
    };

    private static ResourceDictionary LoadTokens()
    {
        // Ensure the WPF "pack://" URI scheme is registered. In a headless xunit host
        // there is no running Application, so the pack scheme is not auto-registered and
        // the pack:// URI fails to parse ("Invalid port specified"). Touching the
        // PackUriHelper / creating an Application instance registers the scheme.
        EnsureWpfApplication();
        // Application.LoadComponent requires a *relative* pack URI when an Application exists.
        var uri = new Uri("/SiemensS7Demo.Wpf;component/Themes/Tokens.xaml", UriKind.Relative);
        return (ResourceDictionary)Application.LoadComponent(uri);
    }

    private static void EnsureWpfApplication()
    {
        // Force registration of the "pack" Uri scheme.
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        if (Application.Current is null)
        {
            // Constructing an Application registers the application pack scheme and sets
            // ResourceAssembly so component-relative pack URIs resolve.
            _ = new Application();
        }
    }

    [Fact]
    public void Tokens_ContainAllRequiredBrushKeys()
    {
        var dict = LoadTokens();
        var missing = new List<string>();
        foreach (var key in RequiredBrushKeys)
        {
            if (!dict.Contains(key))
            {
                missing.Add(key);
            }
        }
        missing.Should().BeEmpty("every CSS custom property must have a XAML brush counterpart");
    }

    [Theory]
    [MemberData(nameof(SpotCheckData))]
    public void Tokens_SpotCheckMatchesCssHex(string key, string expectedHex)
    {
        var dict = LoadTokens();
        dict.Contains(key).Should().BeTrue($"{key} must exist");
        var brush = dict[key] as SolidColorBrush;
        brush.Should().NotBeNull($"{key} must be a SolidColorBrush");
        var actual = $"#{brush!.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
        actual.Should().BeEquivalentTo(expectedHex, $"{key} must match styles.css");
    }

    public static IEnumerable<object[]> SpotCheckData()
    {
        foreach (var t in SpotCheckBrushes)
        {
            yield return new object[] { t.Key, t.Hex };
        }
    }
}
