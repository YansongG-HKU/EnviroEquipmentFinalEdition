using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SiemensS7Demo.Domain.Alarms;

public static class AlarmEvaluator
{
    private static readonly Regex Placeholder = new(
        @"\{(?<field>Pv|Sv|Humid|HumidSv|Press|PressSv)(?::(?<format>[^}]+))?\}",
        RegexOptions.Compiled);

    // DeviceId is threaded in separately because Pkg 1's ReadingSnapshot does not carry it
    // (the id lives on Device, not on the per-poll snapshot).
    public static IEnumerable<AlarmEvent> Evaluate(
        DeviceId deviceId,
        ReadingSnapshot snapshot,
        IReadOnlyList<AlarmRule> rules)
    {
        if (deviceId is null) throw new ArgumentNullException(nameof(deviceId));
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (rules is null) throw new ArgumentNullException(nameof(rules));

        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            bool fires;
            try
            {
                fires = rule.Trigger(snapshot);
            }
            catch
            {
                continue;
            }

            if (!fires) continue;

            var message = RenderTemplate(rule.MessageTemplate, snapshot);
            yield return new AlarmEvent(
                Id: $"{deviceId.Value}:{rule.Code}:{snapshot.At.ToUnixTimeMilliseconds()}",
                DeviceId: deviceId,
                Level: rule.Level,
                Code: rule.Code,
                Message: message,
                At: snapshot.At,
                Ack: false,
                Reset: false,
                Muted: false);
        }
    }

    private static string RenderTemplate(string template, ReadingSnapshot s)
    {
        return Placeholder.Replace(template, m =>
        {
            var field = m.Groups["field"].Value;
            var format = m.Groups["format"].Success ? m.Groups["format"].Value : null;
            double? v = field switch
            {
                "Pv" => s.Pv,
                "Sv" => s.Sv,
                "Humid" => s.Humid,
                "HumidSv" => s.HumidSv,
                "Press" => s.Press,
                "PressSv" => s.PressSv,
                _ => null
            };
            if (!v.HasValue) return "-";
            return format is null
                ? v.Value.ToString(CultureInfo.InvariantCulture)
                : v.Value.ToString(format, CultureInfo.InvariantCulture);
        });
    }
}
