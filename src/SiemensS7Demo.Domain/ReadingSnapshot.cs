using System;

namespace SiemensS7Demo.Domain;

public sealed record ReadingSnapshot(
    DateTimeOffset At,
    double? Pv,
    double? Sv,
    double? Humid,
    double? HumidSv,
    double? Press,
    double? PressSv);
