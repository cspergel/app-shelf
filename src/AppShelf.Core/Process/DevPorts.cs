namespace AppShelf.Core.Process;

/// <summary>Common local dev-server ports scanned in addition to registered app ports.</summary>
public static class DevPorts
{
    public static readonly IReadOnlyList<int> Common =
        new[] { 3000, 4200, 5000, 5173, 5273, 7860, 8000, 8080, 8501 };
}
