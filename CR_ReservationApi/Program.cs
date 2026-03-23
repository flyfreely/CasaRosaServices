using System.Data;
using Microsoft.Data.SqlClient;

var builder = WebApplication.CreateBuilder(args);
var app     = builder.Build();

var connStr   = builder.Configuration["Database:ConnectionString"]!;
var authToken = builder.Configuration["Http:AuthToken"]!;

// ── Auth ──────────────────────────────────────────────────────────────────────
app.Use(async (ctx, next) =>
{
    var token = ctx.Request.Query["Token"].FirstOrDefault()
             ?? ctx.Request.Query["token"].FirstOrDefault()
             ?? "";
    if (token != authToken) { ctx.Response.StatusCode = 404; return; }
    await next();
});

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapGet("/reservations", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "both"));

app.MapGet("/checkin", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "checkin"));

app.MapGet("/checkout", async (string? apartment, string? date) =>
    await HandleAsync(connStr, apartment, date, "checkout"));

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
