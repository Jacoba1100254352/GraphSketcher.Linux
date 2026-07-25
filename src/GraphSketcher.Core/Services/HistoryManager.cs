using GraphSketcher.Core.Models;

namespace GraphSketcher.Core.Services;

/// <summary>
/// Maintains bounded, immutable JSON snapshots for document undo and redo.
/// </summary>
public sealed class HistoryManager
{
    private readonly object _syncRoot = new();
    private readonly List<string> _undoSnapshots = [];
    private readonly List<string> _redoSnapshots = [];
    private string _currentSnapshot;

    public HistoryManager(GraphDocument initialDocument, int capacity = 100)
    {
        ArgumentNullException.ThrowIfNull(initialDocument);
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(capacity),
                "History capacity must be at least one.");
        }

        Capacity = capacity;
        _currentSnapshot = DocumentSerializer.Serialize(initialDocument, indented: false);
    }

    public int Capacity { get; }

    public GraphDocument Current
    {
        get
        {
            lock (_syncRoot)
            {
                return DocumentSerializer.Deserialize(_currentSnapshot);
            }
        }
    }

    public bool CanUndo
    {
        get
        {
            lock (_syncRoot)
            {
                return _undoSnapshots.Count > 0;
            }
        }
    }

    public bool CanRedo
    {
        get
        {
            lock (_syncRoot)
            {
                return _redoSnapshots.Count > 0;
            }
        }
    }

    public int UndoCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _undoSnapshots.Count;
            }
        }
    }

    public int RedoCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _redoSnapshots.Count;
            }
        }
    }

    /// <summary>
    /// Records a new document state and clears the redo branch.
    /// </summary>
    /// <returns>True when the state differed from the current snapshot.</returns>
    public bool Record(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var snapshot = DocumentSerializer.Serialize(document, indented: false);

        lock (_syncRoot)
        {
            if (string.Equals(snapshot, _currentSnapshot, StringComparison.Ordinal))
            {
                return false;
            }

            _undoSnapshots.Add(_currentSnapshot);
            if (_undoSnapshots.Count > Capacity)
            {
                _undoSnapshots.RemoveAt(0);
            }

            _currentSnapshot = snapshot;
            _redoSnapshots.Clear();
            return true;
        }
    }

    public GraphDocument Undo()
    {
        lock (_syncRoot)
        {
            if (_undoSnapshots.Count == 0)
            {
                throw new InvalidOperationException("There is no document state to undo.");
            }

            _redoSnapshots.Add(_currentSnapshot);
            _currentSnapshot = Pop(_undoSnapshots);
            return DocumentSerializer.Deserialize(_currentSnapshot);
        }
    }

    public GraphDocument Redo()
    {
        lock (_syncRoot)
        {
            if (_redoSnapshots.Count == 0)
            {
                throw new InvalidOperationException("There is no document state to redo.");
            }

            _undoSnapshots.Add(_currentSnapshot);
            _currentSnapshot = Pop(_redoSnapshots);
            return DocumentSerializer.Deserialize(_currentSnapshot);
        }
    }

    public void Reset(GraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var snapshot = DocumentSerializer.Serialize(document, indented: false);

        lock (_syncRoot)
        {
            _currentSnapshot = snapshot;
            _undoSnapshots.Clear();
            _redoSnapshots.Clear();
        }
    }

    private static string Pop(List<string> snapshots)
    {
        var index = snapshots.Count - 1;
        var snapshot = snapshots[index];
        snapshots.RemoveAt(index);
        return snapshot;
    }
}
