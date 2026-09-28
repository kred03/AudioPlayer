using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AudioPlayer.UI.Behaviors;

public static class SliderDragBehavior
{
    public static readonly DependencyProperty IsDirectDragEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsDirectDragEnabled",
            typeof(bool),
            typeof(SliderDragBehavior),
            new PropertyMetadata(false, OnIsDirectDragEnabledChanged));

    private static readonly DependencyProperty IsDraggingProperty =
        DependencyProperty.RegisterAttached(
            "IsDragging",
            typeof(bool),
            typeof(SliderDragBehavior),
            new PropertyMetadata(false));

    public static bool GetIsDirectDragEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsDirectDragEnabledProperty);

    public static void SetIsDirectDragEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsDirectDragEnabledProperty, value);

    private static void OnIsDirectDragEnabledChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not Slider slider)
        {
            return;
        }

        if (e.NewValue is true)
        {
            slider.AddHandler(
                Mouse.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnMouseLeftButtonDown),
                handledEventsToo: true);
            slider.AddHandler(
                Mouse.PreviewMouseMoveEvent,
                new MouseEventHandler(OnMouseMove),
                handledEventsToo: true);
            slider.AddHandler(
                Mouse.PreviewMouseUpEvent,
                new MouseButtonEventHandler(OnMouseLeftButtonUp),
                handledEventsToo: true);
            slider.LostMouseCapture += OnLostMouseCapture;
        }
        else
        {
            slider.RemoveHandler(
                Mouse.PreviewMouseDownEvent,
                new MouseButtonEventHandler(OnMouseLeftButtonDown));
            slider.RemoveHandler(
                Mouse.PreviewMouseMoveEvent,
                new MouseEventHandler(OnMouseMove));
            slider.RemoveHandler(
                Mouse.PreviewMouseUpEvent,
                new MouseButtonEventHandler(OnMouseLeftButtonUp));
            slider.LostMouseCapture -= OnLostMouseCapture;
        }
    }

    private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider ||
            !slider.IsEnabled ||
            e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        slider.SetValue(IsDraggingProperty, true);
        slider.Focus();
        Mouse.Capture(slider, CaptureMode.Element);
        UpdateValue(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider ||
            !(bool)slider.GetValue(IsDraggingProperty) ||
            Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        UpdateValue(slider, e.GetPosition(slider));
        e.Handled = true;
    }

    private static void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider ||
            !(bool)slider.GetValue(IsDraggingProperty) ||
            e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        UpdateValue(slider, e.GetPosition(slider));
        slider.SetValue(IsDraggingProperty, false);
        Mouse.Capture(null);
        e.Handled = true;
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.SetValue(IsDraggingProperty, false);
        }
    }

    private static void UpdateValue(Slider slider, Point position)
    {
        var length = slider.Orientation == Orientation.Horizontal
            ? slider.ActualWidth
            : slider.ActualHeight;

        if (length <= 0 || slider.Maximum <= slider.Minimum)
        {
            return;
        }

        var offset = slider.Orientation == Orientation.Horizontal
            ? position.X
            : length - position.Y;
        var ratio = Math.Clamp(offset / length, 0, 1);

        if (slider.IsDirectionReversed)
        {
            ratio = 1 - ratio;
        }

        slider.Value = slider.Minimum + ((slider.Maximum - slider.Minimum) * ratio);
    }
}
