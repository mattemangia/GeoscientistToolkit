// GeoscientistToolkit/Util/UndoRedoManager.cs

using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Util;

/// <summary>
/// Interface for commands that can be undone/redone
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Execute the command
    /// </summary>
    void Execute();
    
    /// <summary>
    /// Undo the command
    /// </summary>
    void Undo();
    
    /// <summary>
    /// Optional: Description of the command for UI display
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Optional: Whether this command can be merged with the next one
    /// </summary>
    bool CanMergeWith(ICommand next) => false;
    
    /// <summary>
    /// Optional: Merge with the next command (for continuous operations like dragging)
    /// </summary>
    void MergeWith(ICommand next) { }
}

/// <summary>
/// Manages undo/redo operations using the Command pattern
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private int _maxHistorySize = 100;
    
    /// <summary>
    /// Maximum number of commands to keep in history
    /// </summary>
    public int MaxHistorySize
    {
        get => _maxHistorySize;
        set => _maxHistorySize = Math.Max(1, value);
    }
    
    /// <summary>
    /// Whether there are commands that can be undone
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;
    
    /// <summary>
    /// Whether there are commands that can be redone
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;
    
    /// <summary>
    /// Number of commands in undo history
    /// </summary>
    public int UndoCount => _undoStack.Count;
    
    /// <summary>
    /// Number of commands in redo history
    /// </summary>
    public int RedoCount => _redoStack.Count;
    
    /// <summary>
    /// Event fired when undo/redo state changes
    /// </summary>
    public event Action StateChanged;
    
    /// <summary>
    /// Execute a command and add it to the undo stack
    /// </summary>
    public void ExecuteCommand(ICommand command)
    {
        if (command == null)
            throw new ArgumentNullException(nameof(command));
        
        try
        {
            // Execute the command
            command.Execute();
            
            // Try to merge with previous command if possible
            if (_undoStack.Count > 0)
            {
                var previous = _undoStack.Peek();
                if (previous.CanMergeWith(command))
                {
                    previous.MergeWith(command);
                    StateChanged?.Invoke();
                    return;
                }
            }
            
            // Add to undo stack
            _undoStack.Push(command);
            
            // Clear redo stack (can't redo after new command)
            _redoStack.Clear();
            
            // Limit history size
            while (_undoStack.Count > _maxHistorySize)
            {
                var stack = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = stack.Length - 1; i > 0; i--)
                    _undoStack.Push(stack[i]);
            }
            
            StateChanged?.Invoke();
            Logger.Log($"Executed: {command.Description}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to execute command '{command.Description}': {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Undo the last command
    /// </summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            Logger.LogWarning("Nothing to undo");
            return;
        }
        
        try
        {
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            
            StateChanged?.Invoke();
            Logger.Log($"Undone: {command.Description}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to undo: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Redo the last undone command
    /// </summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            Logger.LogWarning("Nothing to redo");
            return;
        }
        
        try
        {
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            
            StateChanged?.Invoke();
            Logger.Log($"Redone: {command.Description}");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to redo: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Clear all undo/redo history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke();
        Logger.Log("Undo/redo history cleared");
    }
    
    /// <summary>
    /// Get description of the command that would be undone
    /// </summary>
    public string GetUndoDescription()
    {
        return CanUndo ? _undoStack.Peek().Description : "";
    }
    
    /// <summary>
    /// Get description of the command that would be redone
    /// </summary>
    public string GetRedoDescription()
    {
        return CanRedo ? _redoStack.Peek().Description : "";
    }
    
    /// <summary>
    /// Get all undo command descriptions (for debugging/UI)
    /// </summary>
    public List<string> GetUndoHistory()
    {
        return _undoStack.Select(c => c.Description).ToList();
    }
    
    /// <summary>
    /// Get all redo command descriptions (for debugging/UI)
    /// </summary>
    public List<string> GetRedoHistory()
    {
        return _redoStack.Select(c => c.Description).ToList();
    }
}

/// <summary>
/// Base class for commands that provides common functionality
/// </summary>
public abstract class CommandBase : ICommand
{
    public abstract void Execute();
    public abstract void Undo();
    public abstract string Description { get; }
    
    public virtual bool CanMergeWith(ICommand next) => false;
    public virtual void MergeWith(ICommand next) { }
}

/// <summary>
/// Simple command that executes and undoes using delegates
/// </summary>
public class DelegateCommand : CommandBase
{
    private readonly Action _execute;
    private readonly Action _undo;
    private readonly string _description;
    
    public DelegateCommand(Action execute, Action undo, string description)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        _description = description ?? "Command";
    }
    
    public override void Execute() => _execute();
    public override void Undo() => _undo();
    public override string Description => _description;
}

/// <summary>
/// Command that stores old and new state for simple property changes
/// </summary>
public class PropertyChangeCommand<T> : CommandBase
{
    private readonly Action<T> _setter;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly string _propertyName;
    
    public PropertyChangeCommand(Action<T> setter, T oldValue, T newValue, string propertyName)
    {
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _oldValue = oldValue;
        _newValue = newValue;
        _propertyName = propertyName ?? "Property";
    }
    
    public override void Execute() => _setter(_newValue);
    public override void Undo() => _setter(_oldValue);
    public override string Description => $"Change {_propertyName}";
}

/// <summary>
/// Command for adding an item to a collection
/// </summary>
public class AddItemCommand<T> : CommandBase
{
    private readonly ICollection<T> _collection;
    private readonly T _item;
    private readonly string _itemName;
    
    public AddItemCommand(ICollection<T> collection, T item, string itemName = null)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _item = item;
        _itemName = itemName ?? "Item";
    }
    
    public override void Execute() => _collection.Add(_item);
    public override void Undo() => _collection.Remove(_item);
    public override string Description => $"Add {_itemName}";
}

/// <summary>
/// Command for removing an item from a collection
/// </summary>
public class RemoveItemCommand<T> : CommandBase
{
    private readonly ICollection<T> _collection;
    private readonly T _item;
    private readonly string _itemName;
    
    public RemoveItemCommand(ICollection<T> collection, T item, string itemName = null)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _item = item;
        _itemName = itemName ?? "Item";
    }
    
    public override void Execute() => _collection.Remove(_item);
    public override void Undo() => _collection.Add(_item);
    public override string Description => $"Remove {_itemName}";
}

/// <summary>
/// Command for inserting an item into a list at a specific index
/// </summary>
public class InsertItemCommand<T> : CommandBase
{
    private readonly IList<T> _list;
    private readonly int _index;
    private readonly T _item;
    private readonly string _itemName;

    public InsertItemCommand(IList<T> list, int index, T item, string itemName = null)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _index = index;
        _item = item;
        _itemName = itemName ?? "Item";
    }

    public override void Execute() => _list.Insert(_index, _item);
    public override void Undo() => _list.RemoveAt(_index);
    public override string Description => $"Insert {_itemName}";
}

/// <summary>
/// Command for removing an item from a list at a specific index
/// </summary>
public class RemoveItemAtCommand<T> : CommandBase
{
    private readonly IList<T> _list;
    private readonly int _index;
    private T _item; // Store the item to re-insert it
    private readonly string _itemName;

    public RemoveItemAtCommand(IList<T> list, int index, string itemName = null)
    {
        _list = list ?? throw new ArgumentNullException(nameof(list));
        _index = index;
        _itemName = itemName ?? "Item";
    }
    
    public override void Execute()
    {
        // Store item before removing
        _item = _list[_index];
        _list.RemoveAt(_index);
    }
    public override void Undo() => _list.Insert(_index, _item);
    public override string Description => $"Remove {_itemName}";
}

/// <summary>
/// Command group that executes multiple commands as one atomic operation
/// </summary>
public class CompositeCommand : CommandBase
{
    private readonly List<ICommand> _commands = new();
    private readonly string _description;
    
    public CompositeCommand(string description = "Multiple Changes")
    {
        _description = description;
    }
    
    public void AddCommand(ICommand command)
    {
        _commands.Add(command);
    }
    
    public override void Execute()
    {
        foreach (var command in _commands)
            command.Execute();
    }
    
    public override void Undo()
    {
        // Undo in reverse order
        for (int i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
    
    public override string Description => _description;
}

/// <summary>
/// Command that supports continuous operations (like dragging)
/// Can be merged with subsequent similar commands
/// </summary>
public class ContinuousCommand : CommandBase
{
    private readonly Action _execute;
    private readonly Action _undo;
    private readonly string _description;
    private readonly Func<ICommand, bool> _canMerge;
    
    public ContinuousCommand(
        Action execute, 
        Action undo, 
        string description,
        Func<ICommand, bool> canMerge = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        _description = description ?? "Continuous Command";
        _canMerge = canMerge;
    }
    
    public override void Execute() => _execute();
    public override void Undo() => _undo();
    public override string Description => _description;
    
    public override bool CanMergeWith(ICommand next)
    {
        if (_canMerge == null) return false;
        return _canMerge(next);
    }
    
    public override void MergeWith(ICommand next)
    {
        // The next command's execute action becomes the new execute
        if (next is ContinuousCommand continuous)
        {
            // Update with the new end state
            next.Execute();
        }
    }
}