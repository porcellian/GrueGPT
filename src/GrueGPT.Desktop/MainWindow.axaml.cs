using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using GrueGPT.Core.Runtime;
using GrueGPT.Core.Stories;

namespace GrueGPT.Desktop;

public sealed partial class MainWindow : Window, IInterpreterIO, IZ3ScreenIO, IZ3SaveRestoreIO
{
    private static readonly FilePickerFileType StoryFileType = new("Z-machine v3 stories")
    {
        Patterns = ["*.z3", "*.dat", "*.zork"],
        AppleUniformTypeIdentifiers = ["public.data"],
    };

    private static readonly FilePickerFileType SaveFileType = new("Quetzal save files")
    {
        Patterns = ["*.sav", "*.qzl", "*.ifzs"],
        AppleUniformTypeIdentifiers = ["public.data"],
    };

    private readonly TextBlock _statusText;
    private readonly Border _playArea;
    private readonly TextBlock _screenText;
    private readonly Grid _inputLine;
    private readonly TextBox _commandTextBox;
    private readonly Border _errorPanel;
    private readonly TextBlock _errorText;
    private readonly MenuItem _saveGameMenuItem;
    private readonly MenuItem _loadGameMenuItem;
    private readonly object _inputLock = new();
    private Z3ScreenSnapshot? _currentSnapshot;
    private TaskCompletionSource<string?>? _pendingCommand;
    private LoadedStory? _loadedStory;
    private Z3OpcodeExecutor? _executor;
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private bool _hostActionBusy;

    public MainWindow()
    {
        Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
        _statusText = this.FindControl<TextBlock>("StatusText")!;
        _playArea = this.FindControl<Border>("PlayArea")!;
        _screenText = this.FindControl<TextBlock>("ScreenText")!;
        _inputLine = this.FindControl<Grid>("InputLine")!;
        _commandTextBox = this.FindControl<TextBox>("CommandTextBox")!;
        _errorPanel = this.FindControl<Border>("ErrorPanel")!;
        _errorText = this.FindControl<TextBlock>("ErrorText")!;
        _saveGameMenuItem = this.FindControl<MenuItem>("SaveGameMenuItem")!;
        _loadGameMenuItem = this.FindControl<MenuItem>("LoadGameMenuItem")!;

        Z3ScreenModel initialScreen = new();
        initialScreen.SetStatus(new StoryStatus(string.Empty, 0, 0));
        initialScreen.BeginInput();
        PresentScreen(initialScreen.CreateSnapshot());
    }

    /// <summary>Renders a complete Version 3 screen snapshot from the interpreter core.</summary>
    public void PresentScreen(Z3ScreenSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        }
    }

    public ValueTask PresentScreenAsync(Z3ScreenSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PresentScreen(snapshot);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteTextAsync(string text, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateStatusAsync(StoryStatus status, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_inputLock)
        {
            if (_pendingCommand is not null)
            {
                throw new InvalidOperationException("The desktop host already has a pending command prompt.");
            }

            _pendingCommand = completion;
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((TaskCompletionSource<string?>)state!).TrySetResult(null),
            completion);
        Dispatcher.UIThread.Post(() =>
        {
            _commandTextBox.Text = string.Empty;
            _commandTextBox.IsEnabled = !_hostActionBusy;
            _inputLine.IsVisible = true;
            UpdateMenuAvailability();
            if (_commandTextBox.IsEnabled)
            {
                _commandTextBox.Focus();
            }
        });

        try
        {
            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_inputLock)
            {
                if (ReferenceEquals(_pendingCommand, completion))
                {
                    _pendingCommand = null;
                }
            }

            Dispatcher.UIThread.Post(() =>
            {
                _commandTextBox.IsEnabled = false;
                UpdateMenuAvailability();
            });
        }
    }

    public ValueTask ReportErrorAsync(string message, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Dispatcher.UIThread.Post(() => ShowError(message));
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> SaveGameAsync(ReadOnlyMemory<byte> saveFile, CancellationToken cancellationToken = default) =>
        new(InvokeOnUiThreadAsync(async () =>
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save game",
                SuggestedFileName = "GrueGPT.sav",
                DefaultExtension = "sav",
                FileTypeChoices = [SaveFileType],
                ShowOverwritePrompt = true,
            });
            if (file is null)
            {
                return false;
            }

            using (file)
            await using (Stream destination = await file.OpenWriteAsync())
            {
                await destination.WriteAsync(saveFile, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }));

    public ValueTask<byte[]?> LoadGameAsync(CancellationToken cancellationToken = default) =>
        new(InvokeOnUiThreadAsync(async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Load game",
                AllowMultiple = false,
                FileTypeFilter = [SaveFileType],
            });
            if (files.Count == 0)
            {
                return null;
            }

            using IStorageFile file = files[0];
            await using Stream source = await file.OpenReadAsync();
            using MemoryStream buffer = new();
            await source.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }));

    private void ApplySnapshot(Z3ScreenSnapshot snapshot)
    {
        _currentSnapshot = snapshot;
        RenderScreen(snapshot);
    }

    private void RenderScreen(Z3ScreenSnapshot snapshot)
    {
        double availableWidth = Math.Max(1, (Bounds.Width > 0 ? Bounds.Width : Width) - 48);
        double availableHeight = Math.Max(1, (Bounds.Height > 0 ? Bounds.Height : Height) - 160);
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

        _inputLine.IsVisible = snapshot.IsPromptVisible;
        _commandTextBox.FontFamily = new FontFamily(snapshot.FixedWidthFontFamily);
        _commandTextBox.FontSize = fontSize;
        _commandTextBox.Background = Brush.Parse(snapshot.ScreenBackground);
        _commandTextBox.Foreground = Brush.Parse(snapshot.ScreenForeground);
        _commandTextBox.CaretBrush = Brush.Parse(snapshot.ScreenForeground);
        _commandTextBox.IsEnabled = snapshot.IsPromptVisible && HasPendingCommand && !_hostActionBusy;
        UpdateMenuAvailability();
    }

    private static string FormatScreenText(Z3ScreenSnapshot snapshot)
    {
        IEnumerable<string> lines = snapshot.UpperWindowLines.Concat(snapshot.LowerWindowLines);
        if (snapshot.IsPromptVisible)
        {
            lines = lines.Take(Math.Clamp(snapshot.PromptRow, 0, snapshot.Rows - 1));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private bool HasPendingCommand
    {
        get
        {
            lock (_inputLock)
            {
                return _pendingCommand is not null;
            }
        }
    }

    private void UpdateMenuAvailability()
    {
        _saveGameMenuItem.IsEnabled = _executor is not null && HasPendingCommand && !_hostActionBusy;
        _loadGameMenuItem.IsEnabled = _loadedStory is not null && !_hostActionBusy;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (_currentSnapshot is not null)
        {
            RenderScreen(_currentSnapshot);
        }
    }

    private async void OnOpenStoryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open Z-machine v3 story",
                AllowMultiple = false,
                FileTypeFilter = [StoryFileType, new FilePickerFileType("All files") { Patterns = ["*.*"] }],
            });
            if (files.Count == 0)
            {
                return;
            }

            using IStorageFile file = files[0];
            await using Stream source = await file.OpenReadAsync();
            LoadedStory story = new Z3StoryLoader().Load(source);
            await StopCurrentSessionAsync();
            _loadedStory = story;
            Title = $"GrueGPT — {Path.GetFileNameWithoutExtension(file.Name)}";
            _errorPanel.IsVisible = false;
            StartSession(new Z3ExecutionState(story));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError($"Unable to open story: {exception.Message}");
        }
    }

    private async void OnSaveGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_executor is null || !HasPendingCommand)
        {
            ShowError("Save the game when the story is waiting for a command.");
            return;
        }

        await RunHostActionAsync(async () =>
        {
            if (!await _executor.SaveFromHostAsync())
            {
                return;
            }
        });
    }

    private async void OnLoadGameClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (_loadedStory is null)
        {
            ShowError("Open a story before loading a save game.");
            return;
        }

        await RunHostActionAsync(async () =>
        {
            byte[]? saveFile = await LoadGameAsync();
            if (saveFile is null)
            {
                return;
            }

            Z3ExecutionState candidate = new(_loadedStory);
            Z3RestoreResult result = Z3SaveRestoreService.TryRestore(candidate, saveFile);
            if (!result.Succeeded)
            {
                ShowError($"Unable to load save: {result.Error}");
                return;
            }

            await StopCurrentSessionAsync();
            StartSession(candidate);
        });
    }

    private async Task RunHostActionAsync(Func<Task> action)
    {
        _hostActionBusy = true;
        UpdateMenuAvailability();
        _commandTextBox.IsEnabled = false;
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            ShowError($"The save operation failed: {exception.Message}");
        }
        finally
        {
            _hostActionBusy = false;
            _commandTextBox.IsEnabled = HasPendingCommand && _inputLine.IsVisible;
            UpdateMenuAvailability();
            if (_commandTextBox.IsEnabled)
            {
                _commandTextBox.Focus();
            }
        }
    }

    private void StartSession(Z3ExecutionState state)
    {
        _executor = new Z3OpcodeExecutor(state, this);
        PresentScreen(_executor.Screen);
        _sessionCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _sessionCancellation.Token;
        Z3OpcodeExecutor executor = _executor;
        _sessionTask = Task.Run(async () =>
        {
            try
            {
                await executor.RunAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // The executor has already delivered the failure through ReportErrorAsync.
            }
            finally
            {
                Dispatcher.UIThread.Post(UpdateMenuAvailability);
            }
        });
        UpdateMenuAvailability();
    }

    private async Task StopCurrentSessionAsync()
    {
        _sessionCancellation?.Cancel();
        CompletePendingCommand(null);
        if (_sessionTask is not null)
        {
            await _sessionTask.ConfigureAwait(true);
        }

        _sessionCancellation?.Dispose();
        _sessionCancellation = null;
        _sessionTask = null;
        _executor = null;
    }

    private void SubmitCommand()
    {
        TaskCompletionSource<string?>? completion;
        lock (_inputLock)
        {
            completion = _pendingCommand;
            _pendingCommand = null;
        }

        if (completion is null)
        {
            return;
        }

        completion.TrySetResult(_commandTextBox.Text ?? string.Empty);
        _commandTextBox.IsEnabled = false;
        UpdateMenuAvailability();
    }

    private void CompletePendingCommand(string? command)
    {
        TaskCompletionSource<string?>? completion;
        lock (_inputLock)
        {
            completion = _pendingCommand;
            _pendingCommand = null;
        }

        completion?.TrySetResult(command);
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorPanel.IsVisible = true;
    }

    private async Task<T> InvokeOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return await action();
        }

        TaskCompletionSource<T> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                completion.TrySetResult(await action());
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return await completion.Task.ConfigureAwait(false);
    }

    private void OnCommandKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key == Key.Enter)
        {
            args.Handled = true;
            SubmitCommand();
        }
    }

    private void OnDismissErrorClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args) =>
        _errorPanel.IsVisible = false;

    private void OnExitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs args) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _sessionCancellation?.Cancel();
        CompletePendingCommand(null);
        base.OnClosed(e);
    }
}
