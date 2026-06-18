namespace AppShelf.App.Services;

/// <summary>Result of an elevated <c>Stop-Service</c> attempt (see
/// <see cref="GuiAppService.StopServiceElevatedAsync"/>).
/// <para><see cref="Started"/> is false when the service name was rejected or the elevated process
/// could not be started (non-cancel). <see cref="Cancelled"/> is true when the user dismissed the
/// UAC prompt (Win32 error 1223) — treat as a user choice, not an error. <see cref="PortFreed"/> is
/// true when the port was observed free within the poll window. <see cref="Reason"/> carries a
/// human-readable detail for an Info dialog; null on full success (and on the quiet cancel
/// path).</para></summary>
public sealed record ServiceStopOutcome(
    bool Started,
    bool Cancelled,
    bool PortFreed,
    string? Reason);
