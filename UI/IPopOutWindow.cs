namespace GAIA.UI;

internal interface IPopOutWindow : IDisposable
{
    bool Exists { get; }
    void SetDrawCallback(Action callback);
    void ProcessFrame();
}
