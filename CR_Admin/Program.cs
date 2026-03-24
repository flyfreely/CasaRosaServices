using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

var connStr  = builder.Configuration["Database:ConnectionString"]!;
var apiToken = builder.Configuration["Http:AuthToken"]!;

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath         = "/";
        o.Cookie.Name       = "cr_admin";
        o.ExpireTimeSpan    = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Initialise DB (retry up to 5x to handle slow SQL Server startup)
for (int i = 0; i < 5; i++)
{
    try   { await EnsureDbAsync(); break; }
    catch (Exception ex) when (i < 4)
    {
        Console.WriteLine($"[DB init] {ex.Message} – retrying in 5 s…");
        await Task.Delay(5_000);
    }
}

ReservationDb.Init(connStr);

app.UseAuthentication();
app.UseAuthorization();

// ── Routes ────────────────────────────────────────────────────────────────────

// GET / → login page (redirect to dashboard if already authenticated)
app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/dashboard");
    return Results.Content(LoginPage(), "text/html");
});

// POST /login
app.MapPost("/login", async (HttpContext ctx) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    var storedHash = await GetUserHashAsync(username);
    if (storedHash == null || !VerifyPassword(password, storedHash))
        return Results.Content(LoginPage("Invalid username or password."), "text/html");

    var claims   = new[] { new Claim(ClaimTypes.Name, username) };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity));
    return Results.Redirect("/dashboard");
});

// GET /dashboard
app.MapGet("/dashboard", async () =>
{
    var briefingTime    = await GetConfigAsync("briefing_time")    ?? "18:00";
    var briefingChannel = await GetConfigAsync("briefing_channel") ?? "-5129864639";
    return Results.Content(DashboardPage(briefingTime, briefingChannel), "text/html");
}).RequireAuthorization();

// POST /dashboard – save settings
app.MapPost("/dashboard", async (HttpContext ctx) =>
{
    var form    = await ctx.Request.ReadFormAsync();
    var t       = form["briefing_time"].ToString();
    var channel = form["briefing_channel"].ToString();
    if (!TimeOnly.TryParse(t, out _)) t = "18:00";
    if (!long.TryParse(channel, out _)) channel = "-5129864639";
    await SetConfigAsync("briefing_time",    t);
    await SetConfigAsync("briefing_channel", channel);
    var briefingChannel = await GetConfigAsync("briefing_channel") ?? channel;
    return Results.Content(DashboardPage(t, briefingChannel, "Settings saved."), "text/html");
}).RequireAuthorization();

// GET /api/config/{key} – for internal services
app.MapGet("/api/config/{key}", async (string key, HttpContext ctx) =>
{
    var token = ctx.Request.Query["Token"].FirstOrDefault()
             ?? ctx.Request.Query["token"].FirstOrDefault()
             ?? "";
    if (token != apiToken) return Results.NotFound();
    var value = await GetConfigAsync(key);
    return value is null ? Results.NotFound() : Results.Ok(new { key, value });
});

// POST /logout
app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// ── Reservation routes ────────────────────────────────────────────────────────

// GET /reservations
app.MapGet("/reservations", async (HttpContext ctx) =>
{
    var q      = ctx.Request.Query;
    var apt    = int.TryParse(q["apt"],    out var a)  ? a  : 0;
    var status = q["status"].FirstOrDefault() ?? "active";
    var from   = DateOnly.TryParse(q["from"], out var f) ? f
                 : DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
    var to     = DateOnly.TryParse(q["to"],   out var t) ? t
                 : DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    var rows   = await ReservationDb.ListAsync(apt, from, to, status);
    return Results.Content(ReservationPages.List(rows, apt, from, to, status), "text/html");
}).RequireAuthorization();

// GET /reservations/new
app.MapGet("/reservations/new", () =>
    Results.Content(ReservationPages.NewForm(), "text/html")
).RequireAuthorization();

// POST /reservations/new
app.MapPost("/reservations/new", async (HttpContext ctx) =>
{
    var f  = await ctx.Request.ReadFormAsync();
    var id = await ReservationDb.CreateAsync(f);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// GET /reservations/{id}
app.MapGet("/reservations/{id:int}", async (int id) =>
{
    var res = await ReservationDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg    = await ReservationDb.GetRegistrationAsync(res.RegistrationGuid);
    var guests = reg is not null ? await ReservationDb.GetGuestsAsync(reg.Id) : new();
    return Results.Content(ReservationPages.Detail(res, reg, guests, ""), "text/html");
}).RequireAuthorization();

// POST /reservations/{id}  – save reservation fields
app.MapPost("/reservations/{id:int}", async (int id, HttpContext ctx) =>
{
    var f = await ctx.Request.ReadFormAsync();
    await ReservationDb.UpdateAsync(id, f);
    var res    = await ReservationDb.GetAsync(id);
    var reg    = res is not null ? await ReservationDb.GetRegistrationAsync(res.RegistrationGuid) : null;
    var guests = reg is not null ? await ReservationDb.GetGuestsAsync(reg.Id) : new();
    return Results.Content(ReservationPages.Detail(res!, reg, guests, "Saved."), "text/html");
}).RequireAuthorization();

// POST /reservations/{id}/registration  – create or update registration
app.MapPost("/reservations/{id:int}/registration", async (int id, HttpContext ctx) =>
{
    var f   = await ctx.Request.ReadFormAsync();
    var res = await ReservationDb.GetAsync(id);
    if (res is null) return Results.NotFound();

    var reg = await ReservationDb.GetRegistrationAsync(res.RegistrationGuid);
    if (reg is null)
        await ReservationDb.CreateRegistrationAsync(res);
    else
        await ReservationDb.UpdateRegistrationAsync(f);

    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// POST /reservations/{id}/guests  – add guest
app.MapPost("/reservations/{id:int}/guests", async (int id, HttpContext ctx) =>
{
    var f   = await ctx.Request.ReadFormAsync();
    var res = await ReservationDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg = await ReservationDb.GetRegistrationAsync(res.RegistrationGuid);
    if (reg is null) return Results.Redirect($"/reservations/{id}");
    await ReservationDb.AddGuestAsync(reg.Id, f);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// POST /reservations/{id}/guests/{guestId}/delete  – remove guest
app.MapPost("/reservations/{id:int}/guests/{guestId:int}/delete", async (int id, int guestId) =>
{
    await ReservationDb.DeleteGuestAsync(guestId);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

app.Run();

// ── DB helpers ────────────────────────────────────────────────────────────────

async Task EnsureDbAsync()
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    await RunAsync(conn, """
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Users')
        CREATE TABLE dbo.Admin_Users (
            Id           INT IDENTITY PRIMARY KEY,
            Username     NVARCHAR(100) NOT NULL UNIQUE,
            PasswordHash NVARCHAR(512) NOT NULL,
            CreatedAt    DATETIME2     NOT NULL DEFAULT GETUTCDATE()
        )
        """);

    await RunAsync(conn, """
        IF NOT EXISTS (
            SELECT 1 FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Config')
        CREATE TABLE dbo.Admin_Config (
            [Key]     NVARCHAR(100) NOT NULL PRIMARY KEY,
            [Value]   NVARCHAR(500) NOT NULL,
            UpdatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
        )
        """);

    // Seed default admin user
    var userCount = (int)(await ScalarAsync(conn, "SELECT COUNT(*) FROM dbo.Admin_Users"))!;
    if (userCount == 0)
    {
        var hash = HashPassword("password");
        await RunAsync(conn,
            "INSERT INTO dbo.Admin_Users (Username, PasswordHash) VALUES (@u, @h)",
            ("@u", "user"), ("@h", hash));
        Console.WriteLine("[DB init] Default user created: user / password");
    }

    // Seed default briefing time
    var cfgCount = (int)(await ScalarAsync(conn,
        "SELECT COUNT(*) FROM dbo.Admin_Config WHERE [Key] = 'briefing_time'"))!;
    if (cfgCount == 0)
        await RunAsync(conn,
            "INSERT INTO dbo.Admin_Config ([Key], [Value]) VALUES ('briefing_time', '18:00')");

    // Seed default briefing channel (Casa Rosa English)
    var chCount = (int)(await ScalarAsync(conn,
        "SELECT COUNT(*) FROM dbo.Admin_Config WHERE [Key] = 'briefing_channel'"))!;
    if (chCount == 0)
        await RunAsync(conn,
            "INSERT INTO dbo.Admin_Config ([Key], [Value]) VALUES ('briefing_channel', '-5129864639')");

    Console.WriteLine("[DB init] Tables ready.");
}

async Task RunAsync(SqlConnection conn, string sql,
    params (string Name, object Value)[] prms)
{
    using var cmd = new SqlCommand(sql, conn);
    foreach (var (name, value) in prms)
        cmd.Parameters.AddWithValue(name, value);
    await cmd.ExecuteNonQueryAsync();
}

async Task<object?> ScalarAsync(SqlConnection conn, string sql)
{
    using var cmd = new SqlCommand(sql, conn);
    return await cmd.ExecuteScalarAsync();
}

async Task<string?> GetUserHashAsync(string username)
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new SqlCommand(
        "SELECT PasswordHash FROM dbo.Admin_Users WHERE Username = @u", conn);
    cmd.Parameters.AddWithValue("@u", username);
    return (string?)await cmd.ExecuteScalarAsync();
}

async Task<string?> GetConfigAsync(string key)
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new SqlCommand(
        "SELECT [Value] FROM dbo.Admin_Config WHERE [Key] = @k", conn);
    cmd.Parameters.AddWithValue("@k", key);
    return (string?)await cmd.ExecuteScalarAsync();
}

async Task SetConfigAsync(string key, string value)
{
    using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();
    using var cmd = new SqlCommand("""
        IF EXISTS (SELECT 1 FROM dbo.Admin_Config WHERE [Key] = @k)
            UPDATE dbo.Admin_Config SET [Value] = @v, UpdatedAt = GETUTCDATE() WHERE [Key] = @k
        ELSE
            INSERT INTO dbo.Admin_Config ([Key], [Value]) VALUES (@k, @v)
        """, conn);
    cmd.Parameters.AddWithValue("@k", key);
    cmd.Parameters.AddWithValue("@v", value);
    await cmd.ExecuteNonQueryAsync();
}

// ── Password helpers ──────────────────────────────────────────────────────────

string HashPassword(string password)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
}

bool VerifyPassword(string password, string stored)
{
    var parts = stored.Split(':');
    if (parts.Length != 2) return false;
    var salt     = Convert.FromBase64String(parts[0]);
    var expected = Convert.FromBase64String(parts[1]);
    var actual   = Rfc2898DeriveBytes.Pbkdf2(
        password, salt, 100_000, HashAlgorithmName.SHA256, 32);
    return CryptographicOperations.FixedTimeEquals(actual, expected);
}

// ── HTML pages ────────────────────────────────────────────────────────────────

string LoginPage(string error = "") => $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>Casa Rosa Admin</title>
      <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               background: #f4f4f4; display: flex; align-items: center;
               justify-content: center; min-height: 100vh; }
        .card { background: #fff; border-radius: 10px;
                box-shadow: 0 4px 16px rgba(0,0,0,.1);
                padding: 2.5rem 2rem; width: 100%; max-width: 360px; }
        .logo { font-size: 1.4rem; font-weight: 700; color: #c0392b; margin-bottom: 1.5rem; }
        label { display: block; font-size: .8rem; font-weight: 600;
                color: #555; margin-bottom: .3rem; }
        input { display: block; width: 100%; padding: .6rem .8rem;
                border: 1px solid #ddd; border-radius: 6px;
                font-size: 1rem; margin-bottom: 1rem; outline: none; }
        input:focus { border-color: #c0392b;
                      box-shadow: 0 0 0 3px rgba(192,57,43,.15); }
        .btn { display: block; width: 100%; padding: .7rem;
               background: #c0392b; color: #fff; border: none;
               border-radius: 6px; font-size: 1rem; font-weight: 600; cursor: pointer; }
        .btn:hover { background: #a93226; }
        .err { background: #fdecea; color: #c0392b; border-radius: 6px;
               padding: .6rem .8rem; font-size: .85rem; margin-bottom: 1rem; }
      </style>
    </head>
    <body>
      <div class="card">
        <div class="logo">Casa Rosa Admin</div>
        {{(string.IsNullOrEmpty(error) ? "" : $"<div class=\"err\">{error}</div>")}}
        <form method="post" action="/login">
          <label for="u">Username</label>
          <input id="u" name="username" type="text" autocomplete="username" autofocus/>
          <label for="p">Password</label>
          <input id="p" name="password" type="password" autocomplete="current-password"/>
          <button class="btn" type="submit">Sign in</button>
        </form>
      </div>
    </body>
    </html>
    """;

string DashboardPage(string briefingTime, string briefingChannel, string message = "") => $$"""
    <!DOCTYPE html>
    <html lang="en">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>Casa Rosa Admin</title>
      <style>
        *, *::before, *::after { box-sizing: border-box; }
        body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               background: #f4f4f4; }
        header { background: #c0392b; color: #fff; padding: 1rem 2rem;
                 display: flex; align-items: center; justify-content: space-between; }
        header .title { font-size: 1.1rem; font-weight: 700; }
        header nav a { color: rgba(255,255,255,.85); text-decoration: none;
                        font-size: .9rem; margin-right: 1.2rem; }
        header nav a:hover { color: #fff; }
        .out-btn { background: rgba(255,255,255,.2); border: none; color: #fff;
                   padding: .4rem .9rem; border-radius: 5px;
                   cursor: pointer; font-size: .9rem; }
        .out-btn:hover { background: rgba(255,255,255,.35); }
        main { max-width: 640px; margin: 2rem auto; padding: 0 1rem; }
        .card { background: #fff; border-radius: 10px;
                box-shadow: 0 2px 10px rgba(0,0,0,.08); padding: 1.5rem 2rem;
                margin-bottom: 1.5rem; }
        .card h2 { margin: 0 0 1.2rem; font-size: 1rem; font-weight: 700;
                   color: #333; border-bottom: 1px solid #f0f0f0; padding-bottom: .6rem; }
        .field { margin-bottom: 1rem; }
        label { display: block; font-size: .8rem; font-weight: 600;
                color: #555; margin-bottom: .3rem; }
        input[type=time], select { padding: .6rem .8rem; border: 1px solid #ddd;
                                   border-radius: 6px; font-size: 1rem; outline: none;
                                   background: #fff; }
        input[type=time]:focus, select:focus { border-color: #c0392b;
                                               box-shadow: 0 0 0 3px rgba(192,57,43,.15); }
        .hint { font-size: .78rem; color: #999; margin-top: .3rem; }
        .btn { padding: .6rem 1.4rem; background: #c0392b; color: #fff;
               border: none; border-radius: 6px;
               font-size: .95rem; font-weight: 600; cursor: pointer; }
        .btn:hover { background: #a93226; }
        .ok { margin-top: .8rem; color: #27ae60; font-size: .85rem; font-weight: 600; }
      </style>
    </head>
    <body>
      <header>
        <div class="title">Casa Rosa Admin</div>
        <nav>
          <a href="/dashboard">Settings</a>
          <a href="/reservations">Reservations</a>
        </nav>
        <form method="post" action="/logout" style="margin:0">
          <button class="out-btn" type="submit">Sign out</button>
        </form>
      </header>
      <main>
        <div class="card">
          <h2>Auto-Bot – Daily Briefing</h2>
          <form method="post" action="/dashboard">
            <div class="field">
              <label for="bt">Send time (Portugal time)</label>
              <input id="bt" type="time" name="briefing_time" value="{{briefingTime}}"/>
            </div>
            <div class="field">
              <label for="bc">Destination channel</label>
              <select id="bc" name="briefing_channel">
                {{Option("-5129864639", "Casa Rosa English",    briefingChannel)}}
                {{Option("-5186091931", "Casa Rosa Management", briefingChannel)}}
                {{Option("-5209557963", "Translator",           briefingChannel)}}
                {{Option("-5271439382", "Rapid Response",       briefingChannel)}}
              </select>
              <div class="hint">Auto_Bot will send the daily checkout / check-in summary to this channel.</div>
            </div>
            <button class="btn" type="submit">Save</button>
            {{(string.IsNullOrEmpty(message) ? "" : $"<div class=\"ok\">&#10003; {message}</div>")}}
          </form>
        </div>
      </main>
    </body>
    </html>
    """;

string Option(string value, string label, string selected) =>
    $"<option value=\"{value}\"{(value == selected ? " selected" : "")}>{label}</option>";
