//Copyright 2025 Matteo Mangiagalli - matteo.mangiagalli@unifr.ch
using System.Drawing;
using System.Numerics;

namespace GeoscientistToolkit
{
    // ------------------------------------------------------------------------
    // Material class
    // ------------------------------------------------------------------------
    public class Material
    {
        public byte ID { get; set; } // Changed from int to byte
        public string Name { get; set; }
        public Vector4 Color { get; set; }
        public byte MinValue { get; set; }
        public byte MaxValue { get; set; }
        public bool IsExterior { get; set; } = false;
        public double Density { get; set; } = 0.0;
        public bool IsVisible { get; set; } = true;

        public Material(string name, Vector4 color, byte min, byte max, byte id, double density = 0.0)
        {
            Name = name;
            Color = color;
            MinValue = min;
            MaxValue = max;
            ID = id;
            Density = density;
            this.IsVisible = true;
        }
        public Material(byte id, string name, Vector4 color, byte min = 0, byte max = 0)
        {
            ID = id;
            Name = name;
            Color = color;
            MinValue = min;
            MaxValue = max;
        }

       
        public override string ToString() => $"{Name} ({ID})";
    }
}