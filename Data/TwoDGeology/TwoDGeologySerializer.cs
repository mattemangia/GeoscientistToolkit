// GeoscientistToolkit/Business/GIS/TwoDGeologySerializer.cs

using System.Numerics;
using static GeoscientistToolkit.Business.GIS.GeologicalMapping.ProfileGenerator;

namespace GeoscientistToolkit.Business.GIS;

/// <summary>
///     Handles binary serialization for 2D geological profiles.
/// </summary>
public static class TwoDGeologySerializer
{
    private const int FileVersion = 1;

    #region Write Methods

    public static void Write(string path, GeologicalMapping.CrossSectionGenerator.CrossSection data)
    {
        using var stream = new FileStream(path, FileMode.Create);
        using var writer = new BinaryWriter(stream);

        writer.Write(FileVersion);

        // Write Topographic Profile
        WriteTopographicProfile(writer, data.Profile);

        // Write Formations
        writer.Write(data.Formations.Count);
        foreach (var formation in data.Formations) WriteProjectedFormation(writer, formation);

        // Write Faults
        writer.Write(data.Faults.Count);
        foreach (var fault in data.Faults) WriteProjectedFault(writer, fault);
    }

    private static void WriteTopographicProfile(BinaryWriter writer, TopographicProfile profile)
    {
        WriteVector2(writer, profile.StartPoint);
        WriteVector2(writer, profile.EndPoint);
        writer.Write(profile.TotalDistance);
        writer.Write(profile.Points.Count);
        foreach (var point in profile.Points)
        {
            WriteVector2(writer, point.Position);
            writer.Write(point.Distance);
            writer.Write(point.Elevation);
        }
    }

    private static void WriteProjectedFormation(BinaryWriter writer,
        GeologicalMapping.CrossSectionGenerator.ProjectedFormation formation)
    {
        writer.Write(formation.Name ?? "");
        WriteVector4(writer, formation.Color);
        WriteVector2List(writer, formation.TopBoundary);
        WriteVector2List(writer, formation.BottomBoundary);
        writer.Write(formation.FoldStyle.HasValue);
        if (formation.FoldStyle.HasValue)
            writer.Write((int)formation.FoldStyle.Value);
    }

    private static void WriteProjectedFault(BinaryWriter writer,
        GeologicalMapping.CrossSectionGenerator.ProjectedFault fault)
    {
        writer.Write((int)fault.Type);
        WriteVector2List(writer, fault.FaultTrace);
        writer.Write(fault.Dip);
        writer.Write(fault.DipDirection ?? "");
        writer.Write(fault.Displacement.HasValue);
        if (fault.Displacement.HasValue)
            writer.Write(fault.Displacement.Value);
    }

    private static void WriteVector2(BinaryWriter writer, Vector2 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
    }

    private static void WriteVector4(BinaryWriter writer, Vector4 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
        writer.Write(v.W);
    }

    private static void WriteVector2List(BinaryWriter writer, List<Vector2> list)
    {
        writer.Write(list.Count);
        foreach (var v in list) WriteVector2(writer, v);
    }

    #endregion

    #region Read Methods

    public static GeologicalMapping.CrossSectionGenerator.CrossSection Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadInt32();
        if (version != FileVersion)
            throw new IOException($"Unsupported file version: {version}");

        var section = new GeologicalMapping.CrossSectionGenerator.CrossSection
        {
            Profile = ReadTopographicProfile(reader)
        };

        var formationCount = reader.ReadInt32();
        for (var i = 0; i < formationCount; i++) section.Formations.Add(ReadProjectedFormation(reader));

        var faultCount = reader.ReadInt32();
        for (var i = 0; i < faultCount; i++) section.Faults.Add(ReadProjectedFault(reader));

        return section;
    }

    private static TopographicProfile ReadTopographicProfile(BinaryReader reader)
    {
        var profile = new TopographicProfile
        {
            StartPoint = ReadVector2(reader),
            EndPoint = ReadVector2(reader)
        };
        profile.TotalDistance = reader.ReadSingle();
        var pointCount = reader.ReadInt32();
        for (var i = 0; i < pointCount; i++)
            profile.Points.Add(new ProfilePoint
            {
                Position = ReadVector2(reader),
                Distance = reader.ReadSingle(),
                Elevation = reader.ReadSingle()
            });
        return profile;
    }

    private static GeologicalMapping.CrossSectionGenerator.ProjectedFormation ReadProjectedFormation(
        BinaryReader reader)
    {
        var formation = new GeologicalMapping.CrossSectionGenerator.ProjectedFormation
        {
            Name = reader.ReadString(),
            Color = ReadVector4(reader),
            TopBoundary = ReadVector2List(reader),
            BottomBoundary = ReadVector2List(reader)
        };
    
       
        if (reader.ReadBoolean())
            formation.FoldStyle = (GeologicalMapping.FoldStyle)reader.ReadInt32();
    
        return formation;
    }

    private static GeologicalMapping.CrossSectionGenerator.ProjectedFault ReadProjectedFault(BinaryReader reader)
    {
        var fault = new GeologicalMapping.CrossSectionGenerator.ProjectedFault
        {
            Type = (GeologicalMapping.GeologicalFeatureType)reader.ReadInt32(),
            FaultTrace = ReadVector2List(reader),
            Dip = reader.ReadSingle(),
            DipDirection = reader.ReadString()
        };
    
       
        if (reader.ReadBoolean())
            fault.Displacement = reader.ReadSingle();
    
        return fault;
    }

    private static Vector2 ReadVector2(BinaryReader reader)
    {
        return new Vector2(reader.ReadSingle(), reader.ReadSingle());
    }

    private static Vector4 ReadVector4(BinaryReader reader)
    {
        return new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
    }

    private static List<Vector2> ReadVector2List(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        var list = new List<Vector2>(count);
        for (var i = 0; i < count; i++) list.Add(ReadVector2(reader));
        return list;
    }

    #endregion
}