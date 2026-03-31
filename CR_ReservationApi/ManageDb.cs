using System.Data;
using Microsoft.Data.SqlClient;

static class ManageDb
{
    static string _cs = "";
    public static void Init(string cs) => _cs = cs;

    // ── Reader helpers ─────────────────────────────────────────────────────────
    static string?  S(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? null : v.ToString(); }
    static bool     B(SqlDataReader r, string c) { var v = r[c]; return v != DBNull.Value && (bool)v; }
    static int      I(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? 0 : Convert.ToInt32(v); }
    static decimal  M(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? 0m : (decimal)v; }
    static Guid     G(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? Guid.Empty : (Guid)v; }
    static DateOnly D(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? default : DateOnly.FromDateTime(Convert.ToDateTime(v)); }
    static DateTime? DT(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? null : Convert.ToDateTime(v); }

    static SqlParameter DateParam(string name, DateOnly d) =>
        new(name, SqlDbType.Date) { Value = d.ToDateTime(TimeOnly.MinValue) };

    // ── List ──────────────────────────────────────────────────────────────────
    public static async Task<List<ManageReservationRow>> ListAsync(
        int apt, DateOnly from, DateOnly to, string status)
    {
        var rows = new List<ManageReservationRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT Id, ApartmentNumber, ReservationName, ConfirmationCode,
                   CheckInDate, CheckOutDate, Nights, Adults, Children, Infants,
                   Status, Enabled, Archived, MessagesUrl
            FROM   Reservation
            WHERE  (@apt = 0 OR ApartmentNumber = @apt)
              AND  Enabled = 1
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
                S(rd,"Status"), B(rd,"Enabled"), B(rd,"Archived"), S(rd,"MessagesUrl")));
        return rows;
    }

    // ── Get single ────────────────────────────────────────────────────────────
    public static async Task<ManageReservationDetail?> GetAsync(int id)
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

    // ── Create ────────────────────────────────────────────────────────────────
    public static async Task<int> CreateAsync(ReservationCreateRequest req)
    {
        var nights = req.CheckOutDate.DayNumber - req.CheckInDate.DayNumber;
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
        cmd.Parameters.AddWithValue("@apt",      req.ApartmentNumber);
        cmd.Parameters.AddWithValue("@code",     req.ConfirmationCode ?? "");
        cmd.Parameters.AddWithValue("@status",   string.IsNullOrEmpty(req.Status) ? "confirmed" : req.Status);
        cmd.Parameters.AddWithValue("@nights",   nights);
        cmd.Parameters.AddWithValue("@name",     req.ReservationName);
        cmd.Parameters.Add(DateParam("@checkin",  req.CheckInDate));
        cmd.Parameters.Add(DateParam("@checkout", req.CheckOutDate));
        cmd.Parameters.AddWithValue("@adults",   req.Adults);
        cmd.Parameters.AddWithValue("@children", req.Children);
        cmd.Parameters.AddWithValue("@infants",  req.Infants);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    // ── Update ────────────────────────────────────────────────────────────────
    public static async Task UpdateAsync(int id, ReservationUpdateRequest req)
    {
        var nights = req.CheckOutDate.DayNumber - req.CheckInDate.DayNumber;
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            UPDATE Reservation SET
                ModifiedAt       = GETUTCDATE(),
                ApartmentNumber  = @apt,
                ReservationName  = @name,
                ConfirmationCode = @code,
                Status           = @status,
                CheckInDate      = @checkin,
                CheckOutDate     = @checkout,
                Nights           = @nights,
                Adults           = @adults,
                Children         = @children,
                Infants          = @infants,
                PhoneNumber      = @phone,
                LivesIn          = @livesIn,
                Payout           = @payout,
                NightlyRate      = @rate,
                CleaningFee      = @cleaning,
                Enabled          = @enabled,
                Archived         = @archived,
                Private          = @private
            WHERE Id = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id",       id);
        cmd.Parameters.AddWithValue("@apt",      req.ApartmentNumber);
        cmd.Parameters.AddWithValue("@name",     req.ReservationName);
        cmd.Parameters.AddWithValue("@code",     req.ConfirmationCode ?? "");
        cmd.Parameters.AddWithValue("@status",   req.Status ?? "");
        cmd.Parameters.Add(DateParam("@checkin",  req.CheckInDate));
        cmd.Parameters.Add(DateParam("@checkout", req.CheckOutDate));
        cmd.Parameters.AddWithValue("@nights",   nights);
        cmd.Parameters.AddWithValue("@adults",   req.Adults);
        cmd.Parameters.AddWithValue("@children", req.Children);
        cmd.Parameters.AddWithValue("@infants",  req.Infants);
        cmd.Parameters.AddWithValue("@phone",    req.PhoneNumber ?? "");
        cmd.Parameters.AddWithValue("@livesIn",  req.LivesIn ?? "");
        cmd.Parameters.AddWithValue("@payout",   req.Payout);
        cmd.Parameters.AddWithValue("@rate",     req.NightlyRate);
        cmd.Parameters.AddWithValue("@cleaning", req.CleaningFee);
        cmd.Parameters.AddWithValue("@enabled",  req.Enabled  ? 1 : 0);
        cmd.Parameters.AddWithValue("@archived", req.Archived ? 1 : 0);
        cmd.Parameters.AddWithValue("@private",  req.Private  ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Get registration ──────────────────────────────────────────────────────
    public static async Task<ManageRegistrationDetail?> GetRegistrationAsync(Guid regGuid)
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
    public static async Task CreateRegistrationAsync(ManageReservationDetail res)
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
    public static async Task UpdateRegistrationAsync(int regId, RegistrationWriteRequest req)
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
        cmd.Parameters.AddWithValue("@id",       regId);
        cmd.Parameters.AddWithValue("@email",    req.Email    ?? "");
        cmd.Parameters.AddWithValue("@method",   req.ArrivalMethod ?? "");
        cmd.Parameters.AddWithValue("@time",     req.ArrivalTime   ?? "");
        cmd.Parameters.AddWithValue("@flight",   req.FlightNumber  ?? "");
        cmd.Parameters.AddWithValue("@notes",    req.ArrivalNotes  ?? "");
        cmd.Parameters.AddWithValue("@earlyCI",  req.EarlyCheckIn ? 1 : 0);
        cmd.Parameters.AddWithValue("@crib",     req.Crib     ? 1 : 0);
        cmd.Parameters.AddWithValue("@sofa",     req.Sofa     ? 1 : 0);
        cmd.Parameters.AddWithValue("@foldable", req.Foldable ? 1 : 0);
        cmd.Parameters.AddWithValue("@other",    req.OtherRequests ?? "");
        cmd.Parameters.AddWithValue("@nif",      req.InvoiceNif    ?? "");
        cmd.Parameters.AddWithValue("@invName",  req.InvoiceName   ?? "");
        cmd.Parameters.AddWithValue("@invAddr",  req.InvoiceAddr   ?? "");
        cmd.Parameters.AddWithValue("@invEmail", req.InvoiceEmail  ?? "");
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Guests ────────────────────────────────────────────────────────────────
    public static async Task<List<ManageGuestRow>> GetGuestsAsync(int regId)
    {
        var list = new List<ManageGuestRow>();
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

    public static async Task AddGuestAsync(int regId, GuestWriteRequest req)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            INSERT INTO Guest (RegistrationId, Name, Nationality, BirthDate)
            VALUES (@regId, @name, @nat, @dob)
            """, conn);
        cmd.Parameters.AddWithValue("@regId", regId);
        cmd.Parameters.AddWithValue("@name",  req.Name        ?? "");
        cmd.Parameters.AddWithValue("@nat",   req.Nationality ?? "");
        if (DateTime.TryParse(req.BirthDate, out var dob))
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

    // ── Cal notes ─────────────────────────────────────────────────────────────
    public static async Task EnsureAsync()
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ReservationCalNote')
            CREATE TABLE ReservationCalNote (
                ReservationId INT          NOT NULL PRIMARY KEY,
                CheckInTime   NVARCHAR(50) NULL,
                CheckOutTime  NVARCHAR(50) NULL,
                Crib          BIT          NOT NULL DEFAULT 0,
                EarlyCheckIn  BIT          NOT NULL DEFAULT 0,
                SofaBed       BIT          NOT NULL DEFAULT 0,
                LeavingBags   BIT          NOT NULL DEFAULT 0)
            """, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<CalNote?> GetCalNoteAsync(int reservationId)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT ReservationId, CheckInTime, CheckOutTime, Crib, EarlyCheckIn, SofaBed, LeavingBags
            FROM ReservationCalNote WHERE ReservationId = @id
            """, conn);
        cmd.Parameters.AddWithValue("@id", reservationId);
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return new(I(rd,"ReservationId"), S(rd,"CheckInTime"), S(rd,"CheckOutTime"),
            B(rd,"Crib"), B(rd,"EarlyCheckIn"), B(rd,"SofaBed"), B(rd,"LeavingBags"));
    }

    public static async Task UpsertCalNoteAsync(int reservationId, CalNoteRequest req)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            MERGE ReservationCalNote AS t
            USING (SELECT @id AS ReservationId) AS s ON t.ReservationId = s.ReservationId
            WHEN MATCHED THEN
                UPDATE SET CheckInTime=@ci, CheckOutTime=@co, Crib=@crib,
                           EarlyCheckIn=@earlyCI, SofaBed=@sofa, LeavingBags=@bags
            WHEN NOT MATCHED THEN
                INSERT (ReservationId, CheckInTime, CheckOutTime, Crib, EarlyCheckIn, SofaBed, LeavingBags)
                VALUES (@id, @ci, @co, @crib, @earlyCI, @sofa, @bags);
            """, conn);
        cmd.Parameters.AddWithValue("@id",      reservationId);
        cmd.Parameters.AddWithValue("@ci",      (object?)req.CheckInTime  ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@co",      (object?)req.CheckOutTime ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@crib",    req.Crib        ? 1 : 0);
        cmd.Parameters.AddWithValue("@earlyCI", req.EarlyCheckIn? 1 : 0);
        cmd.Parameters.AddWithValue("@sofa",    req.SofaBed     ? 1 : 0);
        cmd.Parameters.AddWithValue("@bags",    req.LeavingBags ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Stats ─────────────────────────────────────────────────────────────────
    public static async Task<List<MonthlyStatsRow>> GetStatsAsync(int year)
    {
        var rows = new List<MonthlyStatsRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("""
            SELECT FORMAT(CheckInDate, 'yyyy-MM') AS Month,
                   ApartmentNumber,
                   SUM(Nights) AS Nights,
                   SUM(Payout) AS Payout
            FROM   Reservation
            WHERE  YEAR(CheckInDate) = @year
              AND  Enabled  = 1
              AND  Archived = 0
              AND  (Status IS NULL OR Status NOT LIKE '%ancel%')
            GROUP  BY FORMAT(CheckInDate, 'yyyy-MM'), ApartmentNumber
            ORDER  BY Month, ApartmentNumber
            """, conn);
        cmd.Parameters.AddWithValue("@year", year);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            rows.Add(new((string)rd["Month"], (int)rd["ApartmentNumber"],
                Convert.ToInt32(rd["Nights"]), Convert.ToDecimal(rd["Payout"])));
        return rows;
    }
}
