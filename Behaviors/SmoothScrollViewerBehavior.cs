using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StarResonance.DPS.Behaviors;

/// <summary>
/// 提供基于物理插值的逐帧平滑滚动行为，以实现极致流畅的滚动体验。
/// </summary>
public static class SmoothScrollViewerBehavior
{
    // 依赖属性：IsEnabled
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(SmoothScrollViewerBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    // 依赖属性：TargetVerticalOffset (内部使用)
    private static readonly DependencyProperty TargetVerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "TargetVerticalOffset", typeof(double), typeof(SmoothScrollViewerBehavior),
            new PropertyMetadata(0.0));

    private static double GetTargetVerticalOffset(DependencyObject obj) =>
        (double)obj.GetValue(TargetVerticalOffsetProperty);

    private static void SetTargetVerticalOffset(DependencyObject obj, double value) =>
        obj.SetValue(TargetVerticalOffsetProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;

        if (e.NewValue is true)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
            element.Loaded += OnElementLoaded;
            element.Unloaded += OnElementUnloaded;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
            element.Loaded -= OnElementLoaded;
            element.Unloaded -= OnElementUnloaded;
            CompositionTarget.Rendering -= GetRenderingEventHandler(element);
        }
    }

    // 为每个UI元素存储其对应的事件处理器，以便正确移除
    private static readonly DependencyProperty RenderingEventHandlerProperty =
        DependencyProperty.RegisterAttached("RenderingEventHandler", typeof(EventHandler),
            typeof(SmoothScrollViewerBehavior));

    private static EventHandler GetRenderingEventHandler(DependencyObject obj) =>
        (EventHandler)obj.GetValue(RenderingEventHandlerProperty);

    private static void SetRenderingEventHandler(DependencyObject obj, EventHandler value) =>
        obj.SetValue(RenderingEventHandlerProperty, value);

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        var handler = new EventHandler((_, _) => Rendering(element));
        SetRenderingEventHandler(element, handler);
        CompositionTarget.Rendering += handler;

        // 初始化目标偏移量为当前滚动位置
        if (FindVisualChild<ScrollViewer>(element) is { } scrollViewer)
        {
            SetTargetVerticalOffset(scrollViewer, scrollViewer.VerticalOffset);
        }
    }

    private static void OnElementUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;

        CompositionTarget.Rendering -= GetRenderingEventHandler(element);
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not UIElement uiElement || FindVisualChild<ScrollViewer>(uiElement) is not { } scrollViewer)
        {
            return;
        }

        // 获取当前的目标位置
        var targetOffset = GetTargetVerticalOffset(scrollViewer);

        // 定义滚动速率因子。0.5 代表每次滚动半个可视区域的高度。
        const double scrollFactor = 0.5;
        var scrollAmount = scrollViewer.ViewportHeight * scrollFactor;

        // 根据滚轮方向更新目标位置
        targetOffset -= Math.Sign(e.Delta) * scrollAmount;

        // 限制目标位置在有效范围内 (0 到 最大滚动位置)
        targetOffset = Math.Max(0, Math.Min(scrollViewer.ScrollableHeight, targetOffset));

        // 更新目标位置
        SetTargetVerticalOffset(scrollViewer, targetOffset);

        e.Handled = true;
    }

    private static void Rendering(object sender)
    {
        if (sender is not FrameworkElement element || FindVisualChild<ScrollViewer>(element) is not { } scrollViewer)
        {
            return;
        }

        var currentOffset = scrollViewer.VerticalOffset;
        var targetOffset = GetTargetVerticalOffset(scrollViewer);

        // 如果差距很小，直接定位并停止进一步计算，以优化性能
        if (Math.Abs(currentOffset - targetOffset) < 0.5)
        {
            scrollViewer.ScrollToVerticalOffset(targetOffset);
            return;
        }

        // 核心：使用线性插值(Lerp)在每一帧都向目标位置靠近一点
        // 0.2 这个值可以调整，值越大，“吸附”感越强，动画越快
        var newOffset = currentOffset + (targetOffset - currentOffset) * 0.2;

        scrollViewer.ScrollToVerticalOffset(newOffset);
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
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
}