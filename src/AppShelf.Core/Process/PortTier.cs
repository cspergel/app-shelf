namespace AppShelf.Core.Process;

/// <summary>How confident we are about a live port's ownership (Port Doctor).</summary>
public enum PortTier
{
    Managed,         // AppShelf launched it (holds the job)
    Registered,      // maps to a registered app, parent process alive — "yours"
    LikelyOrphaned,  // maps to a registered app but the launching parent is dead
    Unknown,         // no registered app on this port — never auto-kill
}
