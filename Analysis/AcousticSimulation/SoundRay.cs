using System.Numerics;

namespace GeoscientistToolkit.Analysis.AcousticSimulation;

public struct SoundRay
{
    public Vector3 Start;
    public Vector3 End;
    public float MeasuredTime;
    public List<Vector3> Path; // Detailed ray path if tracing is used
}
