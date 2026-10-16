using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GrueGPT.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
