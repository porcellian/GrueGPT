using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using GrueGPT.Core.Runtime;
using GrueGPT.Core.Stories;

namespace GrueGPT.Desktop;

public sealed partial class MainWindow : Window, IInterpreterIO, IZ3ScreenIO, IZ3SaveRestoreIO
{
    private readonly TextBlock _statusText;
    private readonly Border _playArea;
    private readonly TextBlock _screenText;
    private Z3ScreenSnapshot? _currentSnapshot;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _playArea = this.FindControl<Border>("PlayArea")!;
        _screenText = this.FindControl<TextBlock>("ScreenText")!;

        Z3ScreenModel initialScreen = new();
        initialScreen.SetStatus(new StoryStatus(string.Empty, 0, 0));
        initialScreen.BeginInput();
        PresentScreen(initialScreen.CreateSnapshot());
    }

    /// <summary>Renders a complete Version 3 screen snapshot from the interpreter core.</summary>
    public void PresentScreen(Z3ScreenSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
            return;
        }

        ApplySnapshot(snapshot);
    }

    private void ApplySnapshot(Z3ScreenSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        RenderScreen(snapshot);
    }

    private void RenderScreen(Z3ScreenSnapshot snapshot)
    {
        double availableWidth = Math.Max(1, (Bounds.Width > 0 ? Bounds.Width : Width) - 48);
        double availableHeight = Math.Max(1, (Bounds.Height > 0 ? Bounds.Height : Height) - 120);
        double widthBound = availableWidth / (snapshot.Columns * 0.62);
        double heightBound = availableHeight / (snapshot.Rows * 1.16);
        double fontSize = Math.Max(1, Math.Min(snapshot.FixedWidthFontSize, Math.Min(widthBound, heightBound)));

        _statusText.Text = snapshot.StatusLine;
        _statusText.Background = Brush.Parse(snapshot.StatusBackground);
        _statusText.Foreground = Brush.Parse(snapshot.StatusForeground);
        _statusText.FontFamily = new FontFamily(snapshot.FixedWidthFontFamily);
        _statusText.FontSize = fontSize;

        _playArea.Background = Brush.Parse(snapshot.ScreenBackground);
        _screenText.Foreground = Brush.Parse(snapshot.ScreenForeground);
        _screenText.FontFamily = new FontFamily(snapshot.FixedWidthFontFamily);

        _screenText.FontSize = fontSize;
        _screenText.LineHeight = fontSize * 1.16;
        _screenText.Text = FormatScreenText(snapshot);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_currentSnapshot is not null)
        {
            RenderScreen(_currentSnapshot);
        }
    }

    private static string FormatScreenText(Z3ScreenSnapshot snapshot)
    {
        List<string> lines = snapshot.UpperWindowLines
            .Concat(snapshot.LowerWindowLines)
            .ToList();

        if (snapshot.IsPromptVisible && lines.Count > 0)
        {
            int row = Math.Clamp(snapshot.PromptRow, 0, lines.Count - 1);
            int column = Math.Clamp(snapshot.PromptColumn, 0, snapshot.Columns - 1);
            StringBuilder promptLine = new(lines[row]);
            while (promptLine.Length < column)
            {
                promptLine.Append(' ');
            }

            if (promptLine.Length == column)
            {
                promptLine.Append('>');
            }
            else
            {
                promptLine[column] = '>';
            }

            lines[row] = promptLine.ToString();
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close();
}
