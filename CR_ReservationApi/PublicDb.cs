using System.Data;
using Microsoft.Data.SqlClient;

static class PublicDb
{
    static string _cs = "";
    public static void Init(string cs) => _cs = cs;

    static string? S(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? null : v.ToString(); }
    static bool    B(SqlDataReader r, string c) { var v = r[c]; return v != DBNull.Value && (bool)v; }
    static int     I(SqlDataReader r, string c) { var v = r[c]; return v == DBNull.Value ? 0 : Convert.ToInt32(v); }

    static SqlParameter DateParam(string name, DateOnly d) =>
        new(name, SqlDbType.Date) { Value = d.ToDateTime(TimeOnly.MinValue) };

    // ── Get reservation info for prefill ──────────────────────────────────────
    public static async Task<PublicReservationInfoResponse?> GetReservationInfoAsync(Guid guid)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        using var cmd = new SqlCommand("""
            SELECT r.ReservationName, r.ApartmentNumber,
                   r.CheckInDate, r.CheckOutDate,
                   COALESCE(r.Adults,0) + COALESCE(r.Children,0) + COALESCE(r.Infants,0) AS GuestCount,
                   reg.Id         AS RegId,
                   reg.Email,
                   reg.ArrivalMethod, reg.ArrivalTime, reg.FlightNumber, reg.ArrivalNotes,
                   reg.EarlyCheckInRequested, reg.CribSetup, reg.SofaSetup, reg.FoldableBed,
                   reg.OtherRequests, reg.InvoiceNif, reg.InvoiceName,
                   reg.InvoiceAddress, reg.InvoiceEmailAddress,
                   reg.SubmittedAt, reg.GuestCount AS RegGuestCount
            FROM Reservation r
            LEFT JOIN Registration reg
                ON  TRY_CAST(reg.guid AS uniqueidentifier) = r.RegistrationGuid
                AND reg.Enabled = 1
            WHERE r.RegistrationGuid = @guid
              AND r.Enabled  = 1
              AND r.Archived = 0
            """, conn);
        cmd.Parameters.AddWithValue("@guid", guid);

        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;

        var regId       = rd["RegId"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["RegId"]);
        var submitted   = rd["SubmittedAt"] != DBNull.Value;
        var resName     = S(rd, "ReservationName") ?? "";
        var aptNumber   = I(rd, "ApartmentNumber");
        var checkIn     = ((DateTime)rd["CheckInDate"]).ToString("yyyy-MM-dd");
        var checkOut    = ((DateTime)rd["CheckOutDate"]).ToString("yyyy-MM-dd");
        // Use registration GuestCount if available, otherwise sum from reservation
        var regGuestCount = rd["RegGuestCount"] == DBNull.Value ? 0 : Convert.ToInt32(rd["RegGuestCount"]);
        var guestCount  = regGuestCount > 0 ? regGuestCount : I(rd, "GuestCount");
        if (guestCount < 1) guestCount = 1;

        var email       = S(rd, "Email");
        var method      = S(rd, "ArrivalMethod");
        var time        = S(rd, "ArrivalTime");
        var flight      = S(rd, "FlightNumber");
        var notes       = S(rd, "ArrivalNotes");
        var earlyCI     = B(rd, "EarlyCheckInRequested");
        var crib        = B(rd, "CribSetup");
        var sofa        = B(rd, "SofaSetup");
        var foldable    = B(rd, "FoldableBed");
        var otherReqs   = S(rd, "OtherRequests");
        var nif         = S(rd, "InvoiceNif");
        var invName     = S(rd, "InvoiceName");
        var invAddr     = S(rd, "InvoiceAddress");
        var invEmail    = S(rd, "InvoiceEmailAddress");
        rd.Close();

        // Fetch guests if registration exists
        var guests = new List<PublicGuestInfo>();
        if (regId.HasValue)
        {
            using var gCmd = new SqlCommand("""
                SELECT Name, Nationality, BirthDate, Residency, CountryOfBirth, TypeOfId, IdNumber
                FROM Guest WHERE RegistrationId = @id ORDER BY Id
                """, conn);
            gCmd.Parameters.AddWithValue("@id", regId.Value);
            using var gRd = await gCmd.ExecuteReaderAsync();
            while (await gRd.ReadAsync())
            {
                var dob = gRd["BirthDate"] == DBNull.Value
                    ? null
                    : ((DateTime)gRd["BirthDate"]).ToString("yyyy-MM-dd");
                guests.Add(new(
                    S(gRd, "Name"), S(gRd, "Nationality"), dob,
                    S(gRd, "Residency"), S(gRd, "CountryOfBirth"),
                    S(gRd, "TypeOfId"), S(gRd, "IdNumber")));
            }
        }

        return new(submitted, resName, guestCount, aptNumber,
            checkIn, checkOut, email, method, time, flight, notes,
            earlyCI, crib, sofa, foldable, otherReqs,
            nif, invName, invAddr, invEmail, guests);
    }

    // ── Save public registration ───────────────────────────────────────────────
    public static async Task SaveRegistrationAsync(PublicRegisterRequest req)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            var checkin  = DateOnly.TryParse(req.CheckInDate,  out var ci) ? ci : DateOnly.FromDateTime(DateTime.Today);
            var checkout = DateOnly.TryParse(req.CheckOutDate, out var co) ? co : DateOnly.FromDateTime(DateTime.Today.AddDays(1));

            int regId = 0;
            bool linked = false;

            // Try to link to an existing registration (submitted or not)
            if (Guid.TryParse(req.ReservationGuid, out var resGuid))
            {
                using var findCmd = new SqlCommand("""
                    SELECT reg.Id FROM Registration reg
                    INNER JOIN Reservation r ON TRY_CAST(reg.guid AS uniqueidentifier) = r.RegistrationGuid
                    WHERE r.RegistrationGuid = @guid
                      AND reg.Enabled = 1
                    """, conn, tx);
                findCmd.Parameters.AddWithValue("@guid", resGuid);
                var result = await findCmd.ExecuteScalarAsync();
                if (result != null)
                {
                    regId  = Convert.ToInt32(result);
                    linked = true;
                }
            }

            if (linked)
            {
                // Update existing registration
                using var upd = new SqlCommand("""
                    UPDATE Registration SET
                        ReservationName     = @name,
                        GuestCount          = @count,
                        CheckInDate         = @checkin,
                        CheckOutDate        = @checkout,
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
                        InvoiceEmailAddress = @invEmail,
                        SubmittedAt         = GETUTCDATE()
                    WHERE Id = @id
                    """, conn, tx);
                upd.Parameters.AddWithValue("@id",      regId);
                upd.Parameters.AddWithValue("@name",    req.ReservationName ?? "");
                upd.Parameters.AddWithValue("@count",   req.GuestCount);
                upd.Parameters.Add(DateParam("@checkin",  checkin));
                upd.Parameters.Add(DateParam("@checkout", checkout));
                upd.Parameters.AddWithValue("@method",  req.ArrivalMethod   ?? "");
                upd.Parameters.AddWithValue("@time",    req.ArrivalTime     ?? "");
                upd.Parameters.AddWithValue("@flight",  req.FlightNumber    ?? "");
                upd.Parameters.AddWithValue("@notes",   req.ArrivalNotes    ?? "");
                upd.Parameters.AddWithValue("@earlyCI", req.EarlyCheckIn  ? 1 : 0);
                upd.Parameters.AddWithValue("@crib",    req.CribSetup     ? 1 : 0);
                upd.Parameters.AddWithValue("@sofa",    req.SofaSetup     ? 1 : 0);
                upd.Parameters.AddWithValue("@foldable",req.FoldableBed   ? 1 : 0);
                upd.Parameters.AddWithValue("@other",   req.OtherRequests   ?? "");
                upd.Parameters.AddWithValue("@nif",     req.InvoiceNif      ?? "");
                upd.Parameters.AddWithValue("@invName", req.InvoiceName     ?? "");
                upd.Parameters.AddWithValue("@invAddr", req.InvoiceAddress  ?? "");
                upd.Parameters.AddWithValue("@invEmail",req.InvoiceEmail    ?? "");
                await upd.ExecuteNonQueryAsync();

                // Delete old guests
                using var delG = new SqlCommand("DELETE FROM Guest WHERE RegistrationId = @id", conn, tx);
                delG.Parameters.AddWithValue("@id", regId);
                await delG.ExecuteNonQueryAsync();
            }
            else
            {
                // Insert registration using the reservation's own GUID so they always match.
                // For true orphans (no reservation GUID), generate a new one.
                var apt = req.ApartmentNumber ?? 0;
                var regGuidToUse = Guid.TryParse(req.ReservationGuid, out var parsedResGuid)
                    ? parsedResGuid.ToString()
                    : Guid.NewGuid().ToString();

                using var ins = new SqlCommand("""
                    INSERT INTO Registration
                        (FileId, CreatedAt, ApartmentNumber, ReservationName, GuestCount,
                         CheckInDate, CheckOutDate, ArrivalMethod, ArrivalTime, FlightNumber,
                         ArrivalNotes, EarlyCheckInRequested, CribSetup, SofaSetup, FoldableBed,
                         OtherRequests, InvoiceNif, InvoiceName, InvoiceAddress, InvoiceEmailAddress,
                         Email, guid, Enabled, SubmittedAt)
                    OUTPUT INSERTED.Id
                    VALUES
                        (0, GETUTCDATE(), @apt, @name, @count,
                         @checkin, @checkout, @method, @time, @flight,
                         @notes, @earlyCI, @crib, @sofa, @foldable,
                         @other, @nif, @invName, @invAddr, @invEmail,
                         '', @guid, 1, GETUTCDATE())
                    """, conn, tx);
                ins.Parameters.AddWithValue("@apt",     apt);
                ins.Parameters.AddWithValue("@name",    req.ReservationName ?? "");
                ins.Parameters.AddWithValue("@count",   req.GuestCount);
                ins.Parameters.Add(DateParam("@checkin",  checkin));
                ins.Parameters.Add(DateParam("@checkout", checkout));
                ins.Parameters.AddWithValue("@method",  req.ArrivalMethod   ?? "");
                ins.Parameters.AddWithValue("@time",    req.ArrivalTime     ?? "");
                ins.Parameters.AddWithValue("@flight",  req.FlightNumber    ?? "");
                ins.Parameters.AddWithValue("@notes",   req.ArrivalNotes    ?? "");
                ins.Parameters.AddWithValue("@earlyCI", req.EarlyCheckIn  ? 1 : 0);
                ins.Parameters.AddWithValue("@crib",    req.CribSetup     ? 1 : 0);
                ins.Parameters.AddWithValue("@sofa",    req.SofaSetup     ? 1 : 0);
                ins.Parameters.AddWithValue("@foldable",req.FoldableBed   ? 1 : 0);
                ins.Parameters.AddWithValue("@other",   req.OtherRequests   ?? "");
                ins.Parameters.AddWithValue("@nif",     req.InvoiceNif      ?? "");
                ins.Parameters.AddWithValue("@invName", req.InvoiceName     ?? "");
                ins.Parameters.AddWithValue("@invAddr", req.InvoiceAddress  ?? "");
                ins.Parameters.AddWithValue("@invEmail",req.InvoiceEmail    ?? "");
                ins.Parameters.AddWithValue("@guid",    regGuidToUse);
                regId = Convert.ToInt32(await ins.ExecuteScalarAsync());
            }

            // Insert guests
            foreach (var g in req.Guests)
            {
                using var gIns = new SqlCommand("""
                    INSERT INTO Guest
                        (RegistrationId, Name, Nationality, BirthDate, Residency, CountryOfBirth, TypeOfId, IdNumber)
                    VALUES (@regId, @name, @nat, @dob, @res, @cob, @type, @idNum)
                    """, conn, tx);
                gIns.Parameters.AddWithValue("@regId", regId);
                gIns.Parameters.AddWithValue("@name",  g.Name         ?? "");
                gIns.Parameters.AddWithValue("@nat",   g.Nationality  ?? "");
                if (DateTime.TryParse(g.BirthDate, out var dob))
                    gIns.Parameters.AddWithValue("@dob", dob);
                else
                    gIns.Parameters.AddWithValue("@dob", DBNull.Value);
                gIns.Parameters.AddWithValue("@res",   g.Residency      ?? "");
                gIns.Parameters.AddWithValue("@cob",   g.CountryOfBirth ?? "");
                gIns.Parameters.AddWithValue("@type",  g.TypeOfId       ?? "");
                gIns.Parameters.AddWithValue("@idNum", g.IdNumber       ?? "");
                await gIns.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}
