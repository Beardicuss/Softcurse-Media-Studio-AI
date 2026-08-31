using Xunit;

namespace SoftcurseMediaLabAI.Tests;

public sealed class BoundedMemoryHistoryTests
{
    private sealed record State(string Name, long Bytes);

    [Fact]
    public void OldestStatesAreEvictedWhenByteBudgetIsExceeded()
    {
        var history = new BoundedMemoryHistory<State>(10, 100, state => state.Bytes);
        history.Record(new State("first", 60));
        history.Record(new State("second", 60));

        Assert.Equal(1, history.UndoCount);
        Assert.Equal(60, history.RetainedBytes);
        Assert.True(history.TryUndo(new State("current", 20), out State? previous));
        Assert.Equal("second", previous!.Name);
    }

    [Fact]
    public void RecordingAfterUndoClearsRedoBranch()
    {
        var history = new BoundedMemoryHistory<State>(10, 1000, state => state.Bytes);
        history.Record(new State("one", 10));
        Assert.True(history.TryUndo(new State("two", 10), out _));
        Assert.Equal(1, history.RedoCount);

        history.Record(new State("branch", 10));

        Assert.Equal(0, history.RedoCount);
        Assert.False(history.TryRedo(new State("current", 10), out _));
    }

    [Fact]
    public void EntryCountIsBoundedIndependentlyOfBytes()
    {
        var history = new BoundedMemoryHistory<State>(2, 1000, state => state.Bytes);
        history.Record(new State("one", 1));
        history.Record(new State("two", 1));
        history.Record(new State("three", 1));
        Assert.Equal(2, history.UndoCount);
    }
}
