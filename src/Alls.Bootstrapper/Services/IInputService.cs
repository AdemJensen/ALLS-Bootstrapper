namespace Alls.Bootstrapper.Services;

internal enum CabinetInputAction
{
    Select,
    Up,
    Down,
    Confirm,
    SwitchList,
    Button2,
    Button7
}

internal sealed class CabinetInputEventArgs(CabinetInputAction action, int player) : EventArgs
{
    public CabinetInputAction Action { get; } = action;

    public int Player { get; } = player;
}

internal interface IInputService : IDisposable
{
    event EventHandler<CabinetInputEventArgs>? Pressed;

    void Start();

    void Suspend();

    void Resume();
}
