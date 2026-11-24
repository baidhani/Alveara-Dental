namespace Alveara.Api.Architecture.Startup;

/// <summary>
/// ALV-N030 (brief 16.2, 16.2a): in every mode but <see cref="StartupMode.Normal"/>, an <c>/api</c> route answers 503 with only <c>{ error }</c>, except
/// <c>GET /api/setup/status</c> (all modes) and, in <see cref="StartupMode.Setup"/>, <c>POST /api/setup/deployment-administrator</c> and
/// <c>GET /api/auth/csrf-token</c>. Everything outside <c>/api</c> (the client files and the single-page-app fallback) passes in every mode, so the browser
/// can always load the app and show the right screen.
/// </summary>
public sealed class StartupModeMiddleware(RequestDelegate next, StartupModeState state)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (state.Mode == StartupMode.Normal || !context.Request.Path.StartsWithSegments("/api") || Allowed(context.Request))
        {
            await next(context);
            return;
        }
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new { error = state.Code });
    }

    private bool Allowed(HttpRequest request)
    {
        if (HttpMethods.IsGet(request.Method) && request.Path.Equals("/api/setup/status", StringComparison.OrdinalIgnoreCase)) return true;
        if (state.Mode != StartupMode.Setup) return false;
        return (HttpMethods.IsPost(request.Method) && request.Path.Equals("/api/setup/deployment-administrator", StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsGet(request.Method) && request.Path.Equals("/api/auth/csrf-token", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>ALV-N030 (brief 16.2b): asks the process to stop so its supervisor starts it again, now in Normal mode.</summary>
public interface ISetupRestart
{
    void RequestRestart();
}

/// <summary>
/// The real request: <c>Environment.ExitCode = 75</c>, then a graceful stop. <c>Program</c> ends with <c>app.Run()</c> and returns nothing, so the process
/// exits with that code. Called only from the response's <c>OnCompleted</c>, after the 201 has been sent.
/// </summary>
public sealed class ProcessSetupRestart(IHostApplicationLifetime lifetime, ILogger<ProcessSetupRestart> logger) : ISetupRestart
{
    public void RequestRestart()
    {
        logger.LogInformation("startup_restart_requested {ExitCode}", StartupModeState.RestartRequestedExitCode);
        Environment.ExitCode = StartupModeState.RestartRequestedExitCode;
        lifetime.StopApplication();
    }
}
