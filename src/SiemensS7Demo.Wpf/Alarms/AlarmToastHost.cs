using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SiemensS7Demo.App.Alarms;
using SiemensS7Demo.Domain.Alarms;

namespace SiemensS7Demo.Wpf.Alarms;

/// <summary>
/// Visual surface for the non-blocking toast stack. Sits at the bottom-right of
/// the shell, overlaid via <c>Panel.ZIndex</c>. Auto-fade is handled by the
/// underlying <see cref="AlarmToastNotifier"/>; this control only owns the
/// presentation (item layout, basic styling).
/// </summary>
public sealed class AlarmToastHost : ItemsControl, IDisposable
{
    private AlarmToastNotifier? _notifier;

    public AlarmToastHost()
    {
        ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        Width = 360;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        Margin = new Thickness(0, 0, 16, 16);
        IsHitTestVisible = true;
        Focusable = false;
        Background = Brushes.Transparent;
        ItemTemplate = BuildItemTemplate();
    }

    /// <summary>
    /// Attach to a live alarm service. Subscribes to Warn/Info events through a new
    /// <see cref="AlarmToastNotifier"/> and binds its collection to <c>ItemsSource</c>.
    /// Re-entrant: a second call replaces the prior subscription.
    /// </summary>
    public void Attach(IAlarmService service)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));

        _notifier?.Dispose();
        _notifier = new AlarmToastNotifier(service);
        ItemsSource = _notifier.Toasts;
    }

    private static DataTemplate BuildItemTemplate()
    {
        // Programmatic DataTemplate: small dark card with title + body, mirrors the
        // visual language of the side panels. Background is keyed off Level.
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.MarginProperty, new Thickness(0, 0, 0, 8));
        border.SetValue(Border.PaddingProperty, new Thickness(14, 10, 14, 10));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        border.SetResourceReference(Border.BorderBrushProperty, "BrushLine2");
        border.SetResourceReference(Border.BackgroundProperty, "BrushBg3");

        var stack = new FrameworkElementFactory(typeof(StackPanel));

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(ToastNotificationViewModel.Title)));
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.FontSizeProperty, 12.0);
        title.SetResourceReference(TextBlock.ForegroundProperty, "BrushTxt0");

        var body = new FrameworkElementFactory(typeof(TextBlock));
        body.SetBinding(TextBlock.TextProperty, new Binding(nameof(ToastNotificationViewModel.Body)));
        body.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        body.SetValue(TextBlock.FontSizeProperty, 11.0);
        body.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 0, 0));
        body.SetResourceReference(TextBlock.ForegroundProperty, "BrushTxt2");

        stack.AppendChild(title);
        stack.AppendChild(body);

        border.AppendChild(stack);
        // Bind opacity to allow future fade animation hooks (not yet animated).
        border.SetBinding(UIElement.OpacityProperty, new Binding(nameof(ToastNotificationViewModel.Opacity)));

        return new DataTemplate(typeof(ToastNotificationViewModel)) { VisualTree = border };
    }

    public void Dispose() => _notifier?.Dispose();
}
