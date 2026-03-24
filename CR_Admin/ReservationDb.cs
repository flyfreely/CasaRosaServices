using System.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;

static class ReservationDb
{
    static string _cs = "";
    public static void Init(string cs) => _cs = cs;

    // ── Reader helpers ────────────────────────────────────────────────────────
    static string? S(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? null : v.ToString(); }
    static bool    B(SqlDataReader r, string c) { var v = r[c]; return v != DBNull.Value && (bool)v; }
    static int     I(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? 0 : Convert.ToInt32(v); }
    static decimal M(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? 0m : (decimal)v; }
    static Guid    G(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? Guid.Empty : (Guid)v; }
    static DateOnly  D(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? default : DateOnly.FromDateTime(Convert.ToDateTime(v)); }
    static DateTime? DT(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? null : Convert.ToDateTime(v); }

    static SqlParameter DateParam(string name, DateOnly d) =>
        new(name, SqlDbType.Date) { Value = d.ToDateTime(TimeOnly.MinValue) };

    static SqlParameter DateParamN(string name, DateOnly? d) =>
        new(name, SqlDbType.Date) { Value = d.HasValue ? (object)d.Value.ToDateTime(TimeOnly.MinValue) : DBNull.Value };

    // ── List reservations ─────────────────────────────────────────────────────
    public static async Task<List<ReservationRow>> ListAsync(
        int apt, DateOnly from, DateOnly to, string status)
    {
        var rows = new List<ReservationRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT Id, ApartmentNumber, ReservationName, ConfirmationCode,
                   CheckInDate, CheckOutDate, Nights, Adults, Children, Infants,
                   Status, Enabled, Archived
            FROM   Reservation
            WHERE  (@apt = 0 OR ApartmentNumber = @apt)
              AND  (CheckInDate BETWEEN @from AND @to OR CheckOutDate BETWEEN @from AND @to)
              AND  (@status = 'all'
                    OR (@status = 'active'    AND Enabled=1 AND Archived=0
                                             AND (Status IS NULL OR Status NOT LIKE '%ancel%'))
                    OR (@status = 'cancelled' AND Status LIKE '%ancel%')
                    OR (@status = 'archived'  AND Archived = 1))
            ORDER  BY CheckInDate ASC
            """, conn);
        cmd.Parameters.AddWithValue("@apt",    apt);
        cmd.Parameters.Add(DateParam("@from", from));
        cmd.Parameters.Add(DateParam("@to",   to));
        cmd.Parameters.AddWithValue("@status", status);

        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            rows.Add(new(I(rd,"Id"), I(rd,"ApartmentNumber"), S(rd,"ReservationName") ?? "",
                S(rd,"ConfirmationCode"), D(rd,"CheckInDate"), D(rd,"CheckOutDate"),
                I(rd,"Nights"), I(rd,"Adults"), I(rd,"Children"), I(rd,"Infants"),
                S(rd,"Status"), B(rd,"Enabled"), B(rd,"Archived")));
        return rows;
    }

    // ── Get single reservation ────────────────────────────────────────────────
    public static async Task<ReservationDetail?> GetAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT Id, ApartmentNumber, ReservationName, ConfirmationCode,
                   CheckInDate, CheckOutDate, Nights, Adults, Children, Infants,
                   Status, Enabled, Archived, Private, PhoneNumber, LivesIn,
                   NightlyRate, Payout, GuestPaid, CleaningFee, RegistrationGuid, MessagesUrl
            FROM   Reservation WHERE Id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id", id);
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new(
            I(rd,"Id"), I(rd,"ApartmentNumber"), S(rd,"ReservationName") ?? "",
            S(rd,"ConfirmationCode"), D(rd,"CheckInDate"), D(rd,"CheckOutDate"),
            I(rd,"Nights"), I(rd,"Adults"), I(rd,"Children"), I(rd,"Infants"),
            S(rd,"Status"), B(rd,"Enabled"), B(rd,"Archived"), B(rd,"Private"),
            S(rd,"PhoneNumber"), S(rd,"LivesIn"),
            M(rd,"NightlyRate"), M(rd,"Payout"), M(rd,"GuestPaid"), M(rd,"CleaningFee"),
            G(rd,"RegistrationGuid"), S(rd,"MessagesUrl"));
    }

    // ── Create reservation ────────────────────────────────────────────────────
    public static async Task<int> CreateAsync(IFormCollection f)
    {
        var checkin  = DateOnly.Parse(f["checkin"].ToString());
        var checkout = DateOnly.Parse(f["checkout"].ToString());
        var nights   = checkout.DayNumber - checkin.DayNumber;

        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            INSERT INTO Reservation
                (Enabled, CreatedAt, ModifiedAt, Guid, ApartmentNumber, ConfirmationCode,
                 Status, Nights, NightlyRate, Payout, GuestPaid, GuestServiceFee,
                 GuestPropertyUseTax, HostRoomFee, HostNightlyRateAdjustment,
                 HostPropertyUseTax, HostServiceFee, HostPaid, BookedOn,
                 ReservationName, CheckInDate, CheckOutDate, GuestCount,
                 Archived, Private, RegistrationGuid, CleaningFee, Adults, Children, Infants)
            VALUES
                (1, GETUTCDATE(), GETUTCDATE(), NEWID(), @apt, @code,
                 @status, @nights, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, GETUTCDATE(),
                 @name, @checkin, @checkout, 0, 0, 0, NEWID(), 0,
                 @adults, @children, @infants);
            SELECT SCOPE_IDENTITY();
            """, conn);
        cmd.Parameters.AddWithValue("@apt",      int.Parse(f["apartment"].ToString()));
        cmd.Parameters.AddWithValue("@code",     f["code"].ToString());
        cmd.Parameters.AddWithValue("@status",   string.IsNullOrEmpty(f["status"]) ? "confirmed" : f["status"].ToString());
        cmd.Parameters.AddWithValue("@nights",   nights);
        cmd.Parameters.AddWithValue("@name",     f["name"].ToString());
        cmd.Parameters.Add(DateParam("@checkin",  checkin));
        cmd.Parameters.Add(DateParam("@checkout", checkout));
        cmd.Parameters.AddWithValue("@adults",   int.TryParse(f["adults"],   out var a) ? a : 0);
        cmd.Parameters.AddWithValue("@children", int.TryParse(f["children"], out var c) ? c : 0);
        cmd.Parameters.AddWithValue("@infants",  int.TryParse(f["infants"],  out var i) ? i : 0);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── Update reservation ────────────────────────────────────────────────────
    public static async Task UpdateAsync(int id, IFormCollection f)
    {
        var checkin  = DateOnly.Parse(f["checkin"].ToString());
        var checkout = DateOnly.Parse(f["checkout"].ToString());
        var nights   = checkout.DayNumber - checkin.DayNumber;

        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            UPDATE Reservation SET
                ModifiedAt      = GETUTCDATE(),
                ApartmentNumber = @apt,
                ReservationName = @name,
                ConfirmationCode = @code,
                Status          = @status,
                CheckInDate     = @checkin,
                CheckOutDate    = @checkout,
                Nights          = @nights,
                Adults          = @adults,
                Children        = @children,
                Infants         = @infants,
                PhoneNumber     = @phone,
                LivesIn         = @livesIn,
                NightlyRate     = @rate,
                CleaningFee     = @cleaning,
                Enabled         = @enabled,
                Archived        = @archived,
                Private         = @private
            WHERE Id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id",       id);
        cmd.Parameters.AddWithValue("@apt",      int.Parse(f["apartment"].ToString()));
        cmd.Parameters.AddWithValue("@name",     f["name"].ToString());
        cmd.Parameters.AddWithValue("@code",     f["code"].ToString());
        cmd.Parameters.AddWithValue("@status",   f["status"].ToString());
        cmd.Parameters.Add(DateParam("@checkin",  checkin));
        cmd.Parameters.Add(DateParam("@checkout", checkout));
        cmd.Parameters.AddWithValue("@nights",   nights);
        cmd.Parameters.AddWithValue("@adults",   int.TryParse(f["adults"],   out var a) ? a : 0);
        cmd.Parameters.AddWithValue("@children", int.TryParse(f["children"], out var c) ? c : 0);
        cmd.Parameters.AddWithValue("@infants",  int.TryParse(f["infants"],  out var i) ? i : 0);
        cmd.Parameters.AddWithValue("@phone",    f["phone"].ToString());
        cmd.Parameters.AddWithValue("@livesIn",  f["livesIn"].ToString());
        cmd.Parameters.AddWithValue("@rate",     decimal.TryParse(f["rate"],     out var r) ? r : 0m);
        cmd.Parameters.AddWithValue("@cleaning", decimal.TryParse(f["cleaning"], out var cl) ? cl : 0m);
        cmd.Parameters.AddWithValue("@enabled",  f["enabled"].ToString() == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@archived", f["archived"].ToString() == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@private",  f["private"].ToString() == "on" ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Get registration ──────────────────────────────────────────────────────
    public static async Task<RegistrationDetail?> GetRegistrationAsync(Guid regGuid)
    {
        if (regGuid == Guid.Empty) return null;
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT Id, guid, Email, ArrivalMethod, ArrivalTime, FlightNumber, ArrivalNotes,
                   EarlyCheckInRequested, CribSetup, SofaSetup, FoldableBed, OtherRequests,
                   InvoiceNif, InvoiceName, InvoiceAddress, InvoiceEmailAddress
            FROM   Registration WHERE guid = @guid
            """, conn);
        cmd.Parameters.AddWithValue("@guid", regGuid.ToString());
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new(
            I(rd,"Id"), S(rd,"guid") ?? "",
            S(rd,"Email"), S(rd,"ArrivalMethod"), S(rd,"ArrivalTime"),
            S(rd,"FlightNumber"), S(rd,"ArrivalNotes"),
            B(rd,"EarlyCheckInRequested"), B(rd,"CribSetup"), B(rd,"SofaSetup"),
            B(rd,"FoldableBed"), S(rd,"OtherRequests"),
            S(rd,"InvoiceNif"), S(rd,"InvoiceName"),
            S(rd,"InvoiceAddress"), S(rd,"InvoiceEmailAddress"));
    }

    // ── Create registration ───────────────────────────────────────────────────
    public static async Task CreateRegistrationAsync(ReservationDetail res)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            INSERT INTO Registration
                (FileId, CreatedAt, ApartmentNumber, ReservationName, Email,
                 CheckInDate, CheckOutDate, GuestCount, ArrivalMethod, ArrivalTime,
                 FlightNumber, ArrivalNotes, EarlyCheckInRequested,
                 CribSetup, SofaSetup, FoldableBed, OtherRequests, guid, Enabled)
            VALUES
                (0, GETUTCDATE(), @apt, @name, '',
                 @checkin, @checkout, 0, '', '',
                 '', '', 0, 0, 0, 0, '', @guid, 1)
            """, conn);
        cmd.Parameters.AddWithValue("@apt",     res.ApartmentNumber);
        cmd.Parameters.AddWithValue("@name",    res.ReservationName);
        cmd.Parameters.Add(DateParam("@checkin",  res.CheckInDate));
        cmd.Parameters.Add(DateParam("@checkout", res.CheckOutDate));
        cmd.Parameters.AddWithValue("@guid",    res.RegistrationGuid.ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Update registration ───────────────────────────────────────────────────
    public static async Task UpdateRegistrationAsync(IFormCollection f)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            UPDATE Registration SET
                Email               = @email,
                ArrivalMethod       = @method,
                ArrivalTime         = @time,
                FlightNumber        = @flight,
                ArrivalNotes        = @notes,
                EarlyCheckInRequested = @earlyCI,
                CribSetup           = @crib,
                SofaSetup           = @sofa,
                FoldableBed         = @foldable,
                OtherRequests       = @other,
                InvoiceNif          = @nif,
                InvoiceName         = @invName,
                InvoiceAddress      = @invAddr,
                InvoiceEmailAddress = @invEmail
            WHERE Id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id",       int.Parse(f["regId"].ToString()));
        cmd.Parameters.AddWithValue("@email",    f["email"].ToString());
        cmd.Parameters.AddWithValue("@method",   f["arrivalMethod"].ToString());
        cmd.Parameters.AddWithValue("@time",     f["arrivalTime"].ToString());
        cmd.Parameters.AddWithValue("@flight",   f["flight"].ToString());
        cmd.Parameters.AddWithValue("@notes",    f["arrivalNotes"].ToString());
        cmd.Parameters.AddWithValue("@earlyCI",  f["earlyCI"].ToString() == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@crib",     f["crib"].ToString()    == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@sofa",     f["sofa"].ToString()    == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@foldable", f["foldable"].ToString() == "on" ? 1 : 0);
        cmd.Parameters.AddWithValue("@other",    f["otherRequests"].ToString());
        cmd.Parameters.AddWithValue("@nif",      f["invoiceNif"].ToString());
        cmd.Parameters.AddWithValue("@invName",  f["invoiceName"].ToString());
        cmd.Parameters.AddWithValue("@invAddr",  f["invoiceAddr"].ToString());
        cmd.Parameters.AddWithValue("@invEmail", f["invoiceEmail"].ToString());
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Guests ────────────────────────────────────────────────────────────────
    public static async Task<List<GuestRow>> GetGuestsAsync(int regId)
    {
        var list = new List<GuestRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, RegistrationId, Name, Nationality, BirthDate FROM Guest WHERE RegistrationId = @id ORDER BY Id",
            conn);
        cmd.Parameters.AddWithValue("@id", regId);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new(I(rd,"Id"), I(rd,"RegistrationId"), S(rd,"Name"), S(rd,"Nationality"), DT(rd,"BirthDate")));
        return list;
    }

    public static async Task AddGuestAsync(int regId, IFormCollection f)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            INSERT INTO Guest (RegistrationId, Name, Nationality, BirthDate)
            VALUES (@regId, @name, @nat, @dob)
            """, conn);
        cmd.Parameters.AddWithValue("@regId", regId);
        cmd.Parameters.AddWithValue("@name",  f["guestName"].ToString());
        cmd.Parameters.AddWithValue("@nat",   f["guestNat"].ToString());
        var dobStr = f["guestDob"].ToString();
        if (DateTime.TryParse(dobStr, out var dob))
            cmd.Parameters.AddWithValue("@dob", dob);
        else
            cmd.Parameters.AddWithValue("@dob", DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DeleteGuestAsync(int guestId)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("DELETE FROM Guest WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", guestId);
        await cmd.ExecuteNonQueryAsync();
    }
}
