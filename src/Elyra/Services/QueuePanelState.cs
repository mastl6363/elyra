namespace Elyra.Services;

/// <summary>Shared UI state for the queue drawer.</summary>
public sealed class QueuePanelState
{
    public bool IsOpen { get; private set; }
    public event EventHandler? Changed;

    public void Open() => SetOpen(true);
    public void Close() => SetOpen(false);
    public void Toggle() => SetOpen(!IsOpen);

    private void SetOpen(bool value)
    {
        if (IsOpen == value) return;
        IsOpen = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
