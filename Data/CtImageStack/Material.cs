// Copyright 2025 Matteo Mangiagalli - matteo.mangiagalli@unifr.ch

using System.Numerics;

namespace GeoscientistToolkit;

public class Material
{
    public Material(string name, Vector4 color, byte min, byte max, byte id, double density = 0.0)
    {
        Name = name;
        Color = color;
        MinValue = min;
        MaxValue = max;
        ID = id;
        Density = density;
        IsVisible = true;
    }

    public Material(byte id, string name, Vector4 color, byte min = 0, byte max = 0, double density = 0.0,
        string physicalMaterialName = null)
    {
        ID = id;
        Name = name;
        Color = color;
        MinValue = min;
        MaxValue = max;
        Density = density;
        PhysicalMaterialName = physicalMaterialName;
        IsVisible = true;
    }

    public byte ID { get; set; }
    public string Name { get; set; }
    public Vector4 Color { get; set; }
    public byte MinValue { get; set; }
    public byte MaxValue { get; set; }
    public bool IsExterior { get; set; } = false;
    public double Density { get; set; }
    public bool IsVisible { get; set; } = true;

    // NEW: Link to physical material from library
    public string PhysicalMaterialName { get; set; }


    public override string ToString()
    {
        return $"{Name} ({ID})";
    }
}