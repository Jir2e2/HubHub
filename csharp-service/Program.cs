using System.Data;
using System.Net.Http;
using System.Text.Json;
using MySql.Data.MySqlClient;

var builder = WebApplication.CreateBuilder(args);

var connStr = Environment.GetEnvironmentVariable("MYSQL__CONNECTIONSTRING") ?? "Server=localhost;Port=3306;Database=hubhub;Uid=hubhub;Pwd=hubhubpass;";

builder.Services.AddScoped<IDbConnection>(_ => new MySqlConnection(connStr));
builder.Services.AddHttpClient();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", p => p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/github-repos", async (HttpContext ctx, IDbConnection db, HttpClient http) =>
{
    var auth = ctx.Request.Headers.Authorization.ToString();
    if (!auth.StartsWith("Bearer "))
        return Results.Unauthorized();
    var token = auth["Bearer ".Length..];

    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("HubHub");
    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

    var ghResp = await http.GetAsync("https://api.github.com/user/repos?type=public&per_page=100");
    if (!ghResp.IsSuccessStatusCode)
        return Results.Problem($"GitHub API: {(int)ghResp.StatusCode}", statusCode: (int)ghResp.StatusCode);

    var ghJson = await ghResp.Content.ReadAsStringAsync();
    var ghRepos = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(ghJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

    if (db.State != ConnectionState.Open) db.Open();

    foreach (var r in ghRepos)
    {
        long gid = r.TryGetValue("id", out var idv) ? idv.GetInt64() : 0;
        string owner = r.TryGetValue("owner", out var ov) && ov.TryGetProperty("login", out var lv) ? lv.GetString() ?? "" : "";
        string name = r.TryGetValue("name", out var nv) ? nv.GetString() ?? "" : "";
        string full = r.TryGetValue("full_name", out var fv) ? fv.GetString() ?? "" : "";
        string url = r.TryGetValue("html_url", out var uv) ? uv.GetString() ?? "" : "";
        if (gid == 0 || string.IsNullOrEmpty(name)) continue;

        using var check = new MySqlCommand("SELECT id FROM repositories WHERE github_repo_id=@gid", (MySqlConnection)db);
        check.Parameters.AddWithValue("@gid", gid);
        var exists = await check.ExecuteScalarAsync();
        if (exists != null) continue;

        using var ins = new MySqlCommand("INSERT INTO repositories (github_repo_id, owner, name, full_name, html_url) VALUES (@gid,@owner,@name,@full,@url)", (MySqlConnection)db);
        ins.Parameters.AddWithValue("@gid", gid);
        ins.Parameters.AddWithValue("@owner", owner);
        ins.Parameters.AddWithValue("@name", name);
        ins.Parameters.AddWithValue("@full", full);
        ins.Parameters.AddWithValue("@url", url);
        await ins.ExecuteNonQueryAsync();
    }

    const string sql = "SELECT id, github_repo_id, owner, name, full_name, html_url, scanned_at, created_at FROM repositories ORDER BY created_at DESC";
    using var cmd = new MySqlCommand(sql, (MySqlConnection)db);
    using var reader = await cmd.ExecuteReaderAsync();
    var repos = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        repos.Add(row);
    }
    return Results.Ok(repos);
});

app.MapGet("/api/repositories", async (IDbConnection db) =>
{
    if (db.State != ConnectionState.Open) db.Open();
    const string sql = "SELECT id, github_repo_id, owner, name, full_name, html_url, scanned_at, created_at FROM repositories ORDER BY created_at DESC";
    using var cmd = new MySqlCommand(sql, (MySqlConnection)db);
    using var reader = await cmd.ExecuteReaderAsync();
    var repos = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        repos.Add(row);
    }
    return Results.Ok(repos);
});

app.MapGet("/api/links", async (IDbConnection db, string? search, string? domain, string? repo, string? date) =>
{
    if (db.State != ConnectionState.Open) db.Open();
    var conditions = new List<string>();
    using var cmd = new MySqlCommand { Connection = (MySqlConnection)db };

    if (!string.IsNullOrWhiteSpace(search))
    {
        conditions.Add("(url LIKE @search OR title LIKE @title)");
        cmd.Parameters.AddWithValue("@search", $"%{search}%");
        cmd.Parameters.AddWithValue("@title", $"%{search}%");
    }
    if (!string.IsNullOrWhiteSpace(domain))
    {
        conditions.Add("domain = @domain");
        cmd.Parameters.AddWithValue("@domain", domain);
    }
    if (!string.IsNullOrWhiteSpace(repo))
    {
        conditions.Add("repository_id = @repo");
        cmd.Parameters.AddWithValue("@repo", repo);
    }
    if (!string.IsNullOrWhiteSpace(date))
    {
        conditions.Add("DATE(created_at) = @date");
        cmd.Parameters.AddWithValue("@date", date);
    }

    var where = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
    var sql = $"SELECT l.id, l.url, l.normalized_url, l.domain, l.title, l.source_type, l.source_path, l.created_at, r.owner, r.name FROM links l JOIN repositories r ON l.repository_id = r.id {where} ORDER BY l.created_at DESC";
    cmd.CommandText = sql;

    using var reader = await cmd.ExecuteReaderAsync();
    var links = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        links.Add(row);
    }
    return Results.Ok(links);
});

app.MapGet("/api/links/domains", async (IDbConnection db) =>
{
    if (db.State != ConnectionState.Open) db.Open();
    const string sql = "SELECT domain, COUNT(*) as cnt FROM links GROUP BY domain ORDER BY cnt DESC";
    using var cmd = new MySqlCommand(sql, (MySqlConnection)db);
    using var reader = await cmd.ExecuteReaderAsync();
    var domains = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (int i = 0; i < reader.FieldCount; i++)
            row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        domains.Add(row);
    }
    return Results.Ok(domains);
});

app.Run();