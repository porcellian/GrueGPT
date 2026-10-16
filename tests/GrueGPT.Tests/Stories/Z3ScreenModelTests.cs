using GrueGPT.Core.Runtime;
using GrueGPT.Core.Stories;
using Xunit;

namespace GrueGPT.Tests.Stories;

public sealed class Z3ScreenModelTests
{
    [Fact]
    public void SnapshotUsesReferencePaletteFixedWidthFontAndBottomAnchoredLowerWindow()
    {
        Z3ScreenModel screen = new(columns: 20, rows: 5);

        Z3ScreenSnapshot snapshot = screen.CreateSnapshot();

        Assert.Equal(20, snapshot.Columns);
        Assert.Equal(5, snapshot.Rows);
        Assert.Equal(4, snapshot.LowerWindowLines.Count);
        Assert.Equal(new[] { "", "", "", "" }, snapshot.LowerWindowLines);
        Assert.Equal(3, snapshot.LowerCursorRow);
        Assert.Equal(0, snapshot.LowerCursorColumn);
        Assert.Equal(0, snapshot.UpperCursorRow);
        Assert.Equal("#0066CC", snapshot.ScreenBackground);
        Assert.Equal("#FFFFFF", snapshot.ScreenForeground);
        Assert.Equal("#E8E8E8", snapshot.StatusBackground);
        Assert.Equal("#0066CC", snapshot.StatusForeground);
        Assert.Equal("Courier New, Courier", snapshot.FixedWidthFontFamily);
        Assert.Equal(28, snapshot.FixedWidthFontSize);
        Assert.Equal(4, snapshot.PromptRow);
        Assert.Equal(0, snapshot.PromptColumn);
    }

    [Fact]
    public void FormatsScoreAndMoveStatusAcrossTheAvailableWidth()
    {
        Z3ScreenModel screen = new(columns: 40, rows: 10);

        screen.SetStatus(new StoryStatus("West of House", 0, 12));

        string status = screen.CreateSnapshot().StatusLine;
        Assert.StartsWith("West of House", status);
        Assert.EndsWith("Score: 0  Moves: 12", status);
        Assert.Equal(40, status.Length);
    }

    [Fact]
    public void FormatsTimeAndTruncatesLongLocationAtAWordBoundary()
    {
        Z3ScreenModel screen = new(columns: 38, rows: 10);

        screen.SetStatus(new StoryStatus("A Very Long Location Name That Cannot Fit", 4, 5, IsTimeGame: true));

        string status = screen.CreateSnapshot().StatusLine;
        Assert.StartsWith("A Very Long Location...", status);
        Assert.EndsWith("Time: 4:05", status);
        Assert.Equal(38, status.Length);
    }

    [Fact]
    public void LowerWindowScrollsOlderLinesUpAndWrapsAtTheScreenWidth()
    {
        Z3ScreenModel screen = new(columns: 5, rows: 3);

        screen.WriteText("one\ntwo\nthree\nfour");

        Assert.Equal(new[] { "three", "four" }, screen.CreateSnapshot().LowerWindowLines);
    }

    [Fact]
    public void LowerWindowWrapsLongLinesBeforeScrolling()
    {
        Z3ScreenModel screen = new(columns: 4, rows: 3);

        screen.WriteText("ABCDE");

        Assert.Equal(new[] { "ABCD", "E" }, screen.CreateSnapshot().LowerWindowLines);
    }

    [Fact]
    public void UpperWindowStartsAtTopAndSplitClearsItWithoutMovingLowerCursor()
    {
        Z3ScreenModel screen = new(columns: 10, rows: 5);
        screen.SplitWindow(2);
        screen.SelectWindow(1);
        screen.WriteText("upper");
        screen.SelectWindow(0);
        screen.WriteText("lower");

        screen.SplitWindow(2);

        Z3ScreenSnapshot snapshot = screen.CreateSnapshot();
        Assert.Equal(2, snapshot.UpperWindowHeight);
        Assert.Equal(new[] { "", "" }, snapshot.UpperWindowLines);
        Assert.Equal(new[] { "", "lower" }, snapshot.LowerWindowLines);
    }

    [Fact]
    public void UpperWindowOverlaysAtItsBottomInsteadOfScrolling()
    {
        Z3ScreenModel screen = new(columns: 5, rows: 4);
        screen.SplitWindow(2);
        screen.SelectWindow(1);

        screen.WriteText("A\nB\nC");

        Assert.Equal(new[] { "A", "C" }, screen.CreateSnapshot().UpperWindowLines);
    }

    [Fact]
    public void PromptCanBeShownAndClearedIndependentlyOfOutputText()
    {
        Z3ScreenModel screen = new(columns: 20, rows: 5);

        screen.BeginInput();
        Assert.True(screen.CreateSnapshot().IsPromptVisible);
        screen.EndInput();

        Assert.False(screen.CreateSnapshot().IsPromptVisible);
    }

    [Fact]
    public void ResetClearsWindowsStatusAndPromptWhileKeepingDimensions()
    {
        Z3ScreenModel screen = new(columns: 20, rows: 5);
        screen.SetStatus(new StoryStatus("Room", 4, 5));
        screen.SplitWindow(2);
        screen.SelectWindow(1);
        screen.WriteText("upper");
        screen.BeginInput();

        screen.Reset();

        Z3ScreenSnapshot snapshot = screen.CreateSnapshot();
        Assert.Equal(20, snapshot.Columns);
        Assert.Equal(5, snapshot.Rows);
        Assert.Equal(0, snapshot.UpperWindowHeight);
        Assert.Equal((byte)0, snapshot.CurrentWindow);
        Assert.Equal(string.Empty, snapshot.StatusLine);
        Assert.False(snapshot.IsPromptVisible);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(5, 1)]
    public void RejectsInvalidScreenDimensions(int columns, int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Z3ScreenModel(columns, rows));
    }
}
