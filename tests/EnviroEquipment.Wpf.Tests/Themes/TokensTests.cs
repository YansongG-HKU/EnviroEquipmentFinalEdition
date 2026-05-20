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

    // The DARK theme is the locked target. styles.css declares a light base ":root" and a dark
    // override ":root.theme-night"; CssToXaml.ps1 merges them (night wins). These spot-checks
    // assert the *effective dark* hex: surfaces/text/cyan/run come from the night block, while
    // status (ok/warn/alarm/offline) and series colors are inherited unchanged from the base.
    private static readonly (string Key, string Hex)[] SpotCheckBrushes =
    {
        ("BrushBg0", "#07090D"),   // night override
        ("BrushBg1", "#0B0E13"),   // night override
        ("BrushBg2", "#11151C"),   // night override
        ("BrushTxt0", "#E6ECF5"),  // night override
        ("BrushCyan", "#3FBDD0"),  // night override
        ("BrushRun", "#3FBDD0"),   // night override
        ("BrushAlarm", "#DC2626"), // inherited from base
        ("BrushWarn", "#D97706"),  // inherited from base
        ("BrushOk", "#16A34A"),    // inherited from base
        ("BrushOffline", "#94A3B8"),    // inherited from base
        ("BrushSeriesTemp", "#DC2626"), // inherited from base
        ("BrushSeriesHumid", "#0E7CB5"),// inherited from base
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
