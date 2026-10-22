namespace Kinovea.PortableAnalysis;

/// <summary>State history for immutable or caller-owned snapshots. Pushing a state clears redo.</summary>
public sealed class UndoHistory<T>
{
    private readonly Stack<(T State, string Name)> undo = new();
    private readonly Stack<(T State, string Name)> redo = new();
    public T Current { get; private set; }
    public bool CanUndo => undo.Count != 0;
    public bool CanRedo => redo.Count != 0;
    public string? UndoName => CanUndo ? undo.Peek().Name : null;
    public string? RedoName => CanRedo ? redo.Peek().Name : null;

    public UndoHistory(T initialState) => Current = initialState;

    public void Push(T nextState, string actionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        undo.Push((Current, actionName));
        Current = nextState;
        redo.Clear();
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        redo.Push((Current, undo.Peek().Name));
        Current = undo.Pop().State;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        undo.Push((Current, redo.Peek().Name));
        Current = redo.Pop().State;
        return true;
    }
}
