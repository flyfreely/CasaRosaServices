using System.Data;
using Microsoft.Data.SqlClient;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .Enrich.WithProperty("Source", "cr_reservationapi")
    .WriteTo.Http(
        requestUri:    "http://192.168.48.1:5100/api/logs",
        queueLimitBytes: null,
        textFormatter: new CompactJsonFormatter(),
        httpClient:    new TelemetryHttpClient())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();
var app     = builder.Build();

var connStr   = builder.Configuration["Database:ConnectionString"]!;
var authToken = builder.Configuration["Http:AuthToken"]!;

ManageDb.Init(connStr);
AdminDb.Init(connStr);
PublicDb.Init(connStr);
await AdminDb.EnsureAsync();
await ManageDb.EnsureAsync();

// ── Auth (skips /public/*) ────────────────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    var isPublic = path.StartsWith("/public/", StringComparison.OrdinalIgnoreCase)
                || path.Equals("/public", StringComparison.OrdinalIgnoreCase);
    if (isPublic) { await next(); return; }
    var token = ctx.Request.Query["Token"].FirstOrDefault()
             ?? ctx.Request.Query["token"].FirstOrDefault()
             ?? "";
    if (token != authToken) { ctx.Response.StatusCode = 404; return; }
    await next();
});

// ── Public endpoints (no auth required) ──────────────────────────────────────
app.MapGet("/public/reservation/{guid}", async (string guid) =>
{
    if (!Guid.TryParse(guid, out var id)) return Results.BadRequest("Invalid GUID.");
    var info = await PublicDb.GetReservationInfoAsync(id);
    return info is null ? Results.NotFound() : Results.Ok(info);
});

app.MapPost("/public/register", async (PublicRegisterRequest req) =>
{
    try
    {
        await PublicDb.SaveRegistrationAsync(req);
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGet("/reservations", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "both"));

app.MapGet("/checkin", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "checkin"));

app.MapGet("/checkout", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "checkout"));

// ── Manage endpoints (CRUD) ───────────────────────────────────────────────────

app.MapGet("/manage/reservations", async (int? apt, string? from, string? to, string? status) =>
{
    var aptVal    = apt ?? 0;
    var fromVal   = from    is not null ? DateOnly.Parse(from)   : DateOnly.FromDateTime(DateTime.Today.AddMonths(-1));
    var toVal     = to      is not null ? DateOnly.Parse(to)     : DateOnly.FromDateTime(DateTime.Today.AddMonths(3));
    var statusVal = status  ?? "active";
    return Results.Ok(await ManageDb.ListAsync(aptVal, fromVal, toVal, statusVal));
});

app.MapGet("/manage/reservations/{id:int}", async (int id) =>
{
    var res = await ManageDb.GetAsync(id);
    return res is null ? Results.NotFound() : Results.Ok(res);
});

app.MapPost("/manage/reservations", async (ReservationCreateRequest req) =>
{
    var id = await ManageDb.CreateAsync(req);
    if (id == -1) return Results.Conflict(new { error = "overlap" });
    return Results.Ok(new { id });
});

app.MapPost("/manage/reservations/{id:int}", async (int id, ReservationUpdateRequest req) =>
{
    await ManageDb.UpdateAsync(id, req);
    return Results.Ok();
});

app.MapGet("/manage/reservations/{id:int}/registration", async (int id) =>
{
    var res = await ManageDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg = await ManageDb.GetRegistrationAsync(res.RegistrationGuid);
    return reg is null ? Results.NotFound() : Results.Ok(reg);
});

app.MapPost("/manage/reservations/{id:int}/registration", async (int id, RegistrationWriteRequest? req) =>
{
    var res = await ManageDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg = await ManageDb.GetRegistrationAsync(res.RegistrationGuid);
    if (reg is null)
        await ManageDb.CreateRegistrationAsync(res);
    else if (req is not null)
        await ManageDb.UpdateRegistrationAsync(reg.Id, req);
    return Results.Ok();
});

app.MapGet("/manage/reservations/{id:int}/cal-note", async (int id) =>
{
    var note = await ManageDb.GetCalNoteAsync(id);
    return note is null ? Results.NotFound() : Results.Ok(note);
});

app.MapPost("/manage/reservations/{id:int}/cal-note", async (int id, CalNoteRequest req) =>
{
    await ManageDb.UpsertCalNoteAsync(id, req);
    return Results.Ok();
});

app.MapGet("/manage/cleaning", async (string from, string to) =>
    Results.Ok(await AdminDb.ListCleaningAsync(DateOnly.Parse(from), DateOnly.Parse(to))));

app.MapGet("/manage/cleaning/{date}/{apt:int}", async (string date, int apt) =>
{
    var d = DateOnly.Parse(date);
    return Results.Ok(new { state = await AdminDb.GetCleaningStateAsync(d, apt) });
});

app.MapPost("/manage/cleaning/{date}/{apt:int}/toggle", async (string date, int apt) =>
{
    var d = DateOnly.Parse(date);
    return Results.Ok(new { state = await AdminDb.ToggleCleaningAsync(d, apt) });
});

app.MapPost("/manage/cleaning/{date}/{apt:int}/set", async (string date, int apt, int state) =>
{
    var d = DateOnly.Parse(date);
    return Results.Ok(new { state = await AdminDb.SetCleaningAsync(d, apt, state) });
});

app.MapGet("/manage/reservations/{id:int}/guests", async (int id) =>
{
    var res = await ManageDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg = await ManageDb.GetRegistrationAsync(res.RegistrationGuid);
    if (reg is null) return Results.Ok(new List<ManageGuestRow>());
    return Results.Ok(await ManageDb.GetGuestsAsync(reg.Id));
});

app.MapPost("/manage/reservations/{id:int}/guests", async (int id, GuestWriteRequest req) =>
{
    var res = await ManageDb.GetAsync(id);
    if (res is null) return Results.NotFound();
    var reg = await ManageDb.GetRegistrationAsync(res.RegistrationGuid);
    if (reg is null) return Results.BadRequest("No registration exists for this reservation.");
    await ManageDb.AddGuestAsync(reg.Id, req);
    return Results.Ok();
});

app.MapPost("/manage/reservations/{id:int}/guests/{guestId:int}/delete", async (int id, int guestId) =>
{
    await ManageDb.DeleteGuestAsync(guestId);
    return Results.Ok();
});

// ── Admin endpoints ───────────────────────────────────────────────────────────

app.MapPost("/admin/login", async (AdminLoginRequest req) =>
{
    var login = await AdminDb.GetUserLoginAsync(req.Username);
    if (login == null || !AdminDb.VerifyPassword(req.Password, login.Value.Hash)) return Results.Unauthorized();
    return Results.Ok(new { id = login.Value.Id, role = login.Value.Role, lang = login.Value.Language });
});

app.MapGet("/admin/users", async () =>
    Results.Ok(await AdminDb.ListUsersAsync()));

app.MapGet("/admin/users/by-google-email", async (string email) =>
{
    var user = await AdminDb.GetUserByGoogleEmailAsync(email);
    if (user is null) return Results.NotFound();
    return Results.Ok(new { id = user.Value.Id, username = user.Value.Username,
                            role = user.Value.Role, lang = user.Value.Language });
});

app.MapPost("/admin/users/{id:int}/google-email", async (int id, AdminSetGoogleEmailRequest req) =>
{
    await AdminDb.SetGoogleEmailAsync(id, string.IsNullOrWhiteSpace(req.Email) ? null : req.Email);
    return Results.Ok();
});

app.MapPost("/admin/users", async (AdminCreateUserRequest req) =>
{
    await AdminDb.CreateUserAsync(req.Username, req.Password);
    return Results.Ok();
});

app.MapPost("/admin/users/{id:int}/password", async (int id, AdminUpdatePasswordRequest req) =>
{
    await AdminDb.UpdatePasswordAsync(id, req.Password);
    return Results.Ok();
});

app.MapPost("/admin/users/{id:int}/delete", async (int id) =>
{
    if (await AdminDb.AdminCountAsync() <= 1)
    {
        var users = await AdminDb.ListUsersAsync();
        if (users.FirstOrDefault(u => u.Id == id)?.Role == "Admin")
            return Results.BadRequest(new { error = "Cannot delete the last admin user." });
    }
    await AdminDb.DeleteUserAsync(id);
    return Results.Ok();
});

app.MapPost("/admin/users/{id:int}/language", async (int id, AdminSetLanguageRequest req) =>
{
    await AdminDb.UpdateUserLanguageAsync(id, req.Language == "ru" ? "ru" : "en");
    return Results.Ok();
});

app.MapPost("/admin/users/{id:int}/set-role", async (int id, AdminSetRoleRequest req) =>
{
    if (req.Role != "Admin" && req.Role != "Viewer" && req.Role != "Helper")
        return Results.BadRequest(new { error = "Invalid role." });
    if (req.Role != "Admin" && await AdminDb.AdminCountAsync() <= 1)
    {
        var users = await AdminDb.ListUsersAsync();
        if (users.FirstOrDefault(u => u.Id == id)?.Role == "Admin")
            return Results.BadRequest(new { error = "At least one admin must remain." });
    }
    await AdminDb.SetRoleAsync(id, req.Role);
    return Results.Ok();
});

app.MapGet("/manage/stats", async (int? year) =>
    Results.Ok(await ManageDb.GetStatsAsync(year ?? DateTime.Today.Year)));

app.MapGet("/admin/config/{key}", async (string key) =>
{
    var value = await AdminDb.GetConfigAsync(key);
    return value is null ? Results.NotFound() : Results.Ok(new { key, value });
});

app.MapPost("/admin/config/{key}", async (string key, AdminConfigSetRequest req) =>
{
    await AdminDb.SetConfigAsync(key, req.Value);
    return Results.Ok();
});

// ── Audit log endpoints ───────────────────────────────────────────────────────

app.MapPost("/admin/audit", async (AuditLogRequest req) =>
{
    await AdminDb.LogAuditAsync(req.Actor, req.Action, req.Detail);
    return Results.Ok();
});

app.MapGet("/admin/audit", async (int? limit) =>
    Results.Ok(await AdminDb.GetAuditLogsAsync(limit ?? 200)));

// ── Reminder endpoints ────────────────────────────────────────────────────────

app.MapGet("/admin/reminders", async () =>
    Results.Ok(await AdminDb.ListPendingRemindersAsync()));

app.MapPost("/admin/reminders", async (ReminderCreateRequest req) =>
{
    var id = await AdminDb.CreateReminderAsync(req.Message, req.ScheduledAt, req.ChannelId, req.BotId, req.Language);
    return Results.Ok(new { id });
});

app.MapPost("/admin/reminders/{id:int}/cancel", async (int id) =>
{
    await AdminDb.CancelReminderAsync(id);
    return Results.Ok();
});

app.MapPost("/admin/reminders/{id:int}/update", async (int id, ReminderUpdateRequest req) =>
{
    await AdminDb.UpdateReminderAsync(id, req.Message, req.ScheduledAt);
    return Results.Ok();
});

app.MapPost("/admin/reminders/{id:int}/delete", async (int id) =>
{
    await AdminDb.DeleteReminderAsync(id);
    return Results.Ok();
});

app.MapPost("/admin/reminders/{id:int}/sent", async (int id) =>
{
    await AdminDb.MarkReminderSentAsync(id);
    return Results.Ok();
});

app.MapPost("/admin/telegram-log", async (TelegramLogRequest req) =>
{
    await AdminDb.AddTelegramLogAsync(req.EventType, req.MessageType, req.Channel, req.Summary, req.IsError);
    return Results.Ok();
});

app.MapGet("/admin/telegram-log", async (int? limit) =>
    Results.Ok(await AdminDb.GetTelegramLogAsync(limit ?? 200)));

// ── Maintenance tasks ────────────────────────────────────────────────────────
app.MapGet("/admin/maintenance", async () =>
    Results.Ok(await AdminDb.ListMaintenanceTasksAsync()));

app.MapPost("/admin/maintenance/{id:int}/done", async (int id) =>
{
    await AdminDb.MarkMaintenanceDoneAsync(id);
    return Results.Ok();
});

app.MapPost("/admin/maintenance/{id:int}/interval", async (int id, MaintenanceIntervalRequest req) =>
{
    if (req.IntervalWeeks < 1) return Results.BadRequest("Interval must be at least 1 week.");
    await AdminDb.UpdateMaintenanceIntervalAsync(id, req.IntervalWeeks);
    return Results.Ok();
});

app.MapPost("/admin/maintenance/{id:int}/reminder-created", async (int id) =>
{
    await AdminDb.MarkMaintenanceReminderCreatedAsync(id);
    return Results.Ok();
});

app.Run();

// ── Request handler ───────────────────────────────────────────────────────────
static async Task<IResult> HandleAsync(string connStr, string? apartment, string? date, string mode)
{
    if (apartment is not (null or "*"))
    {
        if (!int.TryParse(apartment, out var n) || n < 1 || n > 3)
            return Results.BadRequest("apartment must be 1, 2, 3, or *");
    }
    return Results.Ok(await FetchAsync(connStr, apartment, date, mode));
}

// ── Date range ────────────────────────────────────────────────────────────────
static (DateOnly Start, DateOnly End) ParseRange(string? date)
{
    var today = DateOnly.FromDateTime(DateTime.Today);
    if (date is not null && DateOnly.TryParseExact(date, "yyyy-MM-dd", out var specific))
        return (specific, specific);
    return date?.ToLowerInvariant() switch
    {
        "tomorrow" => (today.AddDays(1), today.AddDays(1)),
        "week"     => (today, today.AddDays(6)),
        _          => (today, today)
    };
}

// ── Age calculation ───────────────────────────────────────────────────────────
static int? CalcAge(DateTime? birthDate)
{
    if (birthDate is null) return null;
    var today = DateTime.Today;
    var age   = today.Year - birthDate.Value.Year;
    if (birthDate.Value > today.AddYears(-age)) age--;
    return age;
}

// ── Data fetch ────────────────────────────────────────────────────────────────
static async Task<List<ReservationResponse>> FetchAsync(string connStr, string? apartment, string? date, string mode)
{
    var (start, end) = ParseRange(date);

    var aptFilter = apartment is null or "*"
        ? "r.ApartmentNumber BETWEEN 1 AND 3"
        : "r.ApartmentNumber = @apartment";

    var dateFilter = mode switch
    {
        "checkin"  => "r.CheckInDate  BETWEEN @start AND @end",
        "checkout" => "r.CheckOutDate BETWEEN @start AND @end",
        _          => "(r.CheckInDate BETWEEN @start AND @end OR r.CheckOutDate BETWEEN @start AND @end)"
    };

    var sql = $"""
        SELECT
            r.ApartmentNumber, r.ReservationName, r.ConfirmationCode, r.Status,
            r.CheckInDate, r.CheckOutDate, r.Nights, r.Adults, r.Children, r.Infants,
            r.GuestCount, r.PhoneNumber,
            reg.Id              AS RegistrationId,
            reg.Email,          reg.ArrivalMethod,  reg.ArrivalTime,   reg.FlightNumber,
            reg.EarlyCheckInRequested, reg.CribSetup, reg.SofaSetup,   reg.FoldableBed,
            reg.OtherRequests,  reg.ArrivalNotes
        FROM Reservation r
        LEFT JOIN Registration reg
            ON  r.RegistrationGuid <> '00000000-0000-0000-0000-000000000000'
            AND TRY_CAST(reg.guid AS uniqueidentifier) = r.RegistrationGuid
            AND reg.Enabled = 1
        WHERE {aptFilter}
          AND r.Enabled  = 1
          AND r.Archived = 0
          AND r.Status NOT LIKE '%ancel%'
          AND {dateFilter}
        ORDER BY r.ApartmentNumber, r.CheckInDate
        """;

    await using var conn = new SqlConnection(connStr);
    await conn.OpenAsync();

    // ── Fetch reservations ────────────────────────────────────────────────────
    var rows = new List<(ReservationResponse Res, int? RegId)>();

    await using (var cmd = new SqlCommand(sql, conn))
    {
        cmd.Parameters.Add("@start", SqlDbType.Date).Value = start;
        cmd.Parameters.Add("@end",   SqlDbType.Date).Value = end;
        if (aptFilter.Contains("= @apartment"))
            cmd.Parameters.AddWithValue("@apartment", int.Parse(apartment!));

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            int? regId = rdr.IsDBNull("RegistrationId") ? null : rdr.GetInt32("RegistrationId");

            var reg = regId.HasValue ? new RegistrationResponse(
                rdr.IsDBNull("Email")                  ? null  : rdr.GetString("Email"),
                rdr.IsDBNull("ArrivalMethod")          ? null  : rdr.GetString("ArrivalMethod"),
                rdr.IsDBNull("ArrivalTime")            ? null  : rdr.GetString("ArrivalTime"),
                rdr.IsDBNull("FlightNumber")           ? null  : rdr.GetString("FlightNumber"),
                !rdr.IsDBNull("EarlyCheckInRequested") && rdr.GetBoolean("EarlyCheckInRequested"),
                !rdr.IsDBNull("CribSetup")             && rdr.GetBoolean("CribSetup"),
                !rdr.IsDBNull("SofaSetup")             && rdr.GetBoolean("SofaSetup"),
                !rdr.IsDBNull("FoldableBed")           && rdr.GetBoolean("FoldableBed"),
                rdr.IsDBNull("OtherRequests")          ? null  : rdr.GetString("OtherRequests"),
                rdr.IsDBNull("ArrivalNotes")           ? null  : rdr.GetString("ArrivalNotes"),
                new List<GuestResponse>()
            ) : null;

            rows.Add((new ReservationResponse(
                rdr.GetInt32("ApartmentNumber"),
                rdr.GetString("ReservationName"),
                rdr.IsDBNull("ConfirmationCode") ? null : rdr.GetString("ConfirmationCode"),
                rdr.IsDBNull("Status")           ? null : rdr.GetString("Status"),
                DateOnly.FromDateTime(rdr.GetDateTime("CheckInDate")),
                DateOnly.FromDateTime(rdr.GetDateTime("CheckOutDate")),
                rdr.GetInt32("Nights"),
                rdr.GetInt32("Adults"),
                rdr.GetInt32("Children"),
                rdr.GetInt32("Infants"),
                rdr.GetInt32("GuestCount"),
                rdr.IsDBNull("PhoneNumber") ? null : rdr.GetString("PhoneNumber"),
                reg
            ), regId));
        }
    }

    // ── Fetch guests in one batch ─────────────────────────────────────────────
    var regIds = rows.Where(x => x.RegId.HasValue).Select(x => x.RegId!.Value).Distinct().ToList();
    if (regIds.Count > 0)
    {
        var regToReg = rows
            .Where(x => x.RegId.HasValue)
            .ToDictionary(x => x.RegId!.Value, x => x.Res.Registration);

        var guestSql = $"""
            SELECT RegistrationId, Name, Nationality, BirthDate
            FROM Guest
            WHERE RegistrationId IN ({string.Join(",", regIds)})
            """;

        await using var gCmd = new SqlCommand(guestSql, conn);
        await using var gRdr = await gCmd.ExecuteReaderAsync();
        while (await gRdr.ReadAsync())
        {
            var regId = gRdr.GetInt32("RegistrationId");
            var guest = new GuestResponse(
                gRdr.IsDBNull("Name")        ? null : gRdr.GetString("Name"),
                gRdr.IsDBNull("Nationality") ? null : gRdr.GetString("Nationality"),
                CalcAge(gRdr.IsDBNull("BirthDate") ? null : gRdr.GetDateTime("BirthDate"))
            );
            if (regToReg.TryGetValue(regId, out var regResp))
                regResp?.Guests.Add(guest);
        }
    }

    return rows.Select(x => x.Res).ToList();
}

// ── Response types ────────────────────────────────────────────────────────────
record GuestResponse(string? Name, string? Nationality, int? Age);

record AdminLoginRequest(string Username, string Password);
record AdminCreateUserRequest(string Username, string Password);
record AdminUpdatePasswordRequest(string Password);
record AdminConfigSetRequest(string Value);
record AdminSetRoleRequest(string Role);
record AdminSetLanguageRequest(string Language);
record ReminderCreateRequest(string Message, DateTime ScheduledAt, long ChannelId, string BotId, string Language);
record MaintenanceIntervalRequest(int IntervalWeeks);

record RegistrationResponse(
    string? Email,
    string? ArrivalMethod,
    string? ArrivalTime,
    string? FlightNumber,
    bool    EarlyCheckIn,
    bool    CribSetup,
    bool    SofaSetup,
    bool    FoldableBed,
    string? OtherRequests,
    string? ArrivalNotes,
    List<GuestResponse> Guests);

record ReservationResponse(
    int      ApartmentNumber,
    string   ReservationName,
    string?  ConfirmationCode,
    string?  Status,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int      Nights,
    int      Adults,
    int      Children,
    int      Infants,
    int      GuestCount,
    string?  PhoneNumber,
    RegistrationResponse? Registration);
