using System;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Abstraction over the WPF popup window. Real implementation calls
/// <c>AlarmPopupWindow.Show()</c>; tests inject a fake that records calls.
/// </summary>
public interface IAlarmPopupGate
{
    /// <summary>
    /// Show the popup for <paramref name="e"/>. Must invoke <paramref name="onDismissed"/>
    /// exactly once when the user closes the popup.
    /// </summary>
    void Show(AlarmEvent e, Action onDismissed);
}
