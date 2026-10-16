using System.Text;
using GrueGPT.Core.Runtime;

namespace GrueGPT.Core.Stories;

/// <summary>Maintains the Version 3 status row and character-cell output windows.</summary>
public sealed class Z3ScreenModel
{
    public const string ScreenBackground = "#0066CC";
    public const string ScreenForeground = "#FFFFFF";
    public const string StatusBackground = "#E8E8E8";
    public const string StatusForeground = "#0066CC";
    public const string FixedWidthFontFamily = "Courier New, Courier";
    public const int FixedWidthFontSize = 28;
    public const int DefaultColumns = 80;
    public const int DefaultRows = 25;

    private readonly List<StringBuilder> _upperLines = [];
    private readonly List<StringBuilder> _lowerLines = [new StringBuilder()];
    private readonly int[] _cursorRows = new int[2];
    private readonly int[] _cursorColumns = new int[2];
    private string _statusLine = string.Empty;
    private bool _previousCharacterWasCarriageReturn;

    public Z3ScreenModel(int columns = DefaultColumns, int rows = DefaultRows)
    {
        if (columns < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "The screen must have at least one column.");
        }

        if (rows < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), "The screen must have a status row and at least one output row.");
        }

        Columns = columns;
        Rows = rows;
    }

    public int Columns { get; }

    public int Rows { get; }

    public byte CurrentWindow { get; private set; }

    public int UpperWindowHeight { get; private set; }

    public bool IsPromptVisible { get; private set; }

    public int LowerWindowHeight => Rows - 1 - UpperWindowHeight;

    public void SetStatus(StoryStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        string rightText = status.IsTimeGame
            ? $"Time: {status.Score}:{status.Moves:00}"
            : $"Score: {status.Score}  Moves: {status.Moves}";
        rightText = rightText.Length > Columns ? rightText[^Columns..] : rightText;
        int locationWidth = Math.Max(0, Columns - rightText.Length - (rightText.Length < Columns ? 1 : 0));
        string location = TruncateLocation(status.Location, locationWidth);
        _statusLine = location.PadRight(locationWidth)
            + (locationWidth < Columns ? " " : string.Empty)
            + rightText;
        _statusLine = _statusLine.Length > Columns ? _statusLine[..Columns] : _statusLine.PadRight(Columns);
    }

    public void SplitWindow(int height)
    {
        if (height < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), "The upper window height cannot be negative.");
        }

        UpperWindowHeight = Math.Min(height, Rows - 1);
        _upperLines.Clear();
        _cursorRows[1] = 0;
        _cursorColumns[1] = 0;
        if (LowerWindowHeight > 0)
        {
            _cursorRows[0] = Math.Min(_cursorRows[0], LowerWindowHeight - 1);
            TrimLowerLines();
        }

        EnsureLineExists(_upperLines, 0);
    }

    public void Reset()
    {
        _upperLines.Clear();
        _lowerLines.Clear();
        _lowerLines.Add(new StringBuilder());
        Array.Clear(_cursorRows);
        Array.Clear(_cursorColumns);
        _statusLine = string.Empty;
        _previousCharacterWasCarriageReturn = false;
        UpperWindowHeight = 0;
        CurrentWindow = 0;
        IsPromptVisible = false;
    }

    public void SelectWindow(byte window)
    {
        if (window > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Version 3 has only the lower and upper output windows.");
        }

        CurrentWindow = window;
        if (window == 1)
        {
            _cursorRows[1] = 0;
            _cursorColumns[1] = 0;
            EnsureLineExists(_upperLines, 0);
        }
    }

    public void BeginInput() => IsPromptVisible = true;

    public void EndInput() => IsPromptVisible = false;

    public void WriteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<StringBuilder> lines = CurrentWindow == 1 ? _upperLines : _lowerLines;
        int windowHeight = CurrentWindow == 1 ? UpperWindowHeight : LowerWindowHeight;
        if (windowHeight == 0)
        {
            return;
        }

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];
            if (character == '\n' && _previousCharacterWasCarriageReturn)
            {
                _previousCharacterWasCarriageReturn = false;
                continue;
            }

            _previousCharacterWasCarriageReturn = character == '\r';
            if (character is '\r' or '\n')
            {
                MoveToNextLine(lines, windowHeight, CurrentWindow == 0);
                continue;
            }

            if (_cursorColumns[CurrentWindow] >= Columns)
            {
                MoveToNextLine(lines, windowHeight, CurrentWindow == 0);
            }

            EnsureLineExists(lines, _cursorRows[CurrentWindow]);
            StringBuilder line = lines[_cursorRows[CurrentWindow]];
            while (line.Length < _cursorColumns[CurrentWindow])
            {
                line.Append(' ');
            }

            if (_cursorColumns[CurrentWindow] == line.Length)
            {
                line.Append(character);
            }
            else
            {
                line[_cursorColumns[CurrentWindow]] = character;
            }

            _cursorColumns[CurrentWindow]++;
        }
    }

    public Z3ScreenSnapshot CreateSnapshot() => new(
        Rows,
        Columns,
        CurrentWindow,
        UpperWindowHeight,
        _statusLine,
        VisibleLines(_upperLines, UpperWindowHeight, padAtTop: false),
        VisibleLines(_lowerLines, LowerWindowHeight, padAtTop: true),
        UpperWindowHeight == 0 ? 0 : _cursorRows[1],
        _cursorColumns[1],
        LowerCursorRow,
        _cursorColumns[0],
        IsPromptVisible,
        Rows - 1,
        0,
        ScreenBackground,
        ScreenForeground,
        StatusBackground,
        StatusForeground,
        FixedWidthFontFamily,
        FixedWidthFontSize);

    private int LowerCursorRow => LowerWindowHeight == 0
        ? 0
        : Math.Min(
            LowerWindowHeight - 1,
            Math.Max(0, LowerWindowHeight - Math.Min(_lowerLines.Count, LowerWindowHeight) + _cursorRows[0]));

    private void TrimLowerLines()
    {
        while (_lowerLines.Count > LowerWindowHeight)
        {
            _lowerLines.RemoveAt(0);
            _cursorRows[0] = Math.Max(0, _cursorRows[0] - 1);
        }
    }

    private void MoveToNextLine(List<StringBuilder> lines, int windowHeight, bool scroll)
    {
        _cursorColumns[CurrentWindow] = 0;
        if (_cursorRows[CurrentWindow] < windowHeight - 1)
        {
            _cursorRows[CurrentWindow]++;
            EnsureLineExists(lines, _cursorRows[CurrentWindow]);
            return;
        }

        if (scroll && windowHeight > 0)
        {
            lines.RemoveAt(0);
            lines.Add(new StringBuilder());
            _cursorRows[CurrentWindow] = windowHeight - 1;
        }
        else if (windowHeight > 0)
        {
            _cursorRows[CurrentWindow] = windowHeight - 1;
        }
    }

    private static void EnsureLineExists(List<StringBuilder> lines, int index)
    {
        while (lines.Count <= index)
        {
            lines.Add(new StringBuilder());
        }
    }

    private static IReadOnlyList<string> VisibleLines(List<StringBuilder> source, int height, bool padAtTop)
    {
        if (height <= 0)
        {
            return [];
        }

        int firstLine = Math.Max(0, source.Count - height);
        List<string> visible = source.Skip(firstLine).Select(line => line.ToString()).ToList();
        if (visible.Count < height)
        {
            string[] blanks = Enumerable.Repeat(string.Empty, height - visible.Count).ToArray();
            if (padAtTop)
            {
                visible.InsertRange(0, blanks);
            }
            else
            {
                visible.AddRange(blanks);
            }
        }

        return visible;
    }

    private static string TruncateLocation(string location, int maximumLength)
    {
        if (maximumLength <= 0)
        {
            return string.Empty;
        }

        if (location.Length <= maximumLength)
        {
            return location;
        }

        if (maximumLength <= 3)
        {
            return location[..maximumLength];
        }

        string candidate = location[..(maximumLength - 3)];
        int lastSpace = candidate.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            candidate = candidate[..lastSpace];
        }

        return candidate + "...";
    }
}

/// <summary>An immutable view of the status row, output windows, prompt, and display style.</summary>
public sealed record Z3ScreenSnapshot(
    int Rows,
    int Columns,
    byte CurrentWindow,
    int UpperWindowHeight,
    string StatusLine,
    IReadOnlyList<string> UpperWindowLines,
    IReadOnlyList<string> LowerWindowLines,
    int UpperCursorRow,
    int UpperCursorColumn,
    int LowerCursorRow,
    int LowerCursorColumn,
    bool IsPromptVisible,
    int PromptRow,
    int PromptColumn,
    string ScreenBackground,
    string ScreenForeground,
    string StatusBackground,
    string StatusForeground,
    string FixedWidthFontFamily,
    int FixedWidthFontSize);
