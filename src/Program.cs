using VNode.Interfaces;
using VNode.Storage;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var httpPort = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--http-port="))?.Split('=')[1], out var hp) ? hp : 5000;
var storageRoot = args.FirstOrDefault(a => a.StartsWith("--data="))?.Split('=')[1]
    ?? Path.Combine(AppContext.BaseDirectory, "vnode-data");

builder.WebHost.UseUrls($"http://localhost:{httpPort}");

// --- Services ---
builder.Services.AddSingleton<IStorageProvider>(new LocalStorageProvider(storageRoot));

var app = builder.Build();

// --- REST API ---
app.MapGet("/api/status", () => new
{
    Status = "Online",
    Server = "VNode",
    Port = httpPort,
    Storage = app.Services.GetRequiredService<IStorageProvider>().ProviderName,
    Code = 200
});

app.MapGet("/api/list/{**path}", (string path, IStorageProvider storage) =>
{
    var entries = storage.List(path)
        .Select(n => new
        {
            n.Name,
            n.Path,
            n.IsDirectory,
            n.LastModified
        });
    return Results.Ok(entries);
});

app.MapGet("/api/read/{**path}", async (string path, IStorageProvider storage) =>
{
    if (!storage.Exists(path))
        return Results.NotFound(new { Error = $"Not found: {path}" });

    var stream = storage.Open(path);
    return Results.File(stream, "application/octet-stream");
});

app.MapGet("/api/exists/{**path}", (string path, IStorageProvider storage) =>
{
    return Results.Ok(new
    {
        File = storage.Exists(path),
        Directory = storage.DirectoryExists(path)
    });
});

app.MapGet("/", () => Results.Ok(new
{
    Server = "VNode",
    Version = "2.0.0",
    Endpoints = new[]
    {
        "GET /api/status",
        "GET /api/list/{path}",
        "GET /api/read/{path}",
        "GET /api/exists/{path}"
    }
}));

Console.WriteLine($"[VNode] HTTP API on http://localhost:{httpPort}");
Console.WriteLine($"[VNode] Storage root: {storageRoot}");

app.Run();
