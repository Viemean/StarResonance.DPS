using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace StarResonance.DPS.Behaviors;

/// <summary>
/// 提供附加行为，为ScrollViewer或包含它的控件实现平滑滚动效果。
/// </summary>
public static class SmoothScrollViewerBehavior
{
    // 注册附加属性 IsEnabled，用于在XAML中启用或禁用此行为。
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollViewerBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    // 当IsEnabled属性变化时，附加或移除事件处理器。
    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 确保事件源是UIElement，并能找到其内部的ScrollViewer。
        if (sender is not UIElement uiElement || FindVisualChild<ScrollViewer>(uiElement) is not { } scrollViewer) return;

        // 计算目标滚动位置。
        var targetOffset = scrollViewer.VerticalOffset - e.Delta;

        // 创建一个动画来平滑地改变VerticalOffset。
        // 使用DoubleAnimation，这是WPF中实现动画的基本方式。
        var animation = new DoubleAnimation
        {
            To = targetOffset,
            Duration = new Duration(TimeSpan.FromMilliseconds(250)), // 持续时间可以调整以获得最佳手感
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } // 使用缓出效果，使滚动更自然
        };

        // 将动画应用到ScrollViewer的内部属性上。
        // 我们需要一个新的依赖属性来作为动画的目标。
        scrollViewer.BeginAnimation(ScrollableOffsetProperty, animation);

        // 标记事件已处理，防止默认的滚动行为发生。
        e.Handled = true;
    }
    
    // 查找指定类型的可视化子元素。
    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var childOfChild = FindVisualChild<T>(child);
            if (childOfChild != null)
            {
                return childOfChild;
            }
        }
        return null;
    }
    
    // 创建一个可用于动画的附加依赖属性。
    // 这是实现平滑滚动的关键，因为ScrollViewer.VerticalOffset本身不是一个依赖属性，不能直接作为动画目标。
    private static readonly DependencyProperty ScrollableOffsetProperty =
        DependencyProperty.RegisterAttached(
            "ScrollableOffset",
            typeof(double),
            typeof(SmoothScrollViewerBehavior),
            new PropertyMetadata(0.0, OnScrollableOffsetChanged));
    
    private static double GetScrollableOffset(DependencyObject obj) => (double)obj.GetValue(ScrollableOffsetProperty);
    private static void SetScrollableOffset(DependencyObject obj, double value) => obj.SetValue(ScrollableOffsetProperty, value);

    // 当动画更新ScrollableOffsetProperty时，实际调用ScrollViewer的ScrollToVerticalOffset方法来滚动。
    private static void OnScrollableOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToVerticalOffset((double)e.NewValue);
        }
    }
}