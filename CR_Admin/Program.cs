using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Serilog;
using Serilog.Formatting.Compact;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Web;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .Enrich.WithProperty("Source", "cr_admin")
    .WriteTo.Http(
        requestUri:    "http://192.168.48.1:5100/api/logs",
        queueLimitBytes: null,
        textFormatter: new CompactJsonFormatter(),
        httpClient:    new TelemetryHttpClient())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

var apiToken         = builder.Configuration["Http:AuthToken"]!;
var resApiBase       = builder.Configuration["ReservationApi:BaseUrl"] ?? "http://localhost:8103";
var resApiToken      = builder.Configuration["ReservationApi:Token"]  ?? "";
var googleClientId   = builder.Configuration["Google:ClientId"]     ?? "";
var googleClientSecret = builder.Configuration["Google:ClientSecret"] ?? "";
var googleEnabled    = !string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret);
var telegramApiBase  = builder.Configuration["TelegramApi:BaseUrl"] ?? "";
var telegramApiToken = builder.Configuration["TelegramApi:Token"]   ?? "";

var resHttp  = new HttpClient();
var jsonOpts = new JsonSerializerOptions(JsonSerializerDefaults.Web);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/app/keys"))
    .SetApplicationName("cr_admin");

var authBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath         = "/";
        o.Cookie.Name       = "cr_admin";
        o.ExpireTimeSpan    = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
    })
    .AddCookie("External", o =>
    {
        o.Cookie.Name    = "cr_ext";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
    });

if (googleEnabled)
    authBuilder.AddGoogle(o =>
    {
        o.SignInScheme  = "External";
        o.ClientId      = googleClientId;
        o.ClientSecret  = googleClientSecret;
        o.CallbackPath  = "/auth/google/callback";
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

// ── URL helpers ───────────────────────────────────────────────────────────────
string ResUrl(string path) => $"{resApiBase}{path}?token={resApiToken}";
string ResUrlQ(string path, string qs) => $"{resApiBase}{path}?token={resApiToken}&{qs}";

string GetRole(HttpContext ctx) =>
    ctx.User.FindFirst("Role")?.Value ?? "Viewer";

bool IsAdmin(HttpContext ctx) => GetRole(ctx) == "Admin";
bool CanManage(HttpContext ctx) { var r = GetRole(ctx); return r == "Admin" || r == "Viewer"; }
bool CanToggleCleaning(HttpContext ctx) { var r = GetRole(ctx); return r == "Admin" || r == "Viewer" || r == "Helper"; }
bool CanViewFinancial(HttpContext ctx) => GetRole(ctx) != "Helper";

string GetLang(HttpContext ctx) =>
    ctx.Request.Cookies.TryGetValue("cr_lang", out var l) && l == "ru" ? "ru" : "en";

int GetUserId(HttpContext ctx) =>
    int.TryParse(ctx.User.FindFirst("UserId")?.Value, out var id) ? id : 0;

void Audit(string actor, string action, string? detail = null) =>
    _ = resHttp.PostAsJsonAsync(ResUrl("/admin/audit"), new { actor, action, detail }, jsonOpts);

// ── Routes ────────────────────────────────────────────────────────────────────

app.MapGet("/", (HttpContext ctx) =>
{
    if (ctx.User.Identity?.IsAuthenticated == true)
        return Results.Redirect("/calendar");
    return Results.Content(LoginPage(lang: GetLang(ctx), showGoogle: googleEnabled), "text/html");
});

// POST /login
app.MapPost("/login", async (HttpContext ctx) =>
{
    var form     = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    var resp = await resHttp.PostAsJsonAsync(ResUrl("/admin/login"),
        new { username, password }, jsonOpts);
    var loginLang = GetLang(ctx);
    if (!resp.IsSuccessStatusCode)
    {
        Audit(username, "Login failed");
        return Results.Content(LoginPage(T.Get(loginLang, "Invalid username or password."), lang: loginLang, showGoogle: googleEnabled), "text/html");
    }

    var body = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    var role       = body.TryGetProperty("role", out var r)   ? r.GetString()  ?? "Viewer" : "Viewer";
    var userId     = body.TryGetProperty("id",   out var uid) ? uid.GetInt32()              : 0;
    var serverLang = body.TryGetProperty("lang", out var l)   ? l.GetString()  ?? "en"     : "en";
    var claims = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim("Role", role),
        new Claim("UserId", userId.ToString())
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    ctx.Response.Cookies.Append("cr_lang", serverLang == "ru" ? "ru" : "en",
        new CookieOptions { MaxAge = TimeSpan.FromDays(365), Path = "/" });
    Audit(username, "Login");
    return Results.Redirect("/calendar");
});

// GET /dashboard
app.MapGet("/dashboard", async (HttpContext ctx) =>
{
    var lang               = GetLang(ctx);
    var tomorrowTime       = await GetConfigAsync("briefing_time")              ?? "18:00";
    var tomorrowChannel    = await GetConfigAsync("briefing_channel")            ?? "-5129864639";
    var todayTime          = await GetConfigAsync("today_briefing_time")        ?? "09:00";
    var todayChannel       = await GetConfigAsync("today_briefing_channel")     ?? "-5129864639";
    var tripleTime         = await GetConfigAsync("triple_cleaning_time")       ?? "08:00";
    var tripleChannel      = await GetConfigAsync("triple_cleaning_channel")    ?? "-5186091931";
    return Results.Content(DashboardPage(tomorrowTime, tomorrowChannel, todayTime, todayChannel, tripleTime, tripleChannel, isAdmin: IsAdmin(ctx), lang: lang), "text/html");
}).RequireAuthorization();

// POST /dashboard
app.MapPost("/dashboard", async (HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect("/dashboard");
    var lang           = GetLang(ctx);
    var form           = await ctx.Request.ReadFormAsync();
    var tomorrowTime    = form["tomorrow_time"].ToString();
    var tomorrowChannel = form["tomorrow_channel"].ToString();
    var todayTime       = form["today_time"].ToString();
    var todayChannel    = form["today_channel"].ToString();
    var tripleTime      = form["triple_time"].ToString();
    var tripleChannel   = form["triple_channel"].ToString();
    if (!TimeOnly.TryParse(tomorrowTime,  out _)) tomorrowTime  = "18:00";
    if (!long.TryParse(tomorrowChannel,   out _)) tomorrowChannel = "-5129864639";
    if (!TimeOnly.TryParse(todayTime,     out _)) todayTime     = "09:00";
    if (!long.TryParse(todayChannel,      out _)) todayChannel  = "-5129864639";
    if (!TimeOnly.TryParse(tripleTime,    out _)) tripleTime    = "08:00";
    if (!long.TryParse(tripleChannel,     out _)) tripleChannel = "-5186091931";
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/briefing_time"),           new { value = tomorrowTime  }, jsonOpts);
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/briefing_channel"),        new { value = tomorrowChannel }, jsonOpts);
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/today_briefing_time"),     new { value = todayTime     }, jsonOpts);
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/today_briefing_channel"),  new { value = todayChannel  }, jsonOpts);
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/triple_cleaning_time"),    new { value = tripleTime    }, jsonOpts);
    await resHttp.PostAsJsonAsync(ResUrl("/admin/config/triple_cleaning_channel"), new { value = tripleChannel }, jsonOpts);
    Audit(ctx.User.Identity?.Name ?? "?", "Settings saved", $"tomorrow={tomorrowTime}/{tomorrowChannel} today={todayTime}/{todayChannel} triple={tripleTime}/{tripleChannel}");
    return Results.Content(DashboardPage(tomorrowTime, tomorrowChannel, todayTime, todayChannel, tripleTime, tripleChannel, T.Get(lang, "Saved."), isAdmin: true, lang: lang), "text/html");
}).RequireAuthorization();

// POST /dashboard/trigger/{type} – manually fire a briefing/alert via CR_Telegram
app.MapPost("/dashboard/trigger/{type}", async (string type, HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Forbid();
    if (string.IsNullOrEmpty(telegramApiBase)) return Results.Problem("TelegramApi not configured.");
    var path = type switch
    {
        "tomorrow" => "/briefing",
        "today"    => "/briefing/today",
        "triple"   => "/briefing/triple-cleaning",
        _          => null
    };
    if (path == null) return Results.BadRequest("Unknown type.");
    var resp = await resHttp.PostAsync($"{telegramApiBase}{path}?Token={telegramApiToken}", null);
    Audit(ctx.User.Identity?.Name ?? "?", "Manual trigger", type);
    return resp.IsSuccessStatusCode ? Results.Ok() : Results.Problem("CR_Telegram returned an error.");
}).RequireAuthorization();

// GET /api/config/{key} – passthrough for internal services (e.g. CR_Telegram)
app.MapGet("/api/config/{key}", async (string key, HttpContext ctx) =>
{
    var token = ctx.Request.Query["Token"].FirstOrDefault()
             ?? ctx.Request.Query["token"].FirstOrDefault()
             ?? "";
    if (token != apiToken) return Results.NotFound();
    var resp = await resHttp.GetAsync(ResUrl($"/admin/config/{key}"));
    if (!resp.IsSuccessStatusCode) return Results.NotFound();
    var data = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    return Results.Ok(data);
});

// POST /logout
app.MapPost("/logout", async (HttpContext ctx) =>
{
    Audit(ctx.User.Identity?.Name ?? "?", "Logout");
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
});

// GET /lang/{code} – set language cookie, persist to DB if authenticated, redirect back
app.MapGet("/lang/{code}", async (string code, HttpContext ctx) =>
{
    var lang = code == "ru" ? "ru" : "en";
    ctx.Response.Cookies.Append("cr_lang", lang,
        new CookieOptions { MaxAge = TimeSpan.FromDays(365), Path = "/" });
    if (ctx.User.Identity?.IsAuthenticated == true)
    {
        var uid = GetUserId(ctx);
        if (uid > 0)
            await resHttp.PostAsJsonAsync(ResUrl($"/admin/users/{uid}/language"), new { language = lang }, jsonOpts);
    }
    var referer = ctx.Request.Headers["Referer"].FirstOrDefault() ?? "/";
    return Results.Redirect(referer);
});

// GET /auth/google – initiate Google OAuth flow
app.MapGet("/auth/google", (HttpContext ctx) =>
    Results.Challenge(
        new AuthenticationProperties { RedirectUri = "/auth/google/done" },
        new[] { "Google" }));

// GET /auth/google/done – process Google callback, look up admin user, sign in
app.MapGet("/auth/google/done", async (HttpContext ctx) =>
{
    var result = await ctx.AuthenticateAsync("External");
    await ctx.SignOutAsync("External");

    var lang = GetLang(ctx);
    if (!result.Succeeded || result.Principal is null)
        return Results.Content(LoginPage(T.Get(lang, "Google sign-in failed. Please try again."), lang, showGoogle: googleEnabled), "text/html");

    var email = result.Principal.FindFirst(ClaimTypes.Email)?.Value;
    if (string.IsNullOrEmpty(email))
        return Results.Content(LoginPage(T.Get(lang, "Google account has no email address."), lang, showGoogle: googleEnabled), "text/html");

    var resp = await resHttp.GetAsync(ResUrlQ("/admin/users/by-google-email", $"email={HttpUtility.UrlEncode(email)}"));
    if (!resp.IsSuccessStatusCode)
    {
        var msg = lang == "ru"
            ? $"Google аккаунт {email} не привязан ни к одному пользователю."
            : $"Google account {email} is not linked to any admin user.";
        return Results.Content(LoginPage(msg, lang, showGoogle: googleEnabled), "text/html");
    }

    var body     = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    var userId   = body.GetProperty("id").GetInt32();
    var role     = body.GetProperty("role").GetString() ?? "Viewer";
    var userLang = body.GetProperty("lang").GetString() ?? "en";
    var username = body.GetProperty("username").GetString() ?? email;

    var claims   = new[]
    {
        new Claim(ClaimTypes.Name, username),
        new Claim("Role", role),
        new Claim("UserId", userId.ToString())
    };
    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    await ctx.SignInAsync(new ClaimsPrincipal(identity),
        new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7) });
    ctx.Response.Cookies.Append("cr_lang", userLang == "ru" ? "ru" : "en",
        new CookieOptions { MaxAge = TimeSpan.FromDays(365), Path = "/" });
    Audit(username, "Google login", email);
    return Results.Redirect("/calendar");
});

// GET /reminders
app.MapGet("/reminders", async (HttpContext ctx) =>
{
    var rows = await resHttp.GetFromJsonAsync<List<ReminderAdminRow>>(ResUrl("/admin/reminders"), jsonOpts) ?? new();
    return Results.Content(RemindersPage(rows, GetLang(ctx), CanManage(ctx)), "text/html");
}).RequireAuthorization();

// POST /reminders/{id}/cancel
app.MapPost("/reminders/{id:int}/cancel", async (int id) =>
{
    await resHttp.PostAsync(ResUrl($"/admin/reminders/{id}/cancel"), null);
    return Results.Redirect("/reminders");
}).RequireAuthorization();

// POST /reminders/{id}/edit
app.MapPost("/reminders/{id:int}/edit", async (int id, HttpRequest req) =>
{
    var form = await req.ReadFormAsync();
    var message     = form["message"].FirstOrDefault() ?? "";
    var scheduledAt = form["scheduledAt"].FirstOrDefault() ?? "";
    if (!DateTime.TryParse(scheduledAt, out var dt)) return Results.Redirect("/reminders");
    // form sends local (Portugal) time; store as-is with Unspecified kind
    var payload = JsonContent.Create(new { message, scheduledAt = dt });
    await resHttp.PostAsync(ResUrl($"/admin/reminders/{id}/update"), payload);
    return Results.Redirect("/reminders");
}).RequireAuthorization();

// POST /reminders/{id}/delete
app.MapPost("/reminders/{id:int}/delete", async (int id) =>
{
    await resHttp.PostAsync(ResUrl($"/admin/reminders/{id}/delete"), null);
    return Results.Redirect("/reminders");
}).RequireAuthorization();

// POST /reminders/create
app.MapPost("/reminders/create", async (HttpRequest req) =>
{
    var form        = await req.ReadFormAsync();
    var message     = form["message"].FirstOrDefault() ?? "";
    var scheduledAt = form["scheduledAt"].FirstOrDefault() ?? "";
    var channel     = form["channel"].FirstOrDefault() ?? "russian";
    var bot         = form["bot"].FirstOrDefault() ?? "Auto_Bot";
    var language    = form["language"].FirstOrDefault() ?? "Russian";
    if (!DateTime.TryParse(scheduledAt, out var dt)) return Results.Redirect("/reminders");
    long channelId  = channel == "english" ? -5129864639L : -5186091931L;
    var payload     = JsonContent.Create(new { message, scheduledAt = dt, channelId, botId = bot, language });
    await resHttp.PostAsync(ResUrl("/admin/reminders"), payload);
    return Results.Redirect("/reminders");
}).RequireAuthorization();

// ── Calendar ──────────────────────────────────────────────────────────────────

app.MapGet("/calendar", async (HttpContext ctx, string? date, string? month) =>
{
    var today        = DateOnly.FromDateTime(DateTime.Today);
    var sel          = DateOnly.TryParse(date, out var d) ? d : today;
    var firstOfMonth = DateOnly.TryParse(month + "-01", out var m)
        ? m : new DateOnly(sel.Year, sel.Month, 1);

    // 6-week calendar window, Monday-based
    var calStart  = firstOfMonth.AddDays(-(((int)firstOfMonth.DayOfWeek + 6) % 7));
    var calEnd    = calStart.AddDays(41);
    var fetchFrom = (sel < calStart ? sel : calStart).AddDays(-90);

    var allRes = await resHttp.GetFromJsonAsync<List<ReservationRow>>(
        ResUrlQ("/manage/reservations",
            $"apt=0&from={fetchFrom:yyyy-MM-dd}&to={calEnd:yyyy-MM-dd}&status=active"),
        jsonOpts) ?? new();

    // Day view per apartment
    var dayInfos = new List<CalAptInfo>();
    for (int a = 1; a <= 3; a++)
    {
        var rs   = allRes.Where(r => r.ApartmentNumber == a).ToList();
        var ci   = rs.FirstOrDefault(r => r.CheckInDate  == sel);
        var co   = rs.FirstOrDefault(r => r.CheckOutDate == sel);
        var stay = rs.FirstOrDefault(r => r.CheckInDate  <  sel && r.CheckOutDate > sel);
        var stat = ci != null && co != null ? "transition"
                 : ci   != null             ? "checkin"
                 : co   != null             ? "checkout"
                 : stay != null             ? "occupied"
                 :                           "vacant";

        string? arrT = null, arrMethod = null; bool earlyCI = false, crib = false, sofa = false; string? otherReq = null;
        if (ci != null)
        {
            var rr = await resHttp.GetAsync(ResUrl($"/manage/reservations/{ci.Id}/registration"));
            if (rr.IsSuccessStatusCode)
            {
                var reg = await rr.Content.ReadFromJsonAsync<RegistrationDetail>(jsonOpts);
                if (reg != null) { arrT = reg.ArrivalTime; earlyCI = reg.EarlyCheckIn; crib = reg.Crib; sofa = reg.Sofa; otherReq = reg.OtherRequests; arrMethod = reg.ArrivalMethod; }
            }
        }
        var primaryId = ci?.Id ?? stay?.Id;
        CalNote? noteIn = null, noteOut = null;
        if (primaryId.HasValue)
        {
            var rn = await resHttp.GetAsync(ResUrl($"/manage/reservations/{primaryId}/cal-note"));
            if (rn.IsSuccessStatusCode) noteIn = await rn.Content.ReadFromJsonAsync<CalNote>(jsonOpts);
        }
        if (co != null && co.Id != primaryId)
        {
            var rn = await resHttp.GetAsync(ResUrl($"/manage/reservations/{co.Id}/cal-note"));
            if (rn.IsSuccessStatusCode) noteOut = await rn.Content.ReadFromJsonAsync<CalNote>(jsonOpts);
        }
        var cr = await resHttp.GetAsync(ResUrl($"/manage/cleaning/{sel:yyyy-MM-dd}/{a}"));
        int cleaningState = 0;
        if (cr.IsSuccessStatusCode)
        {
            var cj = await cr.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
            cleaningState = cj.GetProperty("state").GetInt32();
        }
        dayInfos.Add(new(a, stat, ci, co, stay, arrT, earlyCI, crib, sofa, otherReq, noteIn, noteOut, cleaningState, arrMethod));
    }

    // Calendar state per day for each apartment: 0=vacant 1=checkin 2=checkout 3=transition 4=occupied
    var calData = new Dictionary<DateOnly, int[]>();
    for (var day = calStart; day <= calEnd; day = day.AddDays(1))
    {
        var s = new int[3];
        for (int i = 0; i < 3; i++)
        {
            int a  = i + 1;
            bool ci = allRes.Any(r => r.ApartmentNumber == a && r.CheckInDate  == day);
            bool co = allRes.Any(r => r.ApartmentNumber == a && r.CheckOutDate == day);
            bool md = allRes.Any(r => r.ApartmentNumber == a && r.CheckInDate  <  day && r.CheckOutDate > day);
            s[i] = ci && co ? 3 : ci ? 1 : co ? 2 : md ? 4 : 0;
        }
        calData[day] = s;
    }

    // Find next check-in with crib requested
    DateOnly? nextCrib = null;
    foreach (var r in allRes.Where(r => r.CheckInDate >= today).OrderBy(r => r.CheckInDate))
    {
        var rr = await resHttp.GetAsync(ResUrl($"/manage/reservations/{r.Id}/registration"));
        if (!rr.IsSuccessStatusCode) continue;
        var reg = await rr.Content.ReadFromJsonAsync<RegistrationDetail>(jsonOpts);
        if (reg?.Crib == true) { nextCrib = r.CheckInDate; break; }
    }

    // Fetch all cleaning states for the calendar window in one call
    var calCleaning = new Dictionary<(DateOnly, int), int>();
    var clResp = await resHttp.GetAsync(ResUrlQ("/manage/cleaning", $"from={calStart:yyyy-MM-dd}&to={calEnd:yyyy-MM-dd}"));
    if (clResp.IsSuccessStatusCode)
    {
        var clRows = await clResp.Content.ReadFromJsonAsync<List<CleaningRow>>(jsonOpts) ?? new();
        foreach (var cl in clRows) calCleaning[(cl.Date, cl.ApartmentNumber)] = cl.State;
    }

    return Results.Content(
        CalendarPage(sel, firstOfMonth, calStart, today, dayInfos, calData, IsAdmin(ctx), GetLang(ctx), nextCrib, calCleaning, CanToggleCleaning(ctx)),
        "text/html");
}).RequireAuthorization();

// GET /calendar/partial – AJAX day-view fragment (no full page reload for day navigation)
app.MapGet("/calendar/partial", async (HttpContext ctx, string? date) =>
{
    var today     = DateOnly.FromDateTime(DateTime.Today);
    var sel       = DateOnly.TryParse(date, out var d) ? d : today;
    var fetchFrom = sel.AddDays(-90);
    var allRes    = await resHttp.GetFromJsonAsync<List<ReservationRow>>(
        ResUrlQ("/manage/reservations",
            $"apt=0&from={fetchFrom:yyyy-MM-dd}&to={sel.AddDays(1):yyyy-MM-dd}&status=active"),
        jsonOpts) ?? new();

    var dayInfos = new List<CalAptInfo>();
    for (int a = 1; a <= 3; a++)
    {
        var rs   = allRes.Where(r => r.ApartmentNumber == a).ToList();
        var ci   = rs.FirstOrDefault(r => r.CheckInDate  == sel);
        var co   = rs.FirstOrDefault(r => r.CheckOutDate == sel);
        var stay = rs.FirstOrDefault(r => r.CheckInDate  <  sel && r.CheckOutDate > sel);
        var stat = ci != null && co != null ? "transition"
                 : ci   != null             ? "checkin"
                 : co   != null             ? "checkout"
                 : stay != null             ? "occupied"
                 :                           "vacant";

        string? arrT = null, arrMethod = null; bool earlyCI = false, crib = false, sofa = false; string? otherReq = null;
        if (ci != null)
        {
            var rr = await resHttp.GetAsync(ResUrl($"/manage/reservations/{ci.Id}/registration"));
            if (rr.IsSuccessStatusCode)
            {
                var reg = await rr.Content.ReadFromJsonAsync<RegistrationDetail>(jsonOpts);
                if (reg != null) { arrT = reg.ArrivalTime; earlyCI = reg.EarlyCheckIn; crib = reg.Crib; sofa = reg.Sofa; otherReq = reg.OtherRequests; arrMethod = reg.ArrivalMethod; }
            }
        }
        var primaryId = ci?.Id ?? stay?.Id;
        CalNote? noteIn = null, noteOut = null;
        if (primaryId.HasValue)
        {
            var rn = await resHttp.GetAsync(ResUrl($"/manage/reservations/{primaryId}/cal-note"));
            if (rn.IsSuccessStatusCode) noteIn = await rn.Content.ReadFromJsonAsync<CalNote>(jsonOpts);
        }
        if (co != null && co.Id != primaryId)
        {
            var rn = await resHttp.GetAsync(ResUrl($"/manage/reservations/{co.Id}/cal-note"));
            if (rn.IsSuccessStatusCode) noteOut = await rn.Content.ReadFromJsonAsync<CalNote>(jsonOpts);
        }
        var cr = await resHttp.GetAsync(ResUrl($"/manage/cleaning/{sel:yyyy-MM-dd}/{a}"));
        int cleaningState = 0;
        if (cr.IsSuccessStatusCode)
        {
            var cj = await cr.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
            cleaningState = cj.GetProperty("state").GetInt32();
        }
        dayInfos.Add(new(a, stat, ci, co, stay, arrT, earlyCI, crib, sofa, otherReq, noteIn, noteOut, cleaningState, arrMethod));
    }

    return Results.Content(CalendarDayHtml(sel, today, dayInfos, IsAdmin(ctx), GetLang(ctx), CanToggleCleaning(ctx)), "text/html");
}).RequireAuthorization();

// POST /calendar/cal-note/{id} – save admin calendar note (proxied to CR_ReservationApi)
app.MapPost("/calendar/cal-note/{id:int}", async (int id, CalNoteRequest req) =>
{
    await resHttp.PostAsJsonAsync(ResUrl($"/manage/reservations/{id}/cal-note"), req, jsonOpts);
    return Results.Ok();
}).RequireAuthorization();

// POST /calendar/cleaning/{apt}/toggle?date=yyyy-MM-dd
app.MapPost("/calendar/cleaning/{apt:int}/toggle", async (HttpContext ctx, int apt, string date) =>
{
    if (!CanToggleCleaning(ctx)) return Results.Forbid();
    var resp = await resHttp.PostAsync(ResUrl($"/manage/cleaning/{date}/{apt}/toggle"), null);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode(500);
    var json = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    return Results.Ok(new { state = json.GetProperty("state").GetInt32() });
}).RequireAuthorization();

// POST /calendar/cleaning/{apt}/set?date=yyyy-MM-dd&state=0|1|2
app.MapPost("/calendar/cleaning/{apt:int}/set", async (HttpContext ctx, int apt, string date, int state) =>
{
    if (!CanToggleCleaning(ctx)) return Results.Forbid();
    var resp = await resHttp.PostAsync(ResUrlQ($"/manage/cleaning/{date}/{apt}/set", $"state={state}"), null);
    if (!resp.IsSuccessStatusCode) return Results.StatusCode(500);
    var json = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    var newState = json.GetProperty("state").GetInt32();
    if (newState == 2 && !string.IsNullOrEmpty(telegramApiBase))
        _ = resHttp.PostAsync($"{telegramApiBase}/notify/apartment-ready?Token={telegramApiToken}&apt={apt}", null);
    return Results.Ok(new { state = newState });
}).RequireAuthorization();

// ── Statistics ────────────────────────────────────────────────────────────────

app.MapGet("/statistics", async (HttpContext ctx, int? year) =>
{
    var lang    = GetLang(ctx);
    var selYear = year ?? DateTime.Today.Year;
    var stats   = await resHttp.GetFromJsonAsync<List<MonthlyStatsRow>>(
        ResUrlQ("/manage/stats", $"year={selYear}"), jsonOpts) ?? new();
    return Results.Content(StatisticsPage(stats, selYear, lang, CanManage(ctx)), "text/html");
}).RequireAuthorization();

// ── Reservation routes (proxied to CR_ReservationApi) ─────────────────────────

// GET /reservations
app.MapGet("/reservations", async (HttpContext ctx) =>
{
    var q      = ctx.Request.Query;
    var apt    = q["apt"].FirstOrDefault()    ?? "0";
    var status = q["status"].FirstOrDefault() ?? "active";
    var from   = q["from"].FirstOrDefault()   ?? DateTime.Today.ToString("yyyy-MM-dd");
    var to     = q["to"].FirstOrDefault()     ?? DateTime.Today.AddDays(7).ToString("yyyy-MM-dd");
    var rows   = await resHttp.GetFromJsonAsync<List<ReservationRow>>(
                     ResUrlQ("/manage/reservations", $"apt={apt}&from={from}&to={to}&status={status}"),
                     jsonOpts) ?? new();
    var aptInt = int.TryParse(apt, out var a) ? a : 0;
    return Results.Content(
        ReservationPages.List(rows, aptInt, DateOnly.Parse(from), DateOnly.Parse(to), status, IsAdmin(ctx), GetLang(ctx)),
        "text/html");
}).RequireAuthorization();

// GET /reservations/new
app.MapGet("/reservations/new", (HttpContext ctx) =>
    IsAdmin(ctx)
        ? Results.Content(ReservationPages.NewForm(GetLang(ctx), isAdmin: true), "text/html")
        : Results.Redirect("/reservations")
).RequireAuthorization();

// POST /reservations/new
app.MapPost("/reservations/new", async (HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect("/reservations");
    var f        = await ctx.Request.ReadFormAsync();
    var checkin  = DateOnly.Parse(f["checkin"].ToString());
    var checkout = DateOnly.Parse(f["checkout"].ToString());
    var req = new
    {
        ApartmentNumber  = int.Parse(f["apartment"].ToString()),
        ReservationName  = f["name"].ToString(),
        ConfirmationCode = f["code"].ToString(),
        Status           = f["status"].ToString(),
        CheckInDate      = checkin,
        CheckOutDate     = checkout,
        Adults           = int.TryParse(f["adults"],   out var a) ? a : 0,
        Children         = int.TryParse(f["children"], out var c) ? c : 0,
        Infants          = int.TryParse(f["infants"],  out var i) ? i : 0,
    };
    var resp = await resHttp.PostAsJsonAsync(ResUrl("/manage/reservations"), req, jsonOpts);
    var body = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    var id   = body.GetProperty("id").GetInt32();
    Audit(ctx.User.Identity?.Name ?? "?", "Reservation created", $"Apt {req.ApartmentNumber}, {req.ReservationName}");
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// GET /reservations/{id}
app.MapGet("/reservations/{id:int}", async (int id, HttpContext ctx) =>
{
    var res = await resHttp.GetFromJsonAsync<ReservationDetail>(ResUrl($"/manage/reservations/{id}"), jsonOpts);
    if (res is null) return Results.NotFound();
    var regResp = await resHttp.GetAsync(ResUrl($"/manage/reservations/{id}/registration"));
    var reg     = regResp.IsSuccessStatusCode
                  ? await regResp.Content.ReadFromJsonAsync<RegistrationDetail>(jsonOpts)
                  : null;
    var guests  = reg is not null
                  ? await resHttp.GetFromJsonAsync<List<GuestRow>>(ResUrl($"/manage/reservations/{id}/guests"), jsonOpts) ?? new()
                  : new List<GuestRow>();
    return Results.Content(ReservationPages.Detail(res, reg, guests, "", IsAdmin(ctx), CanViewFinancial(ctx), GetLang(ctx)), "text/html");
}).RequireAuthorization();

// POST /reservations/{id}
app.MapPost("/reservations/{id:int}", async (int id, HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect($"/reservations/{id}");
    var f        = await ctx.Request.ReadFormAsync();
    var checkin  = DateOnly.Parse(f["checkin"].ToString());
    var checkout = DateOnly.Parse(f["checkout"].ToString());
    var req = new
    {
        ApartmentNumber  = int.Parse(f["apartment"].ToString()),
        ReservationName  = f["name"].ToString(),
        ConfirmationCode = f["code"].ToString(),
        Status           = f["status"].ToString(),
        CheckInDate      = checkin,
        CheckOutDate     = checkout,
        Adults           = int.TryParse(f["adults"],   out var a) ? a : 0,
        Children         = int.TryParse(f["children"], out var c) ? c : 0,
        Infants          = int.TryParse(f["infants"],  out var i) ? i : 0,
        PhoneNumber      = f["phone"].ToString(),
        LivesIn          = f["livesIn"].ToString(),
        Payout           = decimal.TryParse(f["payout"],   out var p)  ? p  : 0m,
        NightlyRate      = decimal.TryParse(f["rate"],     out var r)  ? r  : 0m,
        CleaningFee      = decimal.TryParse(f["cleaning"], out var cl) ? cl : 0m,
        Enabled          = f["enabled"].ToString()  == "on",
        Archived         = f["archived"].ToString() == "on",
        Private          = f["private"].ToString()  == "on",
    };
    await resHttp.PostAsJsonAsync(ResUrl($"/manage/reservations/{id}"), req, jsonOpts);
    Audit(ctx.User.Identity?.Name ?? "?", "Reservation updated", $"#{id}");
    var res    = await resHttp.GetFromJsonAsync<ReservationDetail>(ResUrl($"/manage/reservations/{id}"), jsonOpts);
    var regResp = await resHttp.GetAsync(ResUrl($"/manage/reservations/{id}/registration"));
    var reg    = regResp.IsSuccessStatusCode
                 ? await regResp.Content.ReadFromJsonAsync<RegistrationDetail>(jsonOpts)
                 : null;
    var guests = reg is not null
                 ? await resHttp.GetFromJsonAsync<List<GuestRow>>(ResUrl($"/manage/reservations/{id}/guests"), jsonOpts) ?? new()
                 : new List<GuestRow>();
    return Results.Content(ReservationPages.Detail(res!, reg, guests, T.Get(GetLang(ctx), "Saved."), isAdmin: true, showFinancial: true, lang: GetLang(ctx)), "text/html");
}).RequireAuthorization();

// POST /reservations/{id}/registration
app.MapPost("/reservations/{id:int}/registration", async (int id, HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect($"/reservations/{id}");
    var f   = await ctx.Request.ReadFormAsync();
    var req = new
    {
        Email         = f["email"].ToString(),
        ArrivalMethod = f["arrivalMethod"].ToString(),
        ArrivalTime   = f["arrivalTime"].ToString(),
        FlightNumber  = f["flight"].ToString(),
        ArrivalNotes  = f["arrivalNotes"].ToString(),
        EarlyCheckIn  = f["earlyCI"].ToString()   == "on",
        Crib          = f["crib"].ToString()      == "on",
        Sofa          = f["sofa"].ToString()      == "on",
        Foldable      = f["foldable"].ToString()  == "on",
        OtherRequests = f["otherRequests"].ToString(),
        InvoiceNif    = f["invoiceNif"].ToString(),
        InvoiceName   = f["invoiceName"].ToString(),
        InvoiceAddr   = f["invoiceAddr"].ToString(),
        InvoiceEmail  = f["invoiceEmail"].ToString(),
    };
    await resHttp.PostAsJsonAsync(ResUrl($"/manage/reservations/{id}/registration"), req, jsonOpts);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// POST /reservations/{id}/guests
app.MapPost("/reservations/{id:int}/guests", async (int id, HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect($"/reservations/{id}");
    var f   = await ctx.Request.ReadFormAsync();
    var req = new
    {
        Name        = f["guestName"].ToString(),
        Nationality = f["guestNat"].ToString(),
        BirthDate   = f["guestDob"].ToString(),
    };
    await resHttp.PostAsJsonAsync(ResUrl($"/manage/reservations/{id}/guests"), req, jsonOpts);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// POST /reservations/{id}/guests/{guestId}/delete
app.MapPost("/reservations/{id:int}/guests/{guestId:int}/delete", async (int id, int guestId, HttpContext ctx) =>
{
    if (!IsAdmin(ctx)) return Results.Redirect($"/reservations/{id}");
    await resHttp.PostAsync(ResUrl($"/manage/reservations/{id}/guests/{guestId}/delete"), null);
    return Results.Redirect($"/reservations/{id}");
}).RequireAuthorization();

// ── User management routes ────────────────────────────────────────────────────

// GET /users
app.MapGet("/users", async (HttpContext ctx) =>
{
    var users = await ListUsersAsync();
    return Results.Content(UsersPage(users, isAdmin: CanManage(ctx), lang: GetLang(ctx)), "text/html");
}).RequireAuthorization();

// POST /users – add new user
app.MapPost("/users", async (HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/users");
    var lang     = GetLang(ctx);
    var f        = await ctx.Request.ReadFormAsync();
    var username = f["username"].ToString().Trim();
    var password = f["password"].ToString();
    var confirm  = f["confirm"].ToString();

    var users = await ListUsersAsync();

    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return Results.Content(UsersPage(users, T.Get(lang, "Username and password are required."), lang: lang), "text/html");
    if (password != confirm)
        return Results.Content(UsersPage(users, T.Get(lang, "Passwords do not match."), lang: lang), "text/html");
    if (users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
        return Results.Content(UsersPage(users, lang == "ru" ? $"Пользователь '{username}' уже существует." : $"Username '{username}' already exists.", isAdmin: CanManage(ctx), lang: lang), "text/html");

    await resHttp.PostAsJsonAsync(ResUrl("/admin/users"), new { username, password }, jsonOpts);
    Audit(ctx.User.Identity?.Name ?? "?", "User created", username);
    users = await ListUsersAsync();
    return Results.Content(UsersPage(users, message: lang == "ru" ? $"Пользователь '{username}' создан." : $"User '{username}' created.", isAdmin: CanManage(ctx), lang: lang), "text/html");
}).RequireAuthorization();

// POST /users/{id}/set-role
app.MapPost("/users/{id:int}/set-role", async (int id, HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/users");
    var lang  = GetLang(ctx);
    var f     = await ctx.Request.ReadFormAsync();
    var role  = f["role"].ToString();
    var resp  = await resHttp.PostAsJsonAsync(ResUrl($"/admin/users/{id}/set-role"), new { role }, jsonOpts);
    var users = await ListUsersAsync();
    if (resp.IsSuccessStatusCode)
        Audit(ctx.User.Identity?.Name ?? "?", "Role changed", $"{users.FirstOrDefault(u => u.Id == id)?.Username ?? $"#{id}"} → {role}");
    var msg   = resp.IsSuccessStatusCode ? "" : T.Get(lang, "Cannot remove the last admin.");
    return Results.Content(UsersPage(users, msg, isAdmin: CanManage(ctx), lang: lang), "text/html");
}).RequireAuthorization();

// POST /users/{id}/password
app.MapPost("/users/{id:int}/password", async (int id, HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/users");
    var lang    = GetLang(ctx);
    var f       = await ctx.Request.ReadFormAsync();
    var newPwd  = f["password"].ToString();
    var confirm = f["confirm"].ToString();

    var users = await ListUsersAsync();

    if (string.IsNullOrEmpty(newPwd))
        return Results.Content(UsersPage(users, T.Get(lang, "Password cannot be empty."), isAdmin: CanManage(ctx), lang: lang), "text/html");
    if (newPwd != confirm)
        return Results.Content(UsersPage(users, T.Get(lang, "Passwords do not match."), isAdmin: CanManage(ctx), lang: lang), "text/html");

    await resHttp.PostAsJsonAsync(ResUrl($"/admin/users/{id}/password"), new { password = newPwd }, jsonOpts);
    Audit(ctx.User.Identity?.Name ?? "?", "Password changed", users.FirstOrDefault(u => u.Id == id)?.Username ?? $"#{id}");
    users = await ListUsersAsync();
    return Results.Content(UsersPage(users, message: T.Get(lang, "Password updated."), isAdmin: CanManage(ctx), lang: lang), "text/html");
}).RequireAuthorization();

// POST /users/{id}/google-email
app.MapPost("/users/{id:int}/google-email", async (int id, HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/users");
    var f = await ctx.Request.ReadFormAsync();
    var email = f["email"].ToString().Trim();
    await resHttp.PostAsJsonAsync(ResUrl($"/admin/users/{id}/google-email"), new { email }, jsonOpts);
    Audit(ctx.User.Identity?.Name ?? "?", "Google email set", $"#{id} → {email}");
    return Results.Redirect("/users");
}).RequireAuthorization();

// POST /users/{id}/delete
app.MapPost("/users/{id:int}/delete", async (int id, HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/users");
    var lang  = GetLang(ctx);
    var users = await ListUsersAsync();
    if (users.Count <= 1)
        return Results.Content(UsersPage(users, T.Get(lang, "Cannot delete the last user."), isAdmin: CanManage(ctx), lang: lang), "text/html");
    var deletedName = users.FirstOrDefault(u => u.Id == id)?.Username ?? $"#{id}";
    var resp  = await resHttp.PostAsync(ResUrl($"/admin/users/{id}/delete"), null);
    if (resp.IsSuccessStatusCode) Audit(ctx.User.Identity?.Name ?? "?", "User deleted", deletedName);
    users = await ListUsersAsync();
    var msg   = resp.IsSuccessStatusCode ? T.Get(lang, "User deleted.") : T.Get(lang, "Cannot delete the last admin user.");
    return Results.Content(UsersPage(users, message: msg, isAdmin: CanManage(ctx), lang: lang), "text/html");
}).RequireAuthorization();

// GET /audit-log
app.MapGet("/audit-log", async (HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/dashboard");
    var entries = await resHttp.GetFromJsonAsync<List<AuditLogEntry>>(ResUrl("/admin/audit"), jsonOpts) ?? new();
    return Results.Content(AuditLogPage(entries, GetLang(ctx)), "text/html");
}).RequireAuthorization();

// GET /log
app.MapGet("/log", async (HttpContext ctx) =>
{
    if (!CanManage(ctx)) return Results.Redirect("/dashboard");
    var entries = await resHttp.GetFromJsonAsync<List<TelegramLogRow>>(ResUrl("/admin/telegram-log"), jsonOpts) ?? new();
    return Results.Content(TelegramLogPage(entries, GetLang(ctx)), "text/html");
}).RequireAuthorization();

app.Run();

// ── Statistics page ───────────────────────────────────────────────────────────

string StatisticsPage(List<MonthlyStatsRow> stats, int year, string lang = "en", bool isAdmin = false)
{
    string[] aptColor = { "#4a90d9", "#2ecc71", "#e67e22" };
    string[] aptLabel = { T.Get(lang, "Apartment 1"), T.Get(lang, "Apartment 2"), T.Get(lang, "Apartment 3") };

    // Compute all 12 months for the year
    var months = Enumerable.Range(1, 12)
        .Select(m => $"{year:D4}-{m:D2}")
        .ToArray();

    // Group stats by apartment
    decimal totalRevenue = 0;
    int totalNights = 0;
    var nightsByApt = new int[3][];
    var payoutByApt = new decimal[3][];
    for (int i = 0; i < 3; i++)
    {
        nightsByApt[i] = new int[12];
        payoutByApt[i] = new decimal[12];
        for (int m = 0; m < 12; m++)
        {
            var monthStr = months[m];
            var row = stats.FirstOrDefault(s => s.Month == monthStr && s.ApartmentNumber == i + 1);
            nightsByApt[i][m] = row?.Nights ?? 0;
            payoutByApt[i][m] = row?.Payout ?? 0m;
            totalRevenue += row?.Payout ?? 0m;
            totalNights  += row?.Nights ?? 0;
        }
    }

    var daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
    var avgOccupancy = daysInYear > 0 ? (double)totalNights / (daysInYear * 3) * 100 : 0;

    string MonthLabel(string m) => DateTime.Parse(m + "-01").ToString("MMM", T.Culture(lang));
    var monthLabels = string.Join(",", months.Select(m => $"'{MonthLabel(m)}'"));

    string NightsData(int apt) => string.Join(",", nightsByApt[apt]);
    string PayoutData(int apt) => string.Join(",", payoutByApt[apt].Select(p => p.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));

    var prevYear = year - 1;
    var nextYear = year + 1;
    var todayYear = DateTime.Today.Year;

    return $$"""
        <!DOCTYPE html>
        <html lang="{{lang}}">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width,initial-scale=1"/>
          <title>{{T.Get(lang, "Statistics")}} – Casa Rosa Admin</title>
          <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
          <style>
            {{ReservationPages.Css}}
            .stat-cards { display:grid; grid-template-columns:repeat(3,1fr); gap:1rem; margin-bottom:1rem; }
            .stat-card { background:#fff; border-radius:10px; box-shadow:0 2px 8px rgba(0,0,0,.07);
                         padding:1.2rem 1.5rem; }
            .stat-card .label { font-size:.75rem; font-weight:700; text-transform:uppercase;
                                letter-spacing:.06em; color:#aaa; margin-bottom:.3rem; }
            .stat-card .value { font-size:1.8rem; font-weight:700; color:rgb(33,37,41); }
            .stat-card .sub { font-size:.8rem; color:#999; margin-top:.2rem; }
            .chart-card { background:#fff; border-radius:10px; box-shadow:0 2px 8px rgba(0,0,0,.07);
                          padding:1.5rem; margin-bottom:1rem; }
            .chart-title { font-size:.75rem; font-weight:700; text-transform:uppercase;
                           letter-spacing:.06em; color:#aaa; margin:0 0 1rem;
                           padding-bottom:.5rem; border-bottom:1px solid #f0f0f0; }
            .year-nav { display:flex; align-items:center; gap:1rem; margin-bottom:1.2rem; }
            .year-nav h1 { margin:0; font-size:1.3rem; }
            .year-nav a { font-size:1.3rem; text-decoration:none; color:inherit; padding:.2rem .6rem;
                          border-radius:5px; background:#f0f0f0; }
            .year-nav a:hover { background:#e0e0e0; }
            @media(max-width:640px) { .stat-cards { grid-template-columns:1fr; } }
          </style>
          <script src="https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js"></script>
        </head>
        <body>
          {{ReservationPages.Header("statistics", lang, isAdmin)}}
          <main>
            <div class="year-nav">
              <a href="/statistics?year={{prevYear}}">‹</a>
              <h1>{{T.Get(lang, "Statistics")}} {{year}}</h1>
              <a href="/statistics?year={{nextYear}}">›</a>
              {{(year != todayYear ? $"<a href='/statistics' style='font-size:.85rem;background:#f0f0f0;padding:.35rem .8rem;border-radius:5px'>{T.Get(lang, "This Year")}</a>" : "")}}
            </div>

            <div class="stat-cards">
              <div class="stat-card">
                <div class="label">{{T.Get(lang, "Total Nights")}}</div>
                <div class="value">{{totalNights}}</div>
                <div class="sub">{{T.Get(lang, "across all apartments")}}</div>
              </div>
              <div class="stat-card">
                <div class="label">{{T.Get(lang, "Total Revenue")}}</div>
                <div class="value">€{{totalRevenue:F0}}</div>
                <div class="sub">{{T.Get(lang, "total payout")}}</div>
              </div>
              <div class="stat-card">
                <div class="label">{{T.Get(lang, "Avg Occupancy")}}</div>
                <div class="value">{{avgOccupancy:F0}}%</div>
                <div class="sub">{{T.Get(lang, "across all apartments")}}</div>
              </div>
            </div>

            <div class="chart-card">
              <p class="chart-title">{{T.Get(lang, "Nights per Month")}}</p>
              <canvas id="nightsChart" height="80"></canvas>
            </div>

            <div class="chart-card">
              <p class="chart-title">{{T.Get(lang, "Revenue per Month")}} (€)</p>
              <canvas id="revenueChart" height="80"></canvas>
            </div>

          </main>
          <script>
            const months = [{{monthLabels}}];
            const aptColors = {{System.Text.Json.JsonSerializer.Serialize(aptColor)}};
            const aptLabels = {{System.Text.Json.JsonSerializer.Serialize(aptLabel)}};

            const nightsData = [
              { label: aptLabels[0], data: [{{NightsData(0)}}], backgroundColor: aptColors[0] + 'cc', borderColor: aptColors[0], borderWidth: 1 },
              { label: aptLabels[1], data: [{{NightsData(1)}}], backgroundColor: aptColors[1] + 'cc', borderColor: aptColors[1], borderWidth: 1 },
              { label: aptLabels[2], data: [{{NightsData(2)}}], backgroundColor: aptColors[2] + 'cc', borderColor: aptColors[2], borderWidth: 1 },
            ];

            const revenueData = [
              { label: aptLabels[0], data: [{{PayoutData(0)}}], backgroundColor: aptColors[0] + 'cc', borderColor: aptColors[0], borderWidth: 1 },
              { label: aptLabels[1], data: [{{PayoutData(1)}}], backgroundColor: aptColors[1] + 'cc', borderColor: aptColors[1], borderWidth: 1 },
              { label: aptLabels[2], data: [{{PayoutData(2)}}], backgroundColor: aptColors[2] + 'cc', borderColor: aptColors[2], borderWidth: 1 },
            ];

            const chartDefaults = {
              type: 'bar',
              options: {
                responsive: true,
                plugins: { legend: { position: 'top' } },
                scales: { x: { stacked: false }, y: { beginAtZero: true } }
              }
            };

            new Chart(document.getElementById('nightsChart'), {
              ...chartDefaults,
              data: { labels: months, datasets: nightsData }
            });

            new Chart(document.getElementById('revenueChart'), {
              ...chartDefaults,
              data: { labels: months, datasets: revenueData }
            });
          </script>
          {{ReservationPages.Footer()}}
        </body>
        </html>
        """;
}

// ── API helpers ───────────────────────────────────────────────────────────────

async Task<string?> GetConfigAsync(string key)
{
    var resp = await resHttp.GetAsync(ResUrl($"/admin/config/{key}"));
    if (!resp.IsSuccessStatusCode) return null;
    var doc = await resp.Content.ReadFromJsonAsync<JsonElement>(jsonOpts);
    return doc.GetProperty("value").GetString();
}

async Task<List<AdminUserRow>> ListUsersAsync()
{
    return await resHttp.GetFromJsonAsync<List<AdminUserRow>>(
        ResUrl("/admin/users"), jsonOpts) ?? new();
}

// ── HTML pages ────────────────────────────────────────────────────────────────

string CalendarPage(DateOnly sel, DateOnly firstOfMonth, DateOnly calStart, DateOnly today,
    List<CalAptInfo> apts, Dictionary<DateOnly, int[]> calData, bool isAdmin, string lang = "en", DateOnly? nextCrib = null,
    Dictionary<(DateOnly, int), int>? calCleaning = null, bool canToggleCleaning = false)
{
    string[] aptColor = { "#4a90d9", "#2ecc71", "#e67e22" };
    string[] aptDark  = { "#1a5276", "#1a7a43", "#a04000" };
    string[] aptFade  = { "#d0e8f8", "#c8f0d8", "#fde8cc" };
    string[] aptFloor = { T.Get(lang, "1st Floor"), T.Get(lang, "2nd Floor"), T.Get(lang, "3rd Floor") };
    string[] dayNames = lang == "ru"
        ? new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" }
        : new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    string StatusLabel(string s) => s switch
    {
        "checkin" => T.Get(lang, "Check-in ↓"), "checkout" => T.Get(lang, "Check-out ↑"),
        "occupied" => T.Get(lang, "Occupied"), "transition" => T.Get(lang, "Transition Day"), _ => T.Get(lang, "Vacant")
    };
    // Per-half colors: (leftColor, rightColor)
    (string L, string R) BarHalves(int state, int idx) => state switch
    {
        1 => ("#ddd",         aptColor[idx]),   // checkin
        2 => (aptDark[idx],   "#ddd"),           // checkout
        3 => (aptDark[idx],   aptColor[idx]),    // transition
        4 => (aptColor[idx],  aptColor[idx]),    // occupied
        _ => ("#ddd",         "#ddd")            // vacant
    };
    string BarStyle(int state, int idx) => state switch
    {
        1 => $"background:linear-gradient(to right,#ddd 50%,{aptColor[idx]} 50%)",         // checkin: right half colored
        2 => $"background:linear-gradient(to right,{aptDark[idx]} 50%,#ddd 50%)",          // checkout: left half dark
        3 => $"background:linear-gradient(to right,{aptDark[idx]} 50%,{aptColor[idx]} 50%)", // transition
        4 => $"background:{aptFade[idx]}",                                                  // occupied: faded color
        _ => "background:#e8e8e8"                                                           // vacant
    };

    var dayCardHtml = CalendarDayHtml(sel, today, apts, isAdmin, lang, canToggleCleaning);

    // Calendar grid
    var calHtml = new System.Text.StringBuilder();
    foreach (var dn in dayNames)
        calHtml.Append($"<div class='cal-head'>{dn}</div>");

    var prevMonth = firstOfMonth.AddMonths(-1).ToString("yyyy-MM");
    var nextMonth = firstOfMonth.AddMonths(1).ToString("yyyy-MM");
    var monthStr  = firstOfMonth.ToString("yyyy-MM");

    for (var day = calStart; day <= calStart.AddDays(41); day = day.AddDays(1))
    {
        var states   = calData.TryGetValue(day, out var s) ? s : new int[3];
        var isToday  = day == today;
        var isSel    = day == sel;
        var isOther  = day.Month != firstOfMonth.Month;
        var cls      = (isSel ? "selected " : "") + (isToday ? "is-today " : "") + (isOther ? "other-month" : "");
        var barHtml = string.Join("", states.Select((st, i) =>
        {
            var (lc, rc) = BarHalves(st, i);
            var apt      = i + 1;
            var cs       = calCleaning != null && calCleaning.TryGetValue((day, apt), out var csv) ? csv : 0;
            var broom    = cs > 0 ? $"<span class='cbar-broom cbar-broom-{cs}'></span>" : "";
            var title    = $"{T.Get(lang, "Apt")} {apt}: {StatusLabel(st == 0 ? "vacant" : st == 1 ? "checkin" : st == 2 ? "checkout" : st == 3 ? "transition" : "occupied")}";
            return $"<div class='cbar-row' title='{title}'><div class='cbar-half' style='background:{lc}'></div>{broom}<div class='cbar-half' style='background:{rc}'></div></div>";
        }));
        calHtml.Append($"""
            <a href="/calendar?date={day:yyyy-MM-dd}&month={monthStr}" class="cal-day {cls}">
              <span class="cal-num">{day.Day}</span>
              <div class="cbars">{barHtml}</div>
            </a>
            """);
    }

    // Monthly occupancy stats
    var daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
    var occNights = new int[3];
    for (var day = firstOfMonth; day < firstOfMonth.AddMonths(1); day = day.AddDays(1))
    {
        if (!calData.TryGetValue(day, out var s)) continue;
        for (int i = 0; i < 3; i++)
            if (s[i] == 1 || s[i] == 3 || s[i] == 4) // checkin, transition, occupied
                occNights[i]++;
    }
    var occBars = string.Join("", Enumerable.Range(0, 3).Select(i =>
    {
        var pct = daysInMonth > 0 ? occNights[i] * 100 / daysInMonth : 0;
        return $"""
            <div class="occ-row">
              <span class="occ-label"><span class="apt-badge" style="background:{aptColor[i]};color:#fff;width:20px;height:20px;font-size:.75rem">{i+1}</span> {aptFloor[i]}</span>
              <div class="occ-bar-track">
                <div class="occ-bar-fill" style="width:{pct}%;background:{aptColor[i]}"></div>
              </div>
              <span class="occ-pct">{occNights[i]}d · {pct}%</span>
            </div>
            """;
    }));

    return $$"""
        <!DOCTYPE html>
        <html lang="{{lang}}">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width,initial-scale=1"/>
          <title>{{T.Get(lang, "Calendar")}} – Casa Rosa Admin</title>
          <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
          <style>
            {{ReservationPages.Css}}
            .cal-page { display:flex; flex-direction:column; gap:1.2rem; }
            /* Day view */
            .day-nav { display:flex; align-items:center; gap:1rem; margin-bottom:1rem; }
            .day-nav h2 { flex:1; text-align:center; margin:0; font-size:1.15rem; }
            .day-nav a { font-size:1.4rem; text-decoration:none; color:inherit; padding:.2rem .6rem;
                         border-radius:5px; background:#f0f0f0; line-height:1; }
            .day-nav a:hover { background:#e0e0e0; }
            .apt-cols { display:grid; grid-template-columns:repeat(3,1fr); gap:1rem; }
            .cal-apt-col { border-radius:10px; padding:1rem; display:flex; flex-direction:column; gap:.4rem; min-height:160px; }
            .cal-apt-header { display:flex; align-items:center; gap:.5rem; margin-bottom:.3rem; }
            .apt-badge { color:#fff; border-radius:50%; width:26px; height:26px; display:inline-flex;
                         align-items:center; justify-content:center; font-weight:700; font-size:.85rem; flex-shrink:0; }
            .cal-status-badge { display:inline-block; padding:.25rem .65rem; border-radius:20px; color:#fff;
                                font-size:.78rem; font-weight:600; letter-spacing:.02em; width:fit-content; }
            .cal-apt-body { margin-top:.3rem; font-size:.88rem; display:flex; flex-direction:column; gap:.2rem; flex:1; }
            .cal-gline { display:flex; align-items:center; gap:.3rem; }
            .cal-gname-link { font-weight:600; font-size:.95rem; text-decoration:none; color:inherit; flex:1; min-width:0; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
            .cal-note-btn { background:none; border:none; padding:0; cursor:pointer; font-size:.85rem; opacity:.5; flex-shrink:0; line-height:1; }
            .cal-note-btn:hover { opacity:1; }
            .cal-mail-btn { font-size:.85rem; opacity:.55; flex-shrink:0; line-height:1; text-decoration:none; }
            .cal-mail-btn:hover { opacity:1; }
            .apt-date-bar { text-align:center; font-size:.85rem; color:#666; margin-bottom:.4rem; }
            .apt-date-rel { font-weight:700; color:#e67e22; }
            .clean-dlg { border:none; border-radius:12px; padding:1.5rem; box-shadow:0 8px 30px rgba(0,0,0,.18); max-width:300px; width:90%; }
            .clean-dlg h3 { margin:0 0 1rem; font-size:1rem; text-align:center; }
            .clean-dlg .clean-btns { display:flex; flex-direction:column; gap:.5rem; }
            .clean-dlg button { padding:.6rem 1rem; border:none; border-radius:7px; font-size:.95rem; font-weight:600; cursor:pointer; }
            .clean-btn-yes { background:#22c55e; color:#fff; }
            .clean-btn-no  { background:#ef4444; color:#fff; }
            .clean-btn-skip { background:#f0f0f0; color:#555; }
            .cal-gsub { color:#666; font-size:.8rem; }
            .cal-arrival { font-size:.85rem; margin-top:.25rem; }
            .cal-divider { font-size:.75rem; color:#999; font-weight:600; text-transform:uppercase; letter-spacing:.05em;
                           border-top:1px solid rgba(0,0,0,.08); padding-top:.3rem; margin-top:.1rem; }
            .cal-tags { display:flex; flex-wrap:wrap; gap:.3rem; margin-top:.3rem; }
            .cal-tag { background:rgba(0,0,0,.08); padding:.15rem .45rem; border-radius:10px; font-size:.75rem; }
            .cal-req { font-size:.8rem; color:#555; font-style:italic; margin-top:.2rem; }
            .cal-vacant-icon { font-size:2rem; text-align:center; padding:1rem 0; opacity:.3; }
            /* Calendar */
            .cal-section { }
            .cal-month-nav { display:flex; align-items:center; gap:1rem; margin-bottom:.8rem; }
            .cal-month-nav strong { flex:1; text-align:center; font-size:1rem; }
            .cal-month-nav a { font-size:1.3rem; text-decoration:none; color:inherit; padding:.2rem .6rem;
                               border-radius:5px; background:#f0f0f0; }
            .cal-month-nav a:hover { background:#e0e0e0; }
            .apt-img-wrap { position:relative; margin-bottom:.6rem; }
            .apt-img { width:100%; height:130px; object-fit:cover; border-radius:8px 8px 0 0; display:block; }
            .apt-broom { position:absolute; bottom:6px; right:8px; font-size:1.4rem; border-radius:6px;
                         padding:2px 5px; line-height:1; transition:background .2s; }
            .apt-broom[data-state="0"] { background:rgba(0,0,0,.18); opacity:.5; }
            .apt-broom[data-state="1"] { background:rgba(200,30,30,.75); opacity:1; }
            .apt-broom[data-state="2"] { background:rgba(22,160,60,.75); opacity:1; }
            .apt-broom.clickable { cursor:pointer; }
            .cal-legend { display:flex; gap:.8rem; font-size:.75rem; margin-bottom:.7rem; color:#666; flex-wrap:wrap; align-items:center; }
            .cal-legend span { display:flex; align-items:center; gap:.35rem; }
            .cleg { width:28px; height:6px; border-radius:3px; flex-shrink:0; }
            .cal-grid { display:grid; grid-template-columns:repeat(7,1fr); gap:2px; }
            .cal-head { text-align:center; font-size:.75rem; font-weight:600; color:#999;
                        padding:.4rem 0; text-transform:uppercase; letter-spacing:.05em; }
            .cal-day { display:flex; flex-direction:column; align-items:center; padding:.4rem .2rem .3rem;
                       border-radius:7px; text-decoration:none; color:inherit; cursor:pointer;
                       border:2px solid transparent; transition:background .1s; min-height:58px; gap:.2rem; }
            .cal-day:hover { background:#f0f0f0; }
            .cal-day.other-month .cal-num { color:#ccc; }
            .cal-day.is-today .cal-num { background:rgb(33,37,41); color:#fff; border-radius:50%;
                                          width:24px; height:24px; display:flex; align-items:center;
                                          justify-content:center; font-size:.82rem; }
            .cal-day.selected { border-color:rgb(33,37,41); background:#f5f5f5; }
            .cal-num { font-size:.85rem; font-weight:500; line-height:1.4; }
            .cbars { display:flex; flex-direction:column; gap:2px; width:100%; margin-top:auto; padding:0 1px; }
            .cbar-row { display:flex; align-items:center; width:100%; height:7px; border-radius:2px; overflow:visible; }
            .cbar-half { flex:1; height:7px; }
            .cbar-half:first-child { border-radius:2px 0 0 2px; }
            .cbar-half:last-child  { border-radius:0 2px 2px 0; }
            .cbar-broom { width:3px; height:11px; border-radius:2px; flex-shrink:0; }
            .cbar-broom-1 { background:#ef4444; }
            .cbar-broom-2 { background:#22c55e; }
            /* Occupancy */
            .occ-section { margin-top:.8rem; padding-top:.8rem; border-top:1px solid #f0f0f0; }
            .occ-title { font-size:.75rem; font-weight:700; text-transform:uppercase; letter-spacing:.06em;
                         color:#aaa; margin:0 0 .6rem; }
            .occ-row { display:flex; align-items:center; gap:.6rem; margin-bottom:.4rem; }
            .occ-label { display:flex; align-items:center; gap:.4rem; font-size:.82rem; min-width:100px; }
            .occ-bar-track { flex:1; height:10px; background:#f0f0f0; border-radius:5px; overflow:hidden; }
            .occ-bar-fill { height:100%; border-radius:5px; transition:width .3s; }
            .occ-pct { font-size:.78rem; color:#666; min-width:60px; text-align:right; }
            @media(max-width:700px) {
              .apt-cols { grid-template-columns:1fr; }
              .apt-img { height:120px; }
              .apt-broom { font-size:1.2rem; bottom:4px; right:6px; }
              .cbar-row { height:4px; }
              .cbar-half { height:4px; }
              .cal-grid { gap:1px; }
              .cal-day { min-height:50px; padding:.3rem .1rem; }
              .cbar { height:4px; }
            }
          </style>
        </head>
        <body>
          {{ReservationPages.Header("calendar", lang, isAdmin)}}
          <main>
            <div class="page-header">
              <h1>{{T.Get(lang, "Calendar")}}</h1>
              {{(nextCrib.HasValue ? $"<div style='font-size:.85rem;background:#fff8e1;border:1px solid #ffe082;border-radius:6px;padding:.4rem .85rem;margin-left:auto'>🛏 {T.Get(lang, "Crib needed:")} <strong>{nextCrib.Value.ToString("MMM d", T.Culture(lang))}</strong></div>" : "")}}
            </div>
            <div class="cal-page">

              <!-- Day view -->
              <div class="card" id="day-view-card">
                {{dayCardHtml}}
              </div>

              <!-- Calendar navigator -->
              <div class="card cal-section" data-cal-month="{{monthStr}}">
                <div class="cal-month-nav">
                  <a href="/calendar?date={{sel:yyyy-MM-dd}}&month={{prevMonth}}">‹</a>
                  <strong>{{firstOfMonth.ToString("MMMM yyyy", T.Culture(lang))}}</strong>
                  <a href="/calendar?date={{sel:yyyy-MM-dd}}&month={{nextMonth}}">›</a>
                </div>
                <div class="cal-legend">
                  <span><span class="cleg" style="background:linear-gradient(to right,#ddd 50%,#4a90d9 50%)"></span> {{T.Get(lang, "Check-in")}}</span>
                  <span><span class="cleg" style="background:linear-gradient(to right,#1a5276 50%,#ddd 50%)"></span> {{T.Get(lang, "Check-out")}}</span>
                  <span><span class="cleg" style="background:#d0e8f8"></span> {{T.Get(lang, "Occupied")}}</span>
                  <span><span class="cleg" style="background:linear-gradient(to right,#1a5276 50%,#4a90d9 50%)"></span> {{T.Get(lang, "Transition")}}</span>
                </div>
                <div class="cal-grid">
                  {{calHtml}}
                </div>
                <!-- Monthly occupancy -->
                <div class="occ-section">
                  <p class="occ-title">{{T.Get(lang, "Monthly Occupancy")}} – {{firstOfMonth.ToString("MMMM", T.Culture(lang))}}</p>
                  {{occBars}}
                </div>
              </div>

            </div>
          </main>
          <dialog id="clean-dialog" class="clean-dlg">
            <h3>{{T.Get(lang, "Apt")}} <span id="cld-apt"></span> — {{T.Get(lang, "is it clean?")}}</h3>
            <div class="clean-btns">
              <button class="clean-btn-yes"  onclick="cleanChoice('yes')">✓ {{T.Get(lang, "Yes, it's clean")}}</button>
              <button class="clean-btn-no"   onclick="cleanChoice('no')">✗ {{T.Get(lang, "No, still needs cleaning")}}</button>
              <button class="clean-btn-skip" onclick="cleanChoice('skip')">{{T.Get(lang, "Not needed")}}</button>
            </div>
          </dialog>
          <dialog id="cal-note-dialog" style="border:none;border-radius:12px;padding:1.5rem;min-width:300px;box-shadow:0 8px 32px rgba(0,0,0,.18)">
            <h3 id="cnd-title" style="margin:0 0 1rem;font-size:1rem"></h3>
            <input type="hidden" id="cnd-res-id"/>
            <div class="field" id="cnd-ci-row"><label style="font-size:.85rem">{{T.Get(lang, "Check-in time")}}</label>
              <input type="text" id="cnd-ci-time" placeholder="e.g. 3PM-4PM" list="arr-time-opts2"/>
              <datalist id="arr-time-opts2">
                <option value="Before 3PM"/><option value="3PM-4PM"/><option value="4PM-5PM"/>
                <option value="5PM-6PM"/><option value="6PM-7PM"/><option value="After 7PM"/>
              </datalist>
            </div>
            <div class="field" id="cnd-co-row"><label style="font-size:.85rem">{{T.Get(lang, "Check-out time")}}</label>
              <input type="text" id="cnd-co-time" placeholder="e.g. 11AM"/>
            </div>
            <div class="check-row" style="flex-direction:column;gap:.4rem;margin-top:.2rem">
              <label><input type="checkbox" id="cnd-earlyci"/> {{T.Get(lang, "Early check-in")}}</label>
              <label><input type="checkbox" id="cnd-crib"/> {{T.Get(lang, "Crib")}}</label>
              <label><input type="checkbox" id="cnd-sofa"/> {{T.Get(lang, "Sofa bed")}}</label>
              <label><input type="checkbox" id="cnd-bags"/> {{T.Get(lang, "Leaving bags")}}</label>
            </div>
            <div style="display:flex;gap:.5rem;margin-top:1.2rem">
              <button id="cnd-save" class="btn btn-primary" style="flex:1">{{T.Get(lang, "Save")}}</button>
              <button id="cnd-cancel" class="btn btn-secondary">{{T.Get(lang, "Cancel")}}</button>
            </div>
          </dialog>
          <script>
          (function(){
            var calSection = document.querySelector('.cal-section');
            var curMonth = calSection ? calSection.dataset.calMonth : '';
            async function loadDay(date) {
              var newMonth = date.substring(0, 7);
              if (newMonth !== curMonth) { location.href = '/calendar?date=' + date; return; }
              try {
                var resp = await fetch('/calendar/partial?date=' + encodeURIComponent(date));
                if (!resp.ok) throw 0;
                document.getElementById('day-view-card').innerHTML = await resp.text();
                document.querySelectorAll('.cal-day').forEach(function(a) {
                  var u = new URL(a.href, location.origin);
                  a.classList.toggle('selected', u.searchParams.get('date') === date);
                });
                history.pushState(null, '', '/calendar?date=' + date);
              } catch(e) { location.href = '/calendar?date=' + date; }
            }
            function curDate() {
              return new URLSearchParams(location.search).get('date')
                  || new Date().toISOString().split('T')[0];
            }
            document.querySelector('.cal-page').addEventListener('click', async function(e) {
              var noteBtn = e.target.closest('.cal-note-btn');
              if (noteBtn) {
                e.preventDefault();
                var d = noteBtn.dataset;
                document.getElementById('cnd-title').textContent = d.resName || '';
                document.getElementById('cnd-res-id').value      = d.resId;
                document.getElementById('cnd-ci-time').value     = d.checkinTime  || '';
                document.getElementById('cnd-co-time').value     = d.checkoutTime || '';
                document.getElementById('cnd-earlyci').checked   = d.earlyci  === 'true';
                document.getElementById('cnd-crib').checked      = d.crib     === 'true';
                document.getElementById('cnd-sofa').checked      = d.sofabed  === 'true';
                document.getElementById('cnd-bags').checked      = d.leavingbags === 'true';
                var mode = d.mode || 'checkin';
                document.getElementById('cnd-ci-row').style.display = mode === 'checkout' ? 'none' : '';
                document.getElementById('cnd-co-row').style.display = mode === 'checkin'  ? 'none' : '';
                document.getElementById('cal-note-dialog').showModal();
                return;
              }
              var dayLink = e.target.closest('.cal-day');
              if (dayLink && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                await loadDay(new URL(dayLink.href, location.origin).searchParams.get('date'));
                return;
              }
              var navArrow = e.target.closest('.day-nav-arrow');
              if (navArrow && !e.ctrlKey && !e.metaKey) {
                e.preventDefault();
                var d = new URL(navArrow.href, location.origin).searchParams.get('date');
                if (d) await loadDay(d);
                return;
              }
            });
            document.getElementById('cnd-cancel').addEventListener('click', function() {
              document.getElementById('cal-note-dialog').close();
            });
            document.getElementById('cnd-save').addEventListener('click', async function() {
              var resId = document.getElementById('cnd-res-id').value;
              var body = JSON.stringify({
                checkInTime:  document.getElementById('cnd-ci-time').value || null,
                checkOutTime: document.getElementById('cnd-co-time').value || null,
                crib:         document.getElementById('cnd-crib').checked,
                earlyCheckIn: document.getElementById('cnd-earlyci').checked,
                sofaBed:      document.getElementById('cnd-sofa').checked,
                leavingBags:  document.getElementById('cnd-bags').checked
              });
              var r = await fetch('/calendar/cal-note/' + resId, {
                method: 'POST', headers: {'Content-Type':'application/json'}, body: body
              });
              document.getElementById('cal-note-dialog').close();
              if (r.ok) await loadDay(curDate());
            });
            window.addEventListener('popstate', function() {
              var d = new URL(location.href).searchParams.get('date');
              if (d) loadDay(d); else location.reload();
            });
          })();
          var _cleanApt, _cleanDate, _cleanResolve;
          function cleanChoice(choice) {
            document.getElementById('clean-dialog').close();
            _cleanResolve(choice);
          }
          function askClean(apt, date) {
            document.getElementById('cld-apt').textContent = apt;
            document.getElementById('clean-dialog').showModal();
            return new Promise(function(resolve) { _cleanResolve = resolve; });
          }
          async function applyClean(apt, date, state) {
            var resp = await fetch('/calendar/cleaning/' + apt + '/set?date=' + date + '&state=' + state, {method:'POST'});
            if (!resp.ok) return;
            var data = await resp.json();
            var el = document.getElementById('broom-' + apt);
            if (el) el.dataset.state = data.state;
            var calDay = document.querySelector('.cal-day[href*="date=' + date + '"]');
            if (calDay) {
              var rows = calDay.querySelectorAll('.cbar-row');
              var row = rows[apt - 1];
              if (row) {
                var old = row.querySelector('.cbar-broom');
                if (old) old.remove();
                if (data.state > 0) {
                  var broom = document.createElement('span');
                  broom.className = 'cbar-broom cbar-broom-' + data.state;
                  var halves = row.querySelectorAll('.cbar-half');
                  if (halves.length >= 1) halves[0].after(broom);
                }
              }
            }
          }
          window.toggleCleaning = async function(apt) {
            var date = new URLSearchParams(location.search).get('date')
                    || new Date().toISOString().split('T')[0];
            var today = new Date().toISOString().split('T')[0];
            var isToday = date === today;
            var el = document.getElementById('broom-' + apt);
            var curState = el ? parseInt(el.dataset.state || '0') : 0;

            if (isToday && curState === 1) {
              // Ask: yes→2(cleaned), no→1(stay red), skip→0(transparent)
              var choice = await askClean(apt, date);
              if (choice === 'yes')  await applyClean(apt, date, 2);
              else if (choice === 'skip') await applyClean(apt, date, 0);
              // 'no' = stay red, do nothing
            } else if (isToday) {
              // 0→1 or 2→0
              await applyClean(apt, date, curState === 0 ? 1 : 0);
            } else {
              // Non-today: toggle 0↔1 only
              await applyClean(apt, date, curState === 0 ? 1 : 0);
            }
          };
          </script>
          {{ReservationPages.Footer()}}
        </body>
        </html>
        """;
}

// ── Calendar day-view HTML fragment (shared by full page + AJAX partial) ──────
string CalendarDayHtml(DateOnly sel, DateOnly today, List<CalAptInfo> apts, bool isAdmin, string lang, bool canToggleCleaning = false)
{
    string[] aptColor = { "#4a90d9", "#2ecc71", "#e67e22" };
    string[] aptDark  = { "#1a5276", "#1a7a43", "#a04000" };
    string[] aptFloor = { T.Get(lang, "1st Floor"), T.Get(lang, "2nd Floor"), T.Get(lang, "3rd Floor") };
    var aptDateLabel = sel == today             ? $"{sel.ToString("d MMM", T.Culture(lang))} <span class='apt-date-rel'>({T.Get(lang, "Today")})</span>"
                     : sel == today.AddDays(1)  ? $"{sel.ToString("d MMM", T.Culture(lang))} <span class='apt-date-rel'>({T.Get(lang, "Tomorrow")})</span>"
                     : sel.ToString("d MMM", T.Culture(lang));

    string StatusBg(string s) => s switch
    {
        "checkin" => "#e8f5e9", "checkout" => "#fff3e0",
        "occupied" => "#e8f0fb", "transition" => "#fffde7", _ => "#f9f9f9"
    };
    string StatusColor(string s) => s switch
    {
        "checkin" => "#27ae60", "checkout" => "#e67e22",
        "occupied" => "#2980b9", "transition" => "#f39c12", _ => "#bbb"
    };
    string StatusLabel(string s) => s switch
    {
        "checkin" => T.Get(lang, "Check-in ↓"), "checkout" => T.Get(lang, "Check-out ↑"),
        "occupied" => T.Get(lang, "Occupied"), "transition" => T.Get(lang, "Transition Day"), _ => T.Get(lang, "Vacant")
    };

    string GuestLine(ReservationRow? r, string gear = "")
    {
        if (r == null) return "";
        var g    = $"{r.Adults}/{r.Children}/{r.Infants}";
        var ci   = r.CheckInDate.ToString("MMM d", T.Culture(lang));
        var co   = r.CheckOutDate.ToString("MMM d", T.Culture(lang));
        var mail = !string.IsNullOrWhiteSpace(r.MessagesUrl)
            ? $"<a href='{System.Net.WebUtility.HtmlEncode(r.MessagesUrl)}' class='cal-mail-btn' target='_blank' title='Messages'>✉️</a>"
            : "";
        return $"<div class='cal-gline'>" +
               $"<a href='/reservations/{r.Id}' class='cal-gname-link'>{System.Net.WebUtility.HtmlEncode(r.ReservationName)}</a>" +
               $"{mail}{gear}</div>" +
               $"<div class='cal-gsub'>{g} · {ci} – {co} · {r.Nights}n</div>";
    }

    // Build icon tag row from effective values
    string IconTags(string? arrTime, string? coTime, bool earlyCI, bool crib, bool sofa, bool bags, string? arrivalMethod = null)
    {
        var sb = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(arrivalMethod))
        {
            var methodIcon = arrivalMethod.ToLower() switch {
                var s when s.Contains("airport") || s.Contains("flight")                    => "✈️",
                var s when s.Contains("car") || s.Contains("rental") || s.Contains("driv")
                        || s.Contains("taxi")                                               => "🚗",
                var s when s.Contains("train")                                              => "🚂",
                var s when s.Contains("bus") || s.Contains("public")                       => "🚌",
                var s when s.Contains("ferry") || s.Contains("boat")                       => "⛴️",
                var s when s.Contains("self")                                               => "🔑",
                _                                                                           => "🚗"
            };
            sb.Append($"<span class='cal-tag'>{methodIcon} {System.Net.WebUtility.HtmlEncode(arrivalMethod)}</span>");
        }
        if (!string.IsNullOrWhiteSpace(arrTime)) sb.Append($"<span class='cal-tag'>⏰ {System.Net.WebUtility.HtmlEncode(arrTime)}</span>");
        if (!string.IsNullOrWhiteSpace(coTime))  sb.Append($"<span class='cal-tag'>🕐 {System.Net.WebUtility.HtmlEncode(coTime)}</span>");
        if (earlyCI) sb.Append($"<span class='cal-tag'>⚡ {T.Get(lang, "Early CI")}</span>");
        if (crib)    sb.Append($"<span class='cal-tag'>🛏️ {T.Get(lang, "Crib")}</span>");
        if (sofa)    sb.Append($"<span class='cal-tag'>🛋️ {T.Get(lang, "Sofa")}</span>");
        if (bags)    sb.Append($"<span class='cal-tag'>🧳 {T.Get(lang, "Bags")}</span>");
        return sb.Length > 0 ? $"<div class='cal-tags'>{sb}</div>" : "";
    }

    // Build admin note-edit button with data attributes
    // mode: "checkin" | "checkout" | "occupied"
    string NoteBtn(ReservationRow? res, CalNote? note,
        string? regArr = null, bool regCrib = false, bool regECI = false, bool regSofa = false,
        string mode = "checkin")
    {
        if (!isAdmin || res == null) return "";
        var ci   = System.Net.WebUtility.HtmlEncode(note?.CheckInTime  ?? regArr ?? "");
        var co   = System.Net.WebUtility.HtmlEncode(note?.CheckOutTime ?? "");
        var crib = (note != null ? note.Crib        : regCrib).ToString().ToLower();
        var eci  = (note != null ? note.EarlyCheckIn: regECI ).ToString().ToLower();
        var sofa = (note != null ? note.SofaBed     : regSofa).ToString().ToLower();
        var bags = (note?.LeavingBags ?? false).ToString().ToLower();
        var name = System.Net.WebUtility.HtmlEncode(res.ReservationName);
        return $"<button class='cal-note-btn'" +
               $" data-res-id='{res.Id}' data-res-name='{name}' data-mode='{mode}'" +
               $" data-checkin-time='{ci}' data-checkout-time='{co}'" +
               $" data-crib='{crib}' data-earlyci='{eci}' data-sofabed='{sofa}' data-leavingbags='{bags}'>⚙</button>";
    }

    string AptCol(CalAptInfo info)
    {
        var idx = info.AptNumber - 1;
        var bg  = StatusBg(info.Status);
        var sc  = StatusColor(info.Status);
        var sl  = StatusLabel(info.Status);

        // Effective icon values: NoteIn overrides registration
        var effArr  = info.NoteIn?.CheckInTime  ?? info.ArrivalTime;
        var effCrib = info.NoteIn != null ? info.NoteIn.Crib         : info.Crib;
        var effECI  = info.NoteIn != null ? info.NoteIn.EarlyCheckIn : info.EarlyCheckIn;
        var effSofa = info.NoteIn != null ? info.NoteIn.SofaBed      : info.Sofa;

        var otherReqHtml = !string.IsNullOrWhiteSpace(info.OtherRequests)
            ? $"<div class='cal-req'>{System.Net.WebUtility.HtmlEncode(info.OtherRequests)}</div>"
            : "";

        string body = info.Status switch
        {
            "checkin" => $"""
                {GuestLine(info.CheckIn, NoteBtn(info.CheckIn, info.NoteIn, info.ArrivalTime, info.Crib, info.EarlyCheckIn, info.Sofa, "checkin"))}
                {IconTags(effArr, null, effECI, effCrib, effSofa, false, info.ArrivalMethod)}
                {otherReqHtml}
                """,
            "checkout" => $"""
                {GuestLine(info.CheckOut, NoteBtn(info.CheckOut, info.NoteOut, mode: "checkout"))}
                {IconTags(null, info.NoteOut?.CheckOutTime, false, false, false, info.NoteOut?.LeavingBags ?? false)}
                """,
            "occupied" => $"""
                {GuestLine(info.InStay, NoteBtn(info.InStay, info.NoteIn, mode: "occupied"))}
                <div class='cal-arrival'>{T.Get(lang, "Checkout:")} <strong>{info.InStay?.CheckOutDate.ToString("MMM d", T.Culture(lang))}</strong> · Day {(sel.DayNumber - (info.InStay?.CheckInDate.DayNumber ?? sel.DayNumber)) + 1} of {info.InStay?.Nights}</div>
                {IconTags(null, info.NoteIn?.CheckOutTime, false, false, false, info.NoteIn?.LeavingBags ?? false)}
                """,
            "transition" => $"""
                <div class='cal-divider'>{T.Get(lang, "↑ Checking Out")}</div>
                {GuestLine(info.CheckOut, NoteBtn(info.CheckOut, info.NoteOut, mode: "checkout"))}
                {IconTags(null, info.NoteOut?.CheckOutTime, false, false, false, info.NoteOut?.LeavingBags ?? false)}
                <div class='cal-divider' style='margin-top:.5rem'>{T.Get(lang, "↓ Checking In")}</div>
                {GuestLine(info.CheckIn, NoteBtn(info.CheckIn, info.NoteIn, info.ArrivalTime, info.Crib, info.EarlyCheckIn, info.Sofa, "checkin"))}
                {IconTags(effArr, null, effECI, effCrib, effSofa, false, info.ArrivalMethod)}
                {otherReqHtml}
                """,
            _ => "<div class='cal-vacant-icon'>🏠</div>"
        };

        var broomClick = canToggleCleaning ? $" onclick=\"toggleCleaning({info.AptNumber})\" class='apt-broom clickable'" : " class='apt-broom'";
        return $"""
            <div class='cal-apt-col apt-col-{info.AptNumber}' style='background:{bg}'>
              <div class='apt-img-wrap'>
                <img class='apt-img' src='/images/apartment{info.AptNumber}.jpg' alt='Apt {info.AptNumber}'/>
                <div id='broom-{info.AptNumber}' data-state='{info.CleaningState}'{broomClick}>🧹</div>
              </div>
              <div class='cal-apt-header'>
                <span class='apt-badge' style='background:{aptColor[idx]}'>{info.AptNumber}</span>
                <span style='font-weight:600;font-size:.85rem'>{aptFloor[idx]}</span>
              </div>
              <span class='cal-status-badge' style='background:{sc}'>{sl}</span>
              <div class='cal-apt-body'>{body}</div>
            </div>
            """;
    }

    var prevDay    = sel.AddDays(-1);
    var nextDay    = sel.AddDays(1);
    var selDateStr = lang == "ru"
        ? sel.ToString("dddd, d MMMM yyyy", T.Culture(lang))
        : sel.ToString("dddd, MMMM d, yyyy", T.Culture(lang));
    var aptColsHtml = string.Join("", apts.Select(AptCol));
    var todayHref = $"<a href='/calendar?date={today:yyyy-MM-dd}' class='day-nav-arrow' style='font-size:.8rem;padding:.25rem .65rem'>{T.Get(lang, "Go to today")}</a>";
    var leftBtn  = sel > today ? todayHref : "";
    var rightBtn = sel < today ? todayHref : "";

    return $"""
        <div class="day-nav">
          <a href="/calendar?date={prevDay:yyyy-MM-dd}" class="day-nav-arrow">‹</a>
          {leftBtn}
          <h2>{selDateStr}</h2>
          {rightBtn}
          <a href="/calendar?date={nextDay:yyyy-MM-dd}" class="day-nav-arrow">›</a>
        </div>
        <div class="apt-date-bar">{aptDateLabel}</div>
        <div class="apt-cols">
          {aptColsHtml}
        </div>
        """;
}

string RemindersPage(List<ReminderAdminRow> rows, string lang = "en", bool isAdmin = false) => $$"""
    <!DOCTYPE html>
    <html lang="{{lang}}">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>{{T.Get(lang, "Reminders")}} – Casa Rosa Admin</title>
      <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
      <style>{{ReservationPages.Css}}</style>
    </head>
    <body>
      {{ReservationPages.Header("reminders", lang, isAdmin)}}
      <main>
        <div class="page-header">
          <h1>{{T.Get(lang, "Reminders")}}</h1>
          <button class="btn btn-primary" onclick="openNewReminder()">+ {{T.Get(lang, "New Reminder")}}</button>
        </div>
        <div class="card">
          {{(rows.Count == 0
              ? $"<p style='color:#aaa;font-size:.9rem'>{T.Get(lang, "No pending reminders.")}</p>"
              : $"""
                <table class="res-table">
                  <thead><tr>
                    <th>{T.Get(lang, "Scheduled (Portugal)")}</th>
                    <th>{T.Get(lang, "Message")}</th>
                    <th>{T.Get(lang, "Language")}</th>
                    <th>{T.Get(lang, "Bot")}</th>
                    <th></th>
                  </tr></thead>
                  <tbody>
                    {string.Join("", rows.Select(r => {
                        var ptTz  = TimeZoneInfo.FindSystemTimeZoneById("Europe/Lisbon");
                        var local = r.ScheduledAt.Kind == DateTimeKind.Utc
                                    ? TimeZoneInfo.ConvertTimeFromUtc(r.ScheduledAt, ptTz)
                                    : r.ScheduledAt;
                        var iso     = local.ToString("yyyy-MM-ddTHH:mm");
                        var msgEnc  = System.Net.WebUtility.HtmlEncode(r.Message);
                        var msgAttr = System.Net.WebUtility.HtmlEncode(r.Message);
                        var chVal   = r.ChannelId == -5129864639L ? "english" : "russian";
                        return $"""
                            <tr>
                              <td>{local.ToString("MMM d, yyyy  h:mm tt", T.Culture(lang))}</td>
                              <td>{msgEnc}</td>
                              <td>{r.Language}</td>
                              <td>{r.BotId}</td>
                              <td style="display:flex;gap:.4rem;align-items:center">
                                <button class="btn btn-sm" onclick="openEditReminder({r.Id},'{iso}',this)"
                                  data-msg="{msgAttr}" data-channel="{chVal}" data-bot="{r.BotId}" data-lang="{r.Language}">✏️</button>
                                <form method="post" action="/reminders/{r.Id}/delete" style="margin:0"
                                  onsubmit="return confirm('Delete this reminder?')">
                                  <button class="btn btn-danger btn-sm" type="submit">🗑</button>
                                </form>
                              </td>
                            </tr>
                            """;
                    }))}
                  </tbody>
                </table>
                """)}}
        </div>
      </main>

      <dialog id="reminder-dlg" style="border-radius:10px;border:1px solid #ddd;padding:1.5rem;min-width:360px">
        <h3 id="dlg-title" style="margin-top:0"></h3>
        <form method="post" id="reminder-form">
          <div class="field">
            <label>{{T.Get(lang, "Scheduled (Portugal)")}}</label>
            <input type="datetime-local" name="scheduledAt" id="dlg-time" required/>
          </div>
          <div class="field">
            <label>{{T.Get(lang, "Message")}}</label>
            <textarea name="message" id="dlg-msg" rows="4" style="width:100%;box-sizing:border-box"></textarea>
          </div>
          <div class="field" id="dlg-channel-row">
            <label>{{T.Get(lang, "Channel")}}</label>
            <select name="channel" id="dlg-channel">
              <option value="russian">Russian (-5186091931)</option>
              <option value="english">English (-5129864639)</option>
            </select>
          </div>
          <div class="field" id="dlg-bot-row">
            <label>{{T.Get(lang, "Bot")}}</label>
            <select name="bot" id="dlg-bot">
              <option value="Auto_Bot">Auto_Bot</option>
              <option value="Cesar_bot">Cesar_bot</option>
            </select>
          </div>
          <div class="field" id="dlg-lang-row">
            <label>{{T.Get(lang, "Language")}}</label>
            <select name="language" id="dlg-lang">
              <option value="Russian">Russian</option>
              <option value="English">English</option>
            </select>
          </div>
          <div style="display:flex;gap:.5rem;justify-content:flex-end;margin-top:.75rem">
            <button type="button" class="btn" onclick="document.getElementById('reminder-dlg').close()">{{T.Get(lang, "Cancel")}}</button>
            <button type="submit" class="btn btn-primary">{{T.Get(lang, "Save")}}</button>
          </div>
        </form>
      </dialog>

      <script>
      function openNewReminder() {
        var dlg = document.getElementById('reminder-dlg');
        document.getElementById('dlg-title').textContent = '{{T.Get(lang, "New Reminder")}}';
        document.getElementById('reminder-form').action  = '/reminders/create';
        document.getElementById('dlg-time').value        = '';
        document.getElementById('dlg-msg').value         = '';
        document.getElementById('dlg-channel').value     = 'russian';
        document.getElementById('dlg-bot').value         = 'Auto_Bot';
        document.getElementById('dlg-lang').value        = 'Russian';
        document.getElementById('dlg-channel-row').style.display = '';
        document.getElementById('dlg-bot-row').style.display     = '';
        document.getElementById('dlg-lang-row').style.display    = '';
        dlg.showModal();
      }
      function openEditReminder(id, iso, btn) {
        var dlg = document.getElementById('reminder-dlg');
        document.getElementById('dlg-title').textContent = '{{T.Get(lang, "Edit Reminder")}}';
        document.getElementById('reminder-form').action  = '/reminders/' + id + '/edit';
        document.getElementById('dlg-time').value        = iso;
        document.getElementById('dlg-msg').value         = btn.dataset.msg;
        // hide channel/bot/lang for edits (not changeable)
        document.getElementById('dlg-channel-row').style.display = 'none';
        document.getElementById('dlg-bot-row').style.display     = 'none';
        document.getElementById('dlg-lang-row').style.display    = 'none';
        dlg.showModal();
      }
      </script>
      {{ReservationPages.Footer()}}
    </body>
    </html>
    """;

string LoginPage(string error = "", string lang = "en", bool showGoogle = false) => $$"""
    <!DOCTYPE html>
    <html lang="{{lang}}">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>Casa Rosa Admin</title>
      <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
      <style>
        {{ReservationPages.Css}}
        body { display: flex; align-items: center; justify-content: center; min-height: 100vh;
               background-image: url('/CasaRosaAtNight.jpg');
               background-size: cover; background-position: center; }
        .login-card { background: rgba(255,255,255,.92); border-radius: 10px;
                      box-shadow: 0 4px 24px rgba(0,0,0,.3);
                      padding: 2.5rem 2rem; width: 100%; max-width: 360px; }
        .login-card .logo { display: flex; flex-direction: column; align-items: center;
                            gap: .5rem; margin-bottom: 1.5rem; }
        .login-card .logo img { height: 90px; width: auto; }
        .login-card .logo span { font-size: 1.2rem; font-weight: 700; color: rgb(33,37,41); }
        .err { background: #fdecea; color: #b71c1c; border-radius: 6px;
               padding: .6rem .8rem; font-size: .85rem; margin-bottom: 1rem; }
        .lang-toggle { position:fixed; top:.75rem; right:1rem; display:flex; gap:.4rem; }
        .lang-toggle a { background:rgba(255,255,255,.75); border-radius:5px; padding:.25rem .6rem;
                         font-size:.8rem; font-weight:600; color:rgb(33,37,41); text-decoration:none; }
        .lang-toggle a.active { background:rgb(33,37,41); color:#fff; }
      </style>
    </head>
    <body>
      <div class="lang-toggle">
        <a href="/lang/en" class="{{(lang == "en" ? "active" : "")}}">EN</a>
        <a href="/lang/ru" class="{{(lang == "ru" ? "active" : "")}}">RU</a>
      </div>
      <div class="login-card">
        <div class="logo"><img src="/logo.png" alt="Casa Rosa"/><span>Casa Rosa Admin</span></div>
        {{(string.IsNullOrEmpty(error) ? "" : $"<div class=\"err\">{error}</div>")}}
        <form method="post" action="/login">
          <div class="field"><label for="u">{{T.Get(lang, "Username")}}</label>
            <input id="u" name="username" type="text" autocomplete="username" autofocus/></div>
          <div class="field"><label for="p">{{T.Get(lang, "Password")}}</label>
            <input id="p" name="password" type="password" autocomplete="current-password"/></div>
          <button class="btn btn-primary" style="width:100%" type="submit">{{T.Get(lang, "Sign in")}}</button>
        </form>
        {{(showGoogle ? $"""
        <div style="margin-top:1rem;border-top:1px solid #eee;padding-top:1rem">
          <a href="/auth/google" style="display:flex;align-items:center;justify-content:center;gap:.6rem;
             padding:.55rem 1rem;border:1px solid #ddd;border-radius:6px;font-size:.9rem;
             color:#333;text-decoration:none;background:#fff;font-weight:500">
            <svg width="18" height="18" viewBox="0 0 18 18" xmlns="http://www.w3.org/2000/svg">
              <path fill="#4285F4" d="M17.64 9.2c0-.637-.057-1.25-.164-1.84H9v3.48h4.844c-.209 1.125-.843 2.078-1.796 2.717v2.258h2.908c1.702-1.567 2.684-3.875 2.684-6.615z"/>
              <path fill="#34A853" d="M9 18c2.43 0 4.467-.806 5.956-2.18l-2.908-2.259c-.806.54-1.837.86-3.048.86-2.344 0-4.328-1.584-5.036-3.711H.957v2.332C2.438 15.983 5.482 18 9 18z"/>
              <path fill="#FBBC05" d="M3.964 10.71c-.18-.54-.282-1.117-.282-1.71s.102-1.17.282-1.71V4.958H.957C.347 6.173 0 7.548 0 9s.348 2.827.957 4.042l3.007-2.332z"/>
              <path fill="#EA4335" d="M9 3.58c1.321 0 2.508.454 3.44 1.345l2.582-2.58C13.463.891 11.426 0 9 0 5.482 0 2.438 2.017.957 4.958L3.964 7.29C4.672 5.163 6.656 3.58 9 3.58z"/>
            </svg>
            {T.Get(lang, "Sign in with Google")}
          </a>
        </div>
        """ : "")}}
      </div>
      {{ReservationPages.Footer()}}
    </body>
    </html>
    """;

string DashboardPage(string tomorrowTime, string tomorrowChannel, string todayTime, string todayChannel, string tripleTime, string tripleChannel, string message = "", bool isAdmin = false, string lang = "en") => $$"""
    <!DOCTYPE html>
    <html lang="{{lang}}">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>{{T.Get(lang, "Settings")}} – Casa Rosa Admin</title>
      <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
      <style>{{ReservationPages.Css}}</style>
    </head>
    <body>
      {{ReservationPages.Header("settings", lang, isAdmin)}}
      <main>
        <div class="page-header"><h1>{{T.Get(lang, "Settings")}}</h1></div>
        {{(isAdmin ? """<form method="post" action="/dashboard">""" : "<fieldset disabled>")}}
        <div class="card">
          <p class="section-title">{{T.Get(lang, "Auto-Bot – Tomorrow's Briefing")}}</p>
          <div class="grid2" style="max-width:520px">
            <div class="field">
              <label for="tt">{{T.Get(lang, "Send time (Portugal time)")}}</label>
              <input id="tt" type="time" name="tomorrow_time" value="{{tomorrowTime}}"/>
            </div>
            <div class="field">
              <label for="tc">{{T.Get(lang, "Destination channel")}}</label>
              <select id="tc" name="tomorrow_channel">
                {{Option("-5129864639", "Casa Rosa English",    tomorrowChannel)}}
                {{Option("-5186091931", "Casa Rosa Management", tomorrowChannel)}}
                {{Option("-5209557963", "Translator",           tomorrowChannel)}}
                {{Option("-5271439382", "Rapid Response",       tomorrowChannel)}}
              </select>
              <div style="font-size:.78rem;color:#999;margin-top:.3rem">{{T.Get(lang, "Auto_Bot sends the tomorrow checkout / check-in summary to this channel.")}}</div>
            </div>
          </div>
          {{(isAdmin ? """<button type="button" class="btn btn-secondary" style="margin-top:.5rem;font-size:.82rem" onclick="triggerBriefing('tomorrow',this)">▶ Send now</button>""" : "")}}
        </div>
        <div class="card" style="margin-top:1rem">
          <p class="section-title">{{T.Get(lang, "Auto-Bot – Today's Briefing")}}</p>
          <div class="grid2" style="max-width:520px">
            <div class="field">
              <label for="yt">{{T.Get(lang, "Send time (Portugal time)")}}</label>
              <input id="yt" type="time" name="today_time" value="{{todayTime}}"/>
            </div>
            <div class="field">
              <label for="yc">{{T.Get(lang, "Destination channel")}}</label>
              <select id="yc" name="today_channel">
                {{Option("-5129864639", "Casa Rosa English",    todayChannel)}}
                {{Option("-5186091931", "Casa Rosa Management", todayChannel)}}
                {{Option("-5209557963", "Translator",           todayChannel)}}
                {{Option("-5271439382", "Rapid Response",       todayChannel)}}
              </select>
              <div style="font-size:.78rem;color:#999;margin-top:.3rem">{{T.Get(lang, "Auto_Bot sends the today checkout / check-in summary to this channel.")}}</div>
            </div>
          </div>
          {{(isAdmin ? """<button type="button" class="btn btn-secondary" style="margin-top:.5rem;font-size:.82rem" onclick="triggerBriefing('today',this)">▶ Send now</button>""" : "")}}
        </div>
        <div class="card" style="margin-top:1rem">
          <p class="section-title">{{T.Get(lang, "Auto-Bot – Triple Cleaning Alert")}}</p>
          <div class="grid2" style="max-width:520px">
            <div class="field">
              <label for="trt">{{T.Get(lang, "Send time (Portugal time)")}}</label>
              <input id="trt" type="time" name="triple_time" value="{{tripleTime}}"/>
            </div>
            <div class="field">
              <label for="trc">{{T.Get(lang, "Destination channel")}}</label>
              <select id="trc" name="triple_channel">
                {{Option("-5129864639", "Casa Rosa English",    tripleChannel)}}
                {{Option("-5186091931", "Casa Rosa Management", tripleChannel)}}
                {{Option("-5209557963", "Translator",           tripleChannel)}}
                {{Option("-5271439382", "Rapid Response",       tripleChannel)}}
              </select>
              <div style="font-size:.78rem;color:#999;margin-top:.3rem">{{T.Get(lang, "Auto_Bot sends a triple cleaning warning when 3 apartments need cleaning on the same day within the next 7 days.")}}</div>
            </div>
          </div>
          {{(isAdmin ? """<button type="button" class="btn btn-secondary" style="margin-top:.5rem;font-size:.82rem" onclick="triggerBriefing('triple',this)">▶ Send now</button>""" : "")}}
        </div>
        {{(isAdmin ? $"""<button class="btn btn-primary" type="submit" style="margin-top:1rem">{T.Get(lang, "Save")}</button>""" : "")}}
        {{(string.IsNullOrEmpty(message) ? "" : $"<div class=\"ok-msg\" style=\"margin-top:.8rem\">&#10003; {message}</div>")}}
        {{(isAdmin ? "</form>" : "</fieldset>")}}
      </main>
      {{ReservationPages.Footer()}}
      <script>
      async function triggerBriefing(type, btn) {
        var orig = btn.textContent;
        btn.disabled = true; btn.textContent = '⏳';
        try {
          var r = await fetch('/dashboard/trigger/' + type, {method:'POST'});
          btn.textContent = r.ok ? '✓ Sent' : '✗ Error';
        } catch(e) { btn.textContent = '✗ Error'; }
        setTimeout(function(){ btn.disabled = false; btn.textContent = orig; }, 3000);
      }
      </script>
    </body>
    </html>
    """;

string Option(string value, string label, string selected) =>
    $"<option value=\"{value}\"{(value == selected ? " selected" : "")}>{label}</option>";

string AuditLogPage(List<AuditLogEntry> entries, string lang = "en") => $$"""
    <!DOCTYPE html>
    <html lang="{{lang}}">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width, initial-scale=1"/>
      <title>{{T.Get(lang, "Audit Log")}} – Casa Rosa Admin</title>
      <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
      <style>
        {{ReservationPages.Css}}
        .audit-table { width:100%; border-collapse:collapse; font-size:.87rem; }
        .audit-table th { text-align:left; padding:.5rem .75rem; border-bottom:2px solid #e8e8e8;
                          font-size:.75rem; font-weight:700; text-transform:uppercase;
                          letter-spacing:.05em; color:#aaa; white-space:nowrap; }
        .audit-table td { padding:.5rem .75rem; border-bottom:1px solid #f0f0f0; vertical-align:top; }
        .audit-table tr:last-child td { border-bottom:none; }
        .audit-table tr:hover td { background:#fafafa; }
        .audit-time { color:#999; white-space:nowrap; }
        .audit-actor { font-weight:600; white-space:nowrap; }
        .audit-detail { color:#666; }
      </style>
    </head>
    <body>
      {{ReservationPages.Header("auditlog", lang, isAdmin: true)}}
      <main>
        <div class="page-header"><h1>{{T.Get(lang, "Audit Log")}}</h1></div>
        <div class="card" style="padding:0;overflow:hidden">
          {{(entries.Count == 0
              ? $"<p style='padding:1.5rem;color:#999'>{T.Get(lang, "No audit entries.")}</p>"
              : $"""
                <table class="audit-table">
                  <thead><tr>
                    <th>{T.Get(lang, "Time")}</th>
                    <th>{T.Get(lang, "Actor")}</th>
                    <th>{T.Get(lang, "Action")}</th>
                    <th>{T.Get(lang, "Detail")}</th>
                  </tr></thead>
                  <tbody>
                    {string.Join("\n", entries.Select(e => $"""
                      <tr>
                        <td class="audit-time">{e.At.ToString("dd MMM HH:mm", System.Globalization.CultureInfo.InvariantCulture)}</td>
                        <td class="audit-actor">{System.Net.WebUtility.HtmlEncode(e.Actor)}</td>
                        <td>{System.Net.WebUtility.HtmlEncode(e.Action)}</td>
                        <td class="audit-detail">{System.Net.WebUtility.HtmlEncode(e.Detail ?? "")}</td>
                      </tr>
                    """))}
                  </tbody>
                </table>
              """)}}
        </div>
      </main>
      {{ReservationPages.Footer()}}
    </body>
    </html>
    """;

string TelegramLogPage(List<TelegramLogRow> entries, string lang = "en") => $$"""
    <!DOCTYPE html>
    <html lang="{{lang}}">
    <head>
      <meta charset="utf-8"/>
      <meta name="viewport" content="width=device-width,initial-scale=1"/>
      <title>{{T.Get(lang, "Telegram Log")}} – Casa Rosa Admin</title>
      <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
      <style>
        {{ReservationPages.Css}}
        .tlog-table { width:100%; border-collapse:collapse; font-size:.85rem; }
        .tlog-table th { text-align:left; padding:.45rem .6rem; background:#f5f5f5; border-bottom:2px solid #e0e0e0; white-space:nowrap; }
        .tlog-table td { padding:.4rem .6rem; border-bottom:1px solid #f0f0f0; vertical-align:top; }
        .tlog-table tr:hover td { background:#fafafa; }
        .tlog-badge { display:inline-block; padding:.15rem .45rem; border-radius:4px; font-size:.75rem; font-weight:600; white-space:nowrap; }
        .tlog-sent      { background:#d4edda; color:#155724; }
        .tlog-scheduled { background:#cce5ff; color:#004085; }
        .tlog-error     { background:#f8d7da; color:#721c24; }
        .tlog-type      { font-weight:600; white-space:nowrap; }
        .tlog-channel   { color:#666; white-space:nowrap; font-size:.8rem; }
        .tlog-summary   { color:#444; word-break:break-word; max-width:500px; }
        .tlog-time      { color:#888; white-space:nowrap; font-size:.8rem; }
        .tlog-empty     { padding:2rem; text-align:center; color:#999; }
        .page-header-row { display:flex; align-items:center; gap:1rem; margin-bottom:1rem; }
        .tlog-refresh { font-size:.8rem; color:#888; }
      </style>
      <script>setTimeout(() => location.reload(), 30000);</script>
    </head>
    <body>
      {{ReservationPages.Header("log", lang, isAdmin: true)}}
      <main>
        <div class="page-header">
          <h1>{{T.Get(lang, "Telegram Log")}}</h1>
          <p class="tlog-refresh">{{T.Get(lang, "Auto-refreshes every 30 seconds")}}</p>
        </div>
        {{(entries.Count == 0
            ? $"<div class='tlog-empty'>{T.Get(lang, "No log entries yet.")}</div>"
            : $"""
              <div style="overflow-x:auto">
                <table class="tlog-table">
                  <thead>
                    <tr>
                      <th>{T.Get(lang, "Time")} (UTC)</th>
                      <th>{T.Get(lang, "Event")}</th>
                      <th>{T.Get(lang, "Type")}</th>
                      <th>{T.Get(lang, "Channel")}</th>
                      <th>{T.Get(lang, "Summary")}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {string.Join("", entries.Select(e =>
                    {
                        var badgeCls = e.IsError ? "tlog-error" : e.EventType == "scheduled" ? "tlog-scheduled" : "tlog-sent";
                        var badgeLabel = e.IsError ? "error" : e.EventType;
                        var timeStr = e.At.ToString("dd MMM HH:mm:ss");
                        return $"""
                            <tr>
                              <td class='tlog-time'>{timeStr}</td>
                              <td><span class='tlog-badge {badgeCls}'>{badgeLabel}</span></td>
                              <td class='tlog-type'>{System.Net.WebUtility.HtmlEncode(e.MessageType)}</td>
                              <td class='tlog-channel'>{System.Net.WebUtility.HtmlEncode(e.Channel ?? "")}</td>
                              <td class='tlog-summary'>{System.Net.WebUtility.HtmlEncode(e.Summary ?? "")}</td>
                            </tr>
                            """;
                    }))}
                  </tbody>
                </table>
              </div>
              """)}}
      </main>
      {{ReservationPages.Footer()}}
    </body>
    </html>
    """;

string UsersPage(
    List<AdminUserRow> users,
    string error = "", string message = "", bool isAdmin = false, string lang = "en")
{
    var rows = new System.Text.StringBuilder();
    foreach (var u in users)
    {
        var roleColor = u.Role switch
        {
            "Admin"  => "background:#d4edda;color:#155724",
            "Helper" => "background:#cce5ff;color:#004085",
            _        => "background:#f5f5f5;color:#aaa"
        };
        var roleBadge    = $"<span style='{roleColor};padding:.2rem .5rem;border-radius:4px;font-size:.78rem'>{u.Role}</span>";
        var editControls = isAdmin ? $"""
              <td>
                <form method="post" action="/users/{u.Id}/password"
                      style="display:inline-flex;gap:.4rem;align-items:center">
                  <input type="password" name="password" placeholder="{T.Get(lang, "New password")}"
                         style="padding:.4rem .6rem;border:1px solid #ddd;border-radius:5px;font-size:.85rem;width:140px"/>
                  <input type="password" name="confirm" placeholder="{T.Get(lang, "Confirm")}"
                         style="padding:.4rem .6rem;border:1px solid #ddd;border-radius:5px;font-size:.85rem;width:110px"/>
                  <button class="btn btn-secondary btn-sm" type="submit">{T.Get(lang, "Change")}</button>
                </form>
              </td>
              <td>
                <form method="post" action="/users/{u.Id}/google-email"
                      style="display:inline-flex;gap:.35rem;align-items:center">
                  <input type="email" name="email" value="{System.Net.WebUtility.HtmlEncode(u.GoogleEmail ?? "")}"
                         placeholder="{T.Get(lang, "Google Email")}"
                         style="padding:.4rem .6rem;border:1px solid #ddd;border-radius:5px;font-size:.82rem;width:185px"/>
                  <button class="btn btn-secondary btn-sm" type="submit">{T.Get(lang, "Link")}</button>
                </form>
              </td>
              <td style="display:flex;gap:.4rem;align-items:center">
                <form method="post" action="/users/{u.Id}/set-role" style="margin:0;display:inline-flex;gap:.3rem;align-items:center">
                  <select name="role" style="padding:.3rem .5rem;border:1px solid #ddd;border-radius:5px;font-size:.82rem">
                    <option value="Admin"  {(u.Role == "Admin"  ? "selected" : "")}>Admin</option>
                    <option value="Viewer" {(u.Role == "Viewer" ? "selected" : "")}>Viewer</option>
                    <option value="Helper" {(u.Role == "Helper" ? "selected" : "")}>Helper</option>
                  </select>
                  <button class="btn btn-secondary btn-sm" type="submit">{T.Get(lang, "Set")}</button>
                </form>
                <form method="post" action="/users/{u.Id}/delete" style="margin:0"
                      onsubmit="return confirm('{T.Get(lang, "Delete user")} {System.Net.WebUtility.HtmlEncode(u.Username)}?')">
                  <button class="btn btn-danger btn-sm" type="submit">{T.Get(lang, "Delete")}</button>
                </form>
              </td>
            """ : "<td></td><td></td><td></td>";
        rows.Append($"""
            <tr>
              <td>{System.Net.WebUtility.HtmlEncode(u.Username)}</td>
              <td>{roleBadge}</td>
              <td style="color:#aaa;font-size:.85rem">{u.CreatedAt:yyyy-MM-dd}</td>
              {editControls}
            </tr>
            """);
    }

    var alert = !string.IsNullOrEmpty(error)
        ? $"<div class='ok-msg' style='background:#fdecea;color:#b71c1c'>{System.Net.WebUtility.HtmlEncode(error)}</div>"
        : !string.IsNullOrEmpty(message)
        ? $"<div class='ok-msg'>✓ {System.Net.WebUtility.HtmlEncode(message)}</div>"
        : "";

    var addUserSection = isAdmin ? $"""
        <div class="card">
          <p class="section-title">{T.Get(lang, "Add New User")}</p>
          <form method="post" action="/users">
            <div class="grid3">
              <div class="field"><label>{T.Get(lang, "Username")}</label>
                <input type="text" name="username" required autocomplete="off"/></div>
              <div class="field"><label>{T.Get(lang, "Password")}</label>
                <input type="password" name="password" required autocomplete="new-password"/></div>
              <div class="field"><label>{T.Get(lang, "Confirm Password")}</label>
                <input type="password" name="confirm" required autocomplete="new-password"/></div>
            </div>
            <button class="btn btn-primary" type="submit">{T.Get(lang, "Add User")}</button>
          </form>
        </div>
        """ : "";

    return $"""
        <!DOCTYPE html>
        <html lang="{lang}">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1"/>
          <title>{T.Get(lang, "Users")} – Casa Rosa Admin</title>
          <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
          <style>{ReservationPages.Css}</style>
        </head>
        <body>
          {ReservationPages.Header("users", lang, isAdmin)}
          <main>
            <div class="page-header"><h1>{T.Get(lang, "Users")}</h1></div>
            {alert}
            <div class="card" style="padding:0;overflow:hidden">
              <table class="res-table">
                <thead><tr><th>{T.Get(lang, "Username")}</th><th>{T.Get(lang, "Role")}</th><th>{T.Get(lang, "Created")}</th><th>{T.Get(lang, "Password")}</th><th>{T.Get(lang, "Google Email")}</th><th></th></tr></thead>
                <tbody>{rows}</tbody>
              </table>
            </div>
            {addUserSection}
          </main>
          {ReservationPages.Footer()}
        </body>
        </html>
        """;
}

record CalAptInfo(
    int AptNumber, string Status,
    ReservationRow? CheckIn, ReservationRow? CheckOut, ReservationRow? InStay,
    string? ArrivalTime, bool EarlyCheckIn, bool Crib, bool Sofa, string? OtherRequests,
    CalNote? NoteIn, CalNote? NoteOut, int CleaningState = 0, string? ArrivalMethod = null);

record TelegramLogRow(int Id, DateTime At, string EventType, string MessageType, string? Channel, string? Summary, bool IsError);
