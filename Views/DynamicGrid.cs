using System.Windows;
using System.Windows.Controls;

namespace StarResonance.DPS.Views;

public class DynamicGrid : Grid
{
    public static readonly DependencyProperty ColumnDefinitionsSourceProperty =
        DependencyProperty.Register(
            nameof(ColumnDefinitionsSource),
            typeof(IEnumerable<GridLength>),
            typeof(DynamicGrid),
            new PropertyMetadata(null, OnColumnDefinitionsSourceChanged));

    public IEnumerable<GridLength> ColumnDefinitionsSource
    {
        get => (IEnumerable<GridLength>)GetValue(ColumnDefinitionsSourceProperty);
        set => SetValue(ColumnDefinitionsSourceProperty, value);
    }

    private static void OnColumnDefinitionsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DynamicGrid grid || e.NewValue is not IEnumerable<GridLength> columns) return;

        grid.ColumnDefinitions.Clear();
        foreach (var col in columns) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = col });
    }
}