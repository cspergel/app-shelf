namespace AppShelf.Core.Detection;

/// <summary>
/// The detection rule tables (spec §3.2). Kept deliberately small — only the stacks the
/// user actually runs. Adding a stack is a one-line change here.
/// </summary>
public static class FrameworkRules
{
    /// <summary>npm scripts to launch a dev server, in priority order.</summary>
    public static readonly string[] NodeScriptPriority = { "dev", "start", "serve", "preview" };

    /// <summary>
    /// Node frameworks keyed by a dependency name. First match (in this order) wins.
    /// </summary>
    public static readonly IReadOnlyList<NodeFrameworkRule> Node = new[]
    {
        new NodeFrameworkRule("vite", "Vite", 5173),
        new NodeFrameworkRule("next", "Next.js", 3000),
        new NodeFrameworkRule("react-scripts", "CRA", 3000),
        new NodeFrameworkRule("astro", "Astro", 4321),
        new NodeFrameworkRule("@sveltejs/kit", "SvelteKit", 5173),
        new NodeFrameworkRule("@angular/core", "Angular", 4200),
    };

    /// <summary>
    /// Python frameworks keyed by a substring signal found in a .py file. First match
    /// (in this order) wins. <see cref="PythonFrameworkRule.CmdTemplate"/> may contain a
    /// "{file}" placeholder, replaced with the entry file that carried the signal.
    /// </summary>
    public static readonly IReadOnlyList<PythonFrameworkRule> Python = new[]
    {
        new PythonFrameworkRule("streamlit", "Streamlit", 8501, "streamlit run {file}"),
        new PythonFrameworkRule("FastAPI(", "FastAPI", 8000, "uvicorn main:app --reload"),
        new PythonFrameworkRule("Flask(__name__)", "Flask", 5000, "python {file}"),
        new PythonFrameworkRule("gradio", "Gradio", 7860, "python {file}"),
    };
}

public sealed record NodeFrameworkRule(string Dependency, string Framework, int DefaultPort);

public sealed record PythonFrameworkRule(string Signal, string Framework, int DefaultPort, string CmdTemplate);
