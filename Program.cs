app.Use(async (context, next) =>
{
    if (context.Request.Path.Value?.Equals("/service-worker.js", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
    }
    await next();
});
app.UseStaticFiles();