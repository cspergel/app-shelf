using AppShelf.Core.Detection;

namespace AppShelf.Core.Tests;

public sealed class StackDetectorTests : IDisposable
{
    private readonly string _dir;
    private readonly StackDetector _detector = new();

    public StackDetectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "appshelf-detect", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(_dir, name), content);

    private static string Pkg(string deps, string scripts) =>
        $"{{ \"dependencies\": {{ {deps} }}, \"scripts\": {{ {scripts} }} }}";

    // --- Node ---

    [Fact]
    public void Detect_Vite_UsesDevScriptAndPort5173()
    {
        Write("package.json", Pkg("\"vite\": \"^5.0.0\"", "\"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.NotNull(r);
        Assert.Equal("npm run dev", r!.Cmd);
        Assert.Equal("http://localhost:5173", r.Url);
        Assert.Equal("npm install", r.InstallCmd);
        Assert.Equal("Vite", r.Framework);
    }

    [Fact]
    public void Detect_Next_Port3000()
    {
        Write("package.json", Pkg("\"next\": \"14.0.0\"", "\"dev\": \"next dev\""));

        var r = _detector.Detect(_dir);

        Assert.Equal("http://localhost:3000", r!.Url);
        Assert.Equal("Next.js", r.Framework);
    }

    [Theory]
    [InlineData("\"react-scripts\": \"5.0.0\"", "CRA", 3000)]
    [InlineData("\"astro\": \"4.0.0\"", "Astro", 4321)]
    [InlineData("\"@sveltejs/kit\": \"2.0.0\"", "SvelteKit", 5173)]
    [InlineData("\"@angular/core\": \"17.0.0\"", "Angular", 4200)]
    public void Detect_NodeFrameworks_MapToDefaultPorts(string dep, string framework, int port)
    {
        Write("package.json", Pkg(dep, "\"dev\": \"x\""));

        var r = _detector.Detect(_dir);

        Assert.Equal(framework, r!.Framework);
        Assert.Equal($"http://localhost:{port}", r.Url);
    }

    [Fact]
    public void Detect_ScriptPriority_PrefersDevOverStart()
    {
        Write("package.json", Pkg("\"vite\": \"5\"", "\"start\": \"x\", \"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.Equal("npm run dev", r!.Cmd);
    }

    [Fact]
    public void Detect_ScriptPriority_FallsThroughToServeThenPreview()
    {
        Write("package.json", Pkg("\"vite\": \"5\"", "\"serve\": \"x\", \"preview\": \"y\""));

        var r = _detector.Detect(_dir);

        Assert.Equal("npm run serve", r!.Cmd);
    }

    [Fact]
    public void Detect_ReadsDevDependencies_Too()
    {
        Write("package.json", "{ \"devDependencies\": { \"vite\": \"5\" }, \"scripts\": { \"dev\": \"vite\" } }");

        var r = _detector.Detect(_dir);

        Assert.Equal("Vite", r!.Framework);
    }

    [Fact]
    public void Detect_ViteConfigPort_OverridesDefault()
    {
        Write("package.json", Pkg("\"vite\": \"5\"", "\"dev\": \"vite\""));
        Write("vite.config.ts", "export default { server: { port: 3001 } }");

        var r = _detector.Detect(_dir);

        Assert.Equal("http://localhost:3001", r!.Url);
    }

    [Fact]
    public void Detect_NodeWithoutKnownFramework_ReturnsNull()
    {
        Write("package.json", Pkg("\"express\": \"4\"", "\"dev\": \"node index.js\""));

        Assert.Null(_detector.Detect(_dir));
    }

    [Fact]
    public void Detect_NodeWithoutLaunchableScript_ReturnsNull()
    {
        Write("package.json", Pkg("\"vite\": \"5\"", "\"build\": \"vite build\""));

        Assert.Null(_detector.Detect(_dir));
    }

    // --- Python ---

    [Fact]
    public void Detect_Streamlit_RunsTheEntryFile()
    {
        Write("app.py", "import streamlit as st\nst.write('hi')\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("python -m streamlit run app.py", r!.Cmd);
        Assert.Equal("http://localhost:8501", r.Url);
        Assert.Equal("Streamlit", r.Framework);
    }

    [Fact]
    public void Detect_FastAPI_UvicornMainApp()
    {
        Write("main.py", "from fastapi import FastAPI\napp = FastAPI()\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("python -m uvicorn main:app --reload", r!.Cmd);
        Assert.Equal("http://localhost:8000", r.Url);
    }

    [Fact]
    public void Detect_FastAPI_ModuleNameFromActualFile()
    {
        // The uvicorn module path must come from the file that carried the FastAPI( signal,
        // not a hardcoded "main" — a project whose app lives in server.py should still work.
        Write("server.py", "from fastapi import FastAPI\napp = FastAPI()\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("python -m uvicorn server:app --reload", r!.Cmd);
    }

    [Fact]
    public void Detect_FastAPI_InPackage_UsesDottedModuleFromPackageParent()
    {
        // app/ is a Python package (has __init__.py); its main.py imports `from app...`, so it
        // must run as `app.main:app` from the package's PARENT, not `main:app` from inside app/.
        WriteIn("app", "__init__.py", "");
        WriteIn("app", "main.py", "from fastapi import FastAPI\napp = FastAPI()\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("FastAPI", r!.Framework);
        Assert.Equal("python -m uvicorn app.main:app --reload", r.Cmd);
        Assert.Equal(_dir, r.Dir);
    }

    [Fact]
    public void Detect_Flask_PythonEntryFile()
    {
        Write("app.py", "from flask import Flask\napp = Flask(__name__)\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("python app.py", r!.Cmd);
        Assert.Equal("http://localhost:5000", r.Url);
    }

    [Fact]
    public void Detect_Gradio_Port7860()
    {
        Write("app.py", "import gradio as gr\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("http://localhost:7860", r!.Url);
        Assert.Equal("Gradio", r.Framework);
    }

    [Fact]
    public void Detect_NodeTakesPriorityOverPython()
    {
        Write("package.json", Pkg("\"vite\": \"5\"", "\"dev\": \"vite\""));
        Write("app.py", "import streamlit as st\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("Vite", r!.Framework);
    }

    [Fact]
    public void Detect_EmptyDir_ReturnsNull()
    {
        Assert.Null(_detector.Detect(_dir));
    }

    [Fact]
    public void Detect_MissingDir_ReturnsNull()
    {
        Assert.Null(_detector.Detect(Path.Combine(_dir, "does-not-exist")));
    }

    // --- Subfolder detection (monorepo-shaped projects) ---

    private void WriteIn(string subdir, string name, string content)
    {
        var dir = Path.Combine(_dir, subdir);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    [Fact]
    public void Detect_FrontendSubfolder_DetectedWithSubfolderAsWorkingDir()
    {
        WriteIn("frontend", "package.json", Pkg("\"vite\": \"^8\"", "\"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.NotNull(r);
        Assert.Equal("Vite", r!.Framework);
        Assert.Equal("npm run dev", r.Cmd);
        Assert.Equal(Path.Combine(_dir, "frontend"), r.Dir);
    }

    [Fact]
    public void Detect_BackendSubfolder_DetectsPython()
    {
        WriteIn("backend", "main.py", "from fastapi import FastAPI\napp = FastAPI()\n");

        var r = _detector.Detect(_dir);

        Assert.Equal("FastAPI", r!.Framework);
        Assert.Equal("python -m uvicorn main:app --reload", r.Cmd);
        Assert.Equal(Path.Combine(_dir, "backend"), r.Dir);
    }

    [Fact]
    public void Detect_PrefersFrontendOverBackend()
    {
        WriteIn("backend", "main.py", "from fastapi import FastAPI\napp = FastAPI()\n");
        WriteIn("frontend", "package.json", Pkg("\"vite\": \"^8\"", "\"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.Equal("Vite", r!.Framework);
        Assert.Equal(Path.Combine(_dir, "frontend"), r.Dir);
    }

    [Fact]
    public void Detect_TopLevel_TakesPrecedenceOverSubfolders()
    {
        Write("package.json", Pkg("\"next\": \"14\"", "\"dev\": \"next dev\""));
        WriteIn("frontend", "package.json", Pkg("\"vite\": \"^8\"", "\"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.Equal("Next.js", r!.Framework);
        Assert.Equal(_dir, r.Dir);
    }

    [Fact]
    public void Detect_TopLevel_SetsDirToScannedFolder()
    {
        Write("package.json", Pkg("\"vite\": \"^5\"", "\"dev\": \"vite\""));

        var r = _detector.Detect(_dir);

        Assert.Equal(_dir, r!.Dir);
    }
}
