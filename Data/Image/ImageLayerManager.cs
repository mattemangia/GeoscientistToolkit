// GeoscientistToolkit/Data/Image/ImageLayerManager.cs

using System;
using System.Collections.Generic;
using System.Linq;

namespace GeoscientistToolkit.Data.Image
{
    /// <summary>
    /// Manages layers for an ImageDataset
    /// Supports layer operations, blending, and compositing
    /// </summary>
    public class ImageLayerManager : IDisposable
    {
        private List<ImageLayer> _layers = new();
        private int _activeLayerIndex = 0;
        private int _width;
        private int _height;

        // Undo/redo support
        private Stack<List<ImageLayer>> _undoStack = new();
        private Stack<List<ImageLayer>> _redoStack = new();
        private const int MaxUndoSteps = 50;

        public ImageLayerManager(int width, int height)
        {
            _width = width;
            _height = height;

            // Create background layer
            var backgroundLayer = new ImageLayer("Background", width, height);
            backgroundLayer.Fill(255, 255, 255, 255);
            _layers.Add(backgroundLayer);
        }

        public List<ImageLayer> Layers => _layers;
        public int ActiveLayerIndex
        {
            get => _activeLayerIndex;
            set => _activeLayerIndex = Math.Clamp(value, 0, _layers.Count - 1);
        }

        public ImageLayer ActiveLayer => _layers[_activeLayerIndex];
        public int Width => _width;
        public int Height => _height;

        /// <summary>
        /// Add a new layer
        /// </summary>
        public ImageLayer AddLayer(string name, int? insertIndex = null)
        {
            SaveUndoState();

            var layer = new ImageLayer(name, _width, _height);
            int index = insertIndex ?? _activeLayerIndex + 1;
            _layers.Insert(index, layer);
            _activeLayerIndex = index;

            return layer;
        }

        /// <summary>
        /// Add an existing layer
        /// </summary>
        public void AddExistingLayer(ImageLayer layer, int? insertIndex = null)
        {
            SaveUndoState();

            int index = insertIndex ?? _activeLayerIndex + 1;
            _layers.Insert(index, layer);
            _activeLayerIndex = index;
        }

        /// <summary>
        /// Remove a layer
        /// </summary>
        public void RemoveLayer(int index)
        {
            if (_layers.Count <= 1)
                throw new InvalidOperationException("Cannot remove the last layer");

            SaveUndoState();

            _layers[index].Dispose();
            _layers.RemoveAt(index);

            if (_activeLayerIndex >= _layers.Count)
                _activeLayerIndex = _layers.Count - 1;
        }

        /// <summary>
        /// Duplicate a layer
        /// </summary>
        public ImageLayer DuplicateLayer(int index)
        {
            SaveUndoState();

            var clone = _layers[index].Clone();
            _layers.Insert(index + 1, clone);
            _activeLayerIndex = index + 1;

            return clone;
        }

        /// <summary>
        /// Move layer up in stack
        /// </summary>
        public void MoveLayerUp(int index)
        {
            if (index >= _layers.Count - 1) return;

            SaveUndoState();

            var layer = _layers[index];
            _layers.RemoveAt(index);
            _layers.Insert(index + 1, layer);

            if (_activeLayerIndex == index)
                _activeLayerIndex = index + 1;
        }

        /// <summary>
        /// Move layer down in stack
        /// </summary>
        public void MoveLayerDown(int index)
        {
            if (index <= 0) return;

            SaveUndoState();

            var layer = _layers[index];
            _layers.RemoveAt(index);
            _layers.Insert(index - 1, layer);

            if (_activeLayerIndex == index)
                _activeLayerIndex = index - 1;
        }

        /// <summary>
        /// Merge layer down
        /// </summary>
        public void MergeDown(int index)
        {
            if (index <= 0 || index >= _layers.Count)
                throw new InvalidOperationException("Cannot merge this layer");

            SaveUndoState();

            var topLayer = _layers[index];
            var bottomLayer = _layers[index - 1];

            // Blend layers
            byte[] merged = LayerBlending.Blend(bottomLayer, topLayer);

            // Update bottom layer with merged result
            Array.Copy(merged, bottomLayer.Data, merged.Length);

            // Remove top layer
            topLayer.Dispose();
            _layers.RemoveAt(index);

            _activeLayerIndex = index - 1;
        }

        /// <summary>
        /// Flatten all layers into one
        /// </summary>
        public ImageLayer FlattenImage()
        {
            SaveUndoState();

            byte[] composite = CompositeAllLayers();

            // Replace all layers with single flattened layer
            foreach (var layer in _layers)
                layer.Dispose();

            _layers.Clear();

            var flattened = new ImageLayer("Flattened", composite, _width, _height);
            _layers.Add(flattened);
            _activeLayerIndex = 0;

            return flattened;
        }

        /// <summary>
        /// Composite all visible layers into a single image
        /// </summary>
        public byte[] CompositeAllLayers()
        {
            if (_layers.Count == 0)
                throw new InvalidOperationException("No layers to composite");

            // Start with the bottom layer
            byte[] result = (byte[])_layers[0].Data.Clone();

            // Blend each layer on top
            for (int i = 1; i < _layers.Count; i++)
            {
                if (!_layers[i].Visible) continue;

                var bottomTemp = new ImageLayer("Temp Bottom", result, _width, _height);
                var topLayer = _layers[i];

                result = LayerBlending.Blend(bottomTemp, topLayer);
                bottomTemp.Dispose();
            }

            return result;
        }

        /// <summary>
        /// Get composite of layers up to specified index
        /// </summary>
        public byte[] CompositeLayersUpTo(int maxIndex)
        {
            if (_layers.Count == 0)
                throw new InvalidOperationException("No layers to composite");

            byte[] result = (byte[])_layers[0].Data.Clone();

            for (int i = 1; i <= Math.Min(maxIndex, _layers.Count - 1); i++)
            {
                if (!_layers[i].Visible) continue;

                var bottomTemp = new ImageLayer("Temp Bottom", result, _width, _height);
                var topLayer = _layers[i];

                result = LayerBlending.Blend(bottomTemp, topLayer);
                bottomTemp.Dispose();
            }

            return result;
        }

        #region Undo/Redo

        public void SaveUndoState()
        {
            var snapshot = _layers.Select(l => l.Clone()).ToList();
            _undoStack.Push(snapshot);

            if (_undoStack.Count > MaxUndoSteps)
            {
                var oldest = _undoStack.ToArray()[_undoStack.Count - 1];
                foreach (var layer in oldest)
                    layer.Dispose();

                var temp = _undoStack.ToArray();
                _undoStack.Clear();
                for (int i = 0; i < MaxUndoSteps; i++)
                    _undoStack.Push(temp[i]);
            }

            // Clear redo stack on new action
            while (_redoStack.Count > 0)
            {
                var redoState = _redoStack.Pop();
                foreach (var layer in redoState)
                    layer.Dispose();
            }
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;

            // Save current state to redo
            var currentState = _layers.Select(l => l.Clone()).ToList();
            _redoStack.Push(currentState);

            // Restore previous state
            foreach (var layer in _layers)
                layer.Dispose();

            _layers = _undoStack.Pop();
            _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;

            // Save current state to undo
            var currentState = _layers.Select(l => l.Clone()).ToList();
            _undoStack.Push(currentState);

            // Restore redo state
            foreach (var layer in _layers)
                layer.Dispose();

            _layers = _redoStack.Pop();
            _activeLayerIndex = Math.Clamp(_activeLayerIndex, 0, _layers.Count - 1);
        }

        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;

        #endregion

        public void Dispose()
        {
            foreach (var layer in _layers)
                layer?.Dispose();

            _layers.Clear();

            while (_undoStack.Count > 0)
            {
                var state = _undoStack.Pop();
                foreach (var layer in state)
                    layer?.Dispose();
            }

            while (_redoStack.Count > 0)
            {
                var state = _redoStack.Pop();
                foreach (var layer in state)
                    layer?.Dispose();
            }
        }
    }
}
