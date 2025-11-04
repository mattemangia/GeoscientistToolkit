// GeoscientistToolkit/Business/Panorama/Match.cs
namespace GeoscientistToolkit.Business.Panorama;

public class Match
{
    public int QueryIdx { get; set; }
    public int TrainIdx { get; set; }
    public float Distance { get; set; }
}