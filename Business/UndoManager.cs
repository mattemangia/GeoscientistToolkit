// GeoscientistToolkit/Business/UndoManager.cs
using System.Collections.Generic;
using GeoscientistToolkit.Util;

namespace GeoscientistToolkit.Business
{
    /// <summary>
    /// Represents an action that can be executed and un-executed.
    /// </summary>
    public interface ICommand
    {
        void Execute();
        void UnExecute();
    }
    
    /// <summary>
    /// Manages undo and redo operations using the Command Pattern.
    /// </summary>
    public class UndoManager
    {
        private readonly Stack<ICommand> _undoStack = new();
        private readonly Stack<ICommand> _redoStack = new();
        private int _historyLimit;
        
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        public UndoManager(int historyLimit)
        {
            _historyLimit = historyLimit;
        }

        /// <summary>
        /// Executes a command and adds it to the undo history.
        /// </summary>
        public void Do(ICommand command)
        {
            command.Execute();
            _undoStack.Push(command);
            _redoStack.Clear(); // Any new action clears the redo history
            
            TrimHistory();
            ProjectManager.Instance.HasUnsavedChanges = true;
        }

        /// <summary>
        /// Reverts the last executed command.
        /// </summary>
        public void Undo()
        {
            if (CanUndo)
            {
                var command = _undoStack.Pop();
                command.UnExecute();
                _redoStack.Push(command);
            }
        }

        /// <summary>
        /// Re-applies the last undone command.
        /// </summary>
        public void Redo()
        {
            if (CanRedo)
            {
                var command = _redoStack.Pop();
                command.Execute();
                _undoStack.Push(command);
            }
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        public void UpdateHistoryLimit(int newLimit)
        {
            _historyLimit = newLimit;
            TrimHistory();
            Logger.Log($"Undo history limit updated to: {_historyLimit}");
        }

        private void TrimHistory()
        {
            // This is complex to do on a Stack without converting to another collection.
            // A simpler approach is to use a List and manage it like a stack.
            // For now, we'll keep it simple. A more robust solution might use a LinkedList.
            if (_undoStack.Count > _historyLimit)
            {
                // To remove from the bottom, we have to reverse, trim, and reverse back.
                var tempList = new List<ICommand>(_undoStack);
                tempList.Reverse(); // Now oldest is at index 0
                while (tempList.Count > _historyLimit)
                {
                    tempList.RemoveAt(0);
                }
                tempList.Reverse(); // Back to stack order
                _undoStack.Clear();
                foreach (var item in tempList) _undoStack.Push(item);
            }
        }
    }
}