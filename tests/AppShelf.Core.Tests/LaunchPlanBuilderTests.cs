using AppShelf.Core.Launch;
using AppShelf.Core.Models;

namespace AppShelf.Core.Tests;

public class LaunchPlanBuilderTests
{
    private static AppEntry Local(string cmd, int? port, string? framework) =>
        new() { Name = "x", Dir = "C:/x", Url = "http://localhost:1", Cmd = cmd, Port = port, Framework = framework };

    [Fact]
    public void NoPort_LeavesCommandUntouched_NoEnv()
    {
        var plan = LaunchPlanBuilder.Build(Local("npm run dev", port: null, framework: "Vite"));

        Assert.Equal("npm run dev", plan.Command);
        Assert.Empty(plan.Environment);
    }

    [Theory]
    [InlineData("Vite")]
    [InlineData("SvelteKit")]
    [InlineData("Astro")]
    [InlineData("Angular")]
    public void NodeFrameworks_AppendPortAndHostAfterDoubleDash(string framework)
    {
        var plan = LaunchPlanBuilder.Build(Local("npm run dev", 4000, framework));

        Assert.Equal("npm run dev -- --port 4000 --host 127.0.0.1", plan.Command);
        Assert.Empty(plan.Environment);
    }

    [Fact]
    public void Next_UsesHostnameFlag()
    {
        var plan = LaunchPlanBuilder.Build(Local("npm run dev", 4000, "Next.js"));

        Assert.Equal("npm run dev -- --port 4000 --hostname 127.0.0.1", plan.Command);
    }

    [Fact]
    public void Cra_UsesEnvVars_NotFlags()
    {
        var plan = LaunchPlanBuilder.Build(Local("npm start", 4000, "CRA"));

        Assert.Equal("npm start", plan.Command);
        Assert.Equal("4000", plan.Environment["PORT"]);
        Assert.Equal("127.0.0.1", plan.Environment["HOST"]);
    }

    [Fact]
    public void Streamlit_AppendsServerFlags()
    {
        var plan = LaunchPlanBuilder.Build(Local("streamlit run app.py", 4000, "Streamlit"));

        Assert.Equal("streamlit run app.py --server.port 4000 --server.address 127.0.0.1", plan.Command);
    }

    [Fact]
    public void FastApi_AppendsUvicornFlags()
    {
        var plan = LaunchPlanBuilder.Build(Local("uvicorn main:app --reload", 4000, "FastAPI"));

        Assert.Equal("uvicorn main:app --reload --port 4000 --host 127.0.0.1", plan.Command);
    }

    [Fact]
    public void Flask_UsesFlaskEnvVars()
    {
        var plan = LaunchPlanBuilder.Build(Local("python app.py", 4000, "Flask"));

        Assert.Equal("python app.py", plan.Command);
        Assert.Equal("4000", plan.Environment["FLASK_RUN_PORT"]);
        Assert.Equal("127.0.0.1", plan.Environment["FLASK_RUN_HOST"]);
    }

    [Fact]
    public void Gradio_UsesGradioEnvVars()
    {
        var plan = LaunchPlanBuilder.Build(Local("python app.py", 4000, "Gradio"));

        Assert.Equal("4000", plan.Environment["GRADIO_SERVER_PORT"]);
        Assert.Equal("127.0.0.1", plan.Environment["GRADIO_SERVER_NAME"]);
    }

    [Fact]
    public void UnknownFramework_FallsBackToGenericPortHostEnv()
    {
        var plan = LaunchPlanBuilder.Build(Local("./run.sh", 4000, framework: null));

        Assert.Equal("./run.sh", plan.Command);
        Assert.Equal("4000", plan.Environment["PORT"]);
        Assert.Equal("127.0.0.1", plan.Environment["HOST"]);
    }
}
