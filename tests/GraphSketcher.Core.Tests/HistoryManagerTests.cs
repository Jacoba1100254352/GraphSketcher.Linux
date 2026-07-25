using GraphSketcher.Core.Models;
using GraphSketcher.Core.Services;

namespace GraphSketcher.Core.Tests;

public sealed class HistoryManagerTests
{
    [Fact]
    public void UndoAndRedoReturnIndependentSnapshots()
    {
        var document = new GraphDocument { Title = "First" };
        var history = new HistoryManager(document);
        document.Title = "Second";
        history.Record(document);

        var undone = history.Undo();
        undone.Title = "Mutated returned value";
        var redone = history.Redo();

        Assert.Equal("Second", redone.Title);
        Assert.Equal("Second", history.Current.Title);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void RecordingDuplicateSnapshotDoesNothing()
    {
        var document = new GraphDocument();
        var history = new HistoryManager(document);

        var changed = history.Record(document);

        Assert.False(changed);
        Assert.Equal(0, history.UndoCount);
    }

    [Fact]
    public void RecordingAfterUndoClearsRedoBranch()
    {
        var document = new GraphDocument { Title = "One" };
        var history = new HistoryManager(document);
        document.Title = "Two";
        history.Record(document);
        history.Undo();
        document = history.Current;
        document.Title = "Alternate";

        history.Record(document);

        Assert.False(history.CanRedo);
        Assert.Equal("Alternate", history.Current.Title);
    }

    [Fact]
    public void CapacityDiscardsOldestUndoSnapshots()
    {
        var document = new GraphDocument { Title = "Zero" };
        var history = new HistoryManager(document, capacity: 2);
        foreach (var title in new[] { "One", "Two", "Three" })
        {
            document.Title = title;
            history.Record(document);
        }

        Assert.Equal(2, history.UndoCount);
        Assert.Equal("Two", history.Undo().Title);
        Assert.Equal("One", history.Undo().Title);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ResetReplacesCurrentAndClearsBothStacks()
    {
        var history = new HistoryManager(new GraphDocument { Title = "One" });
        history.Record(new GraphDocument { Title = "Two" });
        history.Undo();

        history.Reset(new GraphDocument { Title = "Reset" });

        Assert.Equal("Reset", history.Current.Title);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void EmptyUndoAndRedoThrow()
    {
        var history = new HistoryManager(new GraphDocument());

        Assert.Throws<InvalidOperationException>(() => history.Undo());
        Assert.Throws<InvalidOperationException>(() => history.Redo());
    }

    [Fact]
    public void InvalidCapacityIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new HistoryManager(new GraphDocument(), capacity: 0));
    }
}
