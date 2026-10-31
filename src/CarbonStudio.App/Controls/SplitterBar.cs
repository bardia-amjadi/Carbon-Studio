using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CarbonStudio.App.Controls;

/// <summary>
/// A thin draggable divider. It reports the cumulative drag distance in DIPs; the owner turns that
/// into a new column width or row height. (WinUI 3 has no built-in GridSplitter.)
/// </summary>
public sealed partial class SplitterBar : Grid
{
    private readonly Rectangle _line = new();
    private bool _dragging;
    private double _startCoordinate;
    private Orientation _orientation = Orientation.Vertical;

    public SplitterBar()
    {
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _line.Fill = (Brush)Application.Current.Resources["CarbonBorderBrush"];
        Children.Add(_line);
        ApplyOrientation();

        PointerEntered += (_, _) => _line.Fill = (Brush)Application.Current.Resources["CarbonAccentBrush"];
        PointerExited += (_, _) =>
        {
            if (!_dragging)
            {
                _line.Fill = (Brush)Application.Current.Resources["CarbonBorderBrush"];
            }
        };
    }

    /// <summary>Raised when a drag begins so the owner can record the starting size.</summary>
    public event EventHandler? DragStarted;

    /// <summary>Raised repeatedly while dragging with the distance moved since the drag started.</summary>
    public event EventHandler<double>? DragDelta;

    /// <summary>Raised once when the pointer is released.</summary>
    public event EventHandler? DragCompleted;

    /// <summary><see cref="Orientation.Vertical"/> is a vertical bar that resizes horizontally.</summary>
    public Orientation BarOrientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            ApplyOrientation();
        }
    }

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (CapturePointer(e.Pointer))
        {
            _dragging = true;
            _startCoordinate = Coordinate(e);
            DragStarted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging)
        {
            DragDelta?.Invoke(this, Coordinate(e) - _startCoordinate);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e) => EndDrag(e);

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e) => EndDrag(e);

    private void EndDrag(PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleasePointerCaptures();
        _line.Fill = (Brush)Application.Current.Resources["CarbonBorderBrush"];
        DragCompleted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    // Window-relative coordinates stay stable while the bar itself moves during a drag.
    private double Coordinate(PointerRoutedEventArgs e)
    {
        var position = e.GetCurrentPoint(null).Position;
        return _orientation == Orientation.Vertical ? position.X : position.Y;
    }

    private void ApplyOrientation()
    {
        if (_orientation == Orientation.Vertical)
        {
            Width = 5;
            Height = double.NaN;
            _line.Width = 1;
            _line.Height = double.NaN;
            _line.HorizontalAlignment = HorizontalAlignment.Center;
            _line.VerticalAlignment = VerticalAlignment.Stretch;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        }
        else
        {
            Height = 5;
            Width = double.NaN;
            _line.Height = 1;
            _line.Width = double.NaN;
            _line.HorizontalAlignment = HorizontalAlignment.Stretch;
            _line.VerticalAlignment = VerticalAlignment.Center;
            ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
        }
    }
}
