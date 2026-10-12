var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => Results.Content(
    "<!doctype html><html><body><h1>.NET CTF starter</h1><form action=\"/search\"><input name=\"q\" placeholder=\"Search\"><button>Search</button></form></body></html>",
    "text/html"));

app.MapGet("/search", (string? q) => Results.Content(
    $"<!doctype html><html><body><h1>Search results</h1><p>You searched for: {q}</p><a href=\"/\">Back</a></body></html>",
    "text/html"));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
