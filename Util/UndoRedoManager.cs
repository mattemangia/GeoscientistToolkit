// GeoscientistToolkit/Util/UndoRedoManager.cs

namespace GeoscientistToolkit.Util;

/// <summary>
///     Defines a command that can be executed and un-executed.
/// </summary>
public interface ICommand
{
    void Execute();
    void Undo();
}

/// <summary>
///     Manages undo and redo operations for a series of commands.
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<ICommand> _redoStack = new();
    private readonly Stack<ICommand> _undoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Execute(ICommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear(); // New action clears the redo stack
        Logger.Log("Executed command, cleared redo stack.");
    }

    public void Undo()
    {
        if (CanUndo)
        {
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            Logger.Log("Undid command.");
        }
    }

    public void Redo()
    {
        if (CanRedo)
        {
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            Logger.Log("Redid command.");
        }
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}