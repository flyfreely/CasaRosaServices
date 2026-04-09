using System.Security.Cryptography;
using Microsoft.Data.SqlClient;

static class AdminDb
{
    static string _cs = "";
    public static void Init(string cs) => _cs = cs;

    // ── Ensure tables exist ───────────────────────────────────────────────────
    public static async Task EnsureAsync()
    {
        for (int i = 0; i < 5; i++)
        {
            try   { await EnsureInnerAsync(); return; }
            catch (Exception ex) when (i < 4)
            {
                Console.WriteLine($"[AdminDb] {ex.Message} – retrying in 5s…");
                await Task.Delay(5_000);
            }
        }
    }

    static async Task EnsureInnerAsync()
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();

        await Exec(conn, """
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

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Users' AND COLUMN_NAME = 'IsAdmin')
            ALTER TABLE dbo.Admin_Users ADD IsAdmin BIT NOT NULL DEFAULT 1
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Users' AND COLUMN_NAME = 'Role')
            BEGIN
                ALTER TABLE dbo.Admin_Users ADD Role NVARCHAR(20) NOT NULL DEFAULT 'Admin';
                EXEC sp_executesql N'UPDATE dbo.Admin_Users SET Role = N''Viewer'' WHERE IsAdmin = 0';
            END
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Users' AND COLUMN_NAME = 'Language')
            ALTER TABLE dbo.Admin_Users ADD Language NVARCHAR(10) NOT NULL DEFAULT 'en'
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Users' AND COLUMN_NAME = 'GoogleEmail')
            ALTER TABLE dbo.Admin_Users ADD GoogleEmail NVARCHAR(200) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_Config')
            CREATE TABLE dbo.Admin_Config (
                [Key]     NVARCHAR(100) NOT NULL PRIMARY KEY,
                [Value]   NVARCHAR(500) NOT NULL,
                UpdatedAt DATETIME2     NOT NULL DEFAULT GETUTCDATE()
            )
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_AuditLog')
            CREATE TABLE dbo.Admin_AuditLog (
                Id     INT IDENTITY PRIMARY KEY,
                At     DATETIME2     NOT NULL DEFAULT GETUTCDATE(),
                Actor  NVARCHAR(100) NOT NULL,
                Action NVARCHAR(100) NOT NULL,
                Detail NVARCHAR(500) NULL
            )
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Reminders')
            CREATE TABLE dbo.Reminders (
                Id          INT IDENTITY PRIMARY KEY,
                Message     NVARCHAR(1000) NOT NULL,
                ScheduledAt DATETIME2      NOT NULL,
                ChannelId   BIGINT         NOT NULL,
                BotId       NVARCHAR(50)   NOT NULL DEFAULT 'Auto_Bot',
                Language    NVARCHAR(50)   NOT NULL DEFAULT 'Russian',
                CreatedAt   DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
                SentAt      DATETIME2      NULL,
                Cancelled   BIT            NOT NULL DEFAULT 0
            )
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'ApartmentCleaning')
            CREATE TABLE dbo.ApartmentCleaning (
                Date            DATE    NOT NULL,
                ApartmentNumber INT     NOT NULL,
                State           TINYINT NOT NULL DEFAULT 1,
                PRIMARY KEY (Date, ApartmentNumber)
            )
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Telegram_Log')
            CREATE TABLE dbo.Telegram_Log (
                Id          INT IDENTITY PRIMARY KEY,
                At          DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
                EventType   NVARCHAR(20)   NOT NULL,
                MessageType NVARCHAR(100)  NOT NULL,
                Channel     NVARCHAR(100)  NULL,
                Summary     NVARCHAR(1000) NULL,
                IsError     BIT            NOT NULL DEFAULT 0
            )
            """);

        var userCount = (int)(await Scalar(conn, "SELECT COUNT(*) FROM dbo.Admin_Users"))!;
        if (userCount == 0)
        {
            await Exec(conn,
                "INSERT INTO dbo.Admin_Users (Username, PasswordHash) VALUES (@u, @h)",
                ("@u", "user"), ("@h", HashPassword("password")));
            Console.WriteLine("[AdminDb] Default user created: user / password");
        }

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Registration' AND COLUMN_NAME = 'Sef')
            ALTER TABLE dbo.Registration ADD Sef NVARCHAR(100) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Guest' AND COLUMN_NAME = 'Residency')
            ALTER TABLE dbo.Guest ADD Residency NVARCHAR(100) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Guest' AND COLUMN_NAME = 'CountryOfBirth')
            ALTER TABLE dbo.Guest ADD CountryOfBirth NVARCHAR(100) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Guest' AND COLUMN_NAME = 'TypeOfId')
            ALTER TABLE dbo.Guest ADD TypeOfId NVARCHAR(50) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Guest' AND COLUMN_NAME = 'IdNumber')
            ALTER TABLE dbo.Guest ADD IdNumber NVARCHAR(100) NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Registration' AND COLUMN_NAME = 'SubmittedAt')
            ALTER TABLE dbo.Registration ADD SubmittedAt DATETIME NULL
            """);

        await Exec(conn, """
            IF NOT EXISTS (
                SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'Admin_MaintenanceTasks')
            CREATE TABLE dbo.Admin_MaintenanceTasks (
                Id                     INT IDENTITY PRIMARY KEY,
                ApartmentNumber        INT          NOT NULL,
                TaskKey                NVARCHAR(50) NOT NULL,
                IntervalWeeks          INT          NOT NULL DEFAULT 4,
                LastDoneAt             DATETIME2    NULL,
                LastReminderCreatedAt  DATETIME2    NULL,
                CONSTRAINT UQ_Maint_Apt_Task UNIQUE (ApartmentNumber, TaskKey)
            )
            """);

        // Seed the 9 default maintenance tasks
        string[] taskKeys = { "door_battery", "ac_filters", "ventilator_filters" };
        foreach (var apt in new[] { 1, 2, 3 })
            foreach (var tk in taskKeys)
            {
                var exists = (int)(await Scalar(conn,
                    "SELECT COUNT(*) FROM dbo.Admin_MaintenanceTasks WHERE ApartmentNumber=@a AND TaskKey=@t",
                    ("@a", (object)apt), ("@t", (object)tk)))!;
                if (exists == 0)
                    await Exec(conn,
                        "INSERT INTO dbo.Admin_MaintenanceTasks (ApartmentNumber, TaskKey) VALUES (@a, @t)",
                        ("@a", (object)apt), ("@t", (object)tk));
            }

        await SeedConfig(conn, "briefing_time",               "18:00");
        await SeedConfig(conn, "briefing_channel",             "-5129864639");
        await SeedConfig(conn, "today_briefing_time",         "09:00");
        await SeedConfig(conn, "today_briefing_channel",      "-5129864639");
        await SeedConfig(conn, "triple_cleaning_time",        "08:00");
        await SeedConfig(conn, "triple_cleaning_channel",     "-5186091931");
        await SeedConfig(conn, "crib_alert_time",             "08:00");
        await SeedConfig(conn, "crib_alert_channel",          "-5186091931");
        Console.WriteLine("[AdminDb] Tables ready.");
    }

    static async Task SeedConfig(SqlConnection conn, string key, string defaultValue)
    {
        var count = (int)(await Scalar(conn,
            "SELECT COUNT(*) FROM dbo.Admin_Config WHERE [Key] = @k",
            ("@k", key)))!;
        if (count == 0)
            await Exec(conn,
                "INSERT INTO dbo.Admin_Config ([Key], [Value]) VALUES (@k, @v)",
                ("@k", key), ("@v", defaultValue));
    }

    // ── Users ─────────────────────────────────────────────────────────────────
    public static async Task<(int Id, string Hash, string Role, string Language)?> GetUserLoginAsync(string username)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, PasswordHash, Role, Language FROM dbo.Admin_Users WHERE Username = @u", conn);
        cmd.Parameters.AddWithValue("@u", username);
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return ((int)rd["Id"], (string)rd["PasswordHash"], (string)rd["Role"], (string)rd["Language"]);
    }

    public static async Task<List<AdminUser>> ListUsersAsync()
    {
        var list = new List<AdminUser>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, Username, Role, CreatedAt, Language, GoogleEmail FROM dbo.Admin_Users ORDER BY CreatedAt", conn);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new((int)rd["Id"], (string)rd["Username"], (string)rd["Role"], (DateTime)rd["CreatedAt"],
                (string)rd["Language"], rd["GoogleEmail"] == DBNull.Value ? null : (string)rd["GoogleEmail"]));
        return list;
    }

    public static async Task SetRoleAsync(int id, string role)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_Users SET Role = @r, IsAdmin = @a WHERE Id = @id",
            ("@r", (object)role), ("@a", (object)(role == "Admin" ? 1 : 0)), ("@id", (object)id));
    }

    public static async Task<int> AdminCountAsync()
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        return (int)(await Scalar(conn, "SELECT COUNT(*) FROM dbo.Admin_Users WHERE Role = 'Admin'"))!;
    }

    public static async Task CreateUserAsync(string username, string password)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.Admin_Users (Username, PasswordHash) VALUES (@u, @h)", conn);
        cmd.Parameters.AddWithValue("@u", username);
        cmd.Parameters.AddWithValue("@h", HashPassword(password));
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task UpdatePasswordAsync(int id, string password)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "UPDATE dbo.Admin_Users SET PasswordHash = @h WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@h",  HashPassword(password));
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<(int Id, string Username, string Role, string Language)?> GetUserByGoogleEmailAsync(string email)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, Username, Role, Language FROM dbo.Admin_Users WHERE GoogleEmail = @e", conn);
        cmd.Parameters.AddWithValue("@e", email);
        using var rd = await cmd.ExecuteReaderAsync();
        if (!await rd.ReadAsync()) return null;
        return ((int)rd["Id"], (string)rd["Username"], (string)rd["Role"], (string)rd["Language"]);
    }

    public static async Task SetGoogleEmailAsync(int id, string? email)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_Users SET GoogleEmail = @e WHERE Id = @id",
            ("@e", email is null ? (object)DBNull.Value : (object)email), ("@id", (object)id));
    }

    public static async Task UpdateUserLanguageAsync(int id, string language)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_Users SET Language = @l WHERE Id = @id",
            ("@l", (object)language), ("@id", (object)id));
    }

    public static async Task DeleteUserAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand("DELETE FROM dbo.Admin_Users WHERE Id = @id", conn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Config ────────────────────────────────────────────────────────────────
    public static async Task<string?> GetConfigAsync(string key)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT [Value] FROM dbo.Admin_Config WHERE [Key] = @k", conn);
        cmd.Parameters.AddWithValue("@k", key);
        return (string?)await cmd.ExecuteScalarAsync();
    }

    public static async Task SetConfigAsync(string key, string value)
    {
        using var conn = new SqlConnection(_cs);
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

    // ── Reminders ─────────────────────────────────────────────────────────────
    public static async Task<int> CreateReminderAsync(string message, DateTime scheduledAt, long channelId, string botId, string language)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "INSERT INTO dbo.Reminders (Message, ScheduledAt, ChannelId, BotId, Language) OUTPUT INSERTED.Id VALUES (@m, @s, @c, @b, @l)", conn);
        cmd.Parameters.AddWithValue("@m", message);
        cmd.Parameters.AddWithValue("@s", scheduledAt);
        cmd.Parameters.AddWithValue("@c", channelId);
        cmd.Parameters.AddWithValue("@b", botId);
        cmd.Parameters.AddWithValue("@l", language);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<List<ReminderRow>> ListPendingRemindersAsync()
    {
        var list = new List<ReminderRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, Message, ScheduledAt, ChannelId, BotId, Language, CreatedAt FROM dbo.Reminders WHERE SentAt IS NULL AND Cancelled = 0 ORDER BY ScheduledAt", conn);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new((int)rd["Id"], (string)rd["Message"], (DateTime)rd["ScheduledAt"],
                (long)rd["ChannelId"], (string)rd["BotId"], (string)rd["Language"], (DateTime)rd["CreatedAt"]));
        return list;
    }

    public static async Task CancelReminderAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Reminders SET Cancelled = 1 WHERE Id = @id", ("@id", (object)id));
    }

    public static async Task UpdateReminderAsync(int id, string message, DateTime scheduledAt)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn,
            "UPDATE dbo.Reminders SET Message=@m, ScheduledAt=@s WHERE Id=@id AND SentAt IS NULL AND Cancelled=0",
            ("@id", (object)id), ("@m", (object)message), ("@s", (object)scheduledAt));
    }

    public static async Task DeleteReminderAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "DELETE FROM dbo.Reminders WHERE Id=@id", ("@id", (object)id));
    }

    public static async Task MarkReminderSentAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Reminders SET SentAt = GETUTCDATE() WHERE Id = @id", ("@id", (object)id));
    }

    // ── Audit log ─────────────────────────────────────────────────────────────
    public static async Task LogAuditAsync(string actor, string action, string? detail)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn,
            "INSERT INTO dbo.Admin_AuditLog (Actor, Action, Detail) VALUES (@a, @ac, @d)",
            ("@a", (object)actor), ("@ac", (object)action),
            ("@d", detail is null ? (object)DBNull.Value : (object)detail));
        await Exec(conn,
            "DELETE FROM dbo.Admin_AuditLog WHERE At < DATEADD(day, -10, GETUTCDATE())");
    }

    public static async Task<List<AuditLogRow>> GetAuditLogsAsync(int limit = 200)
    {
        var list = new List<AuditLogRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            $"SELECT TOP {limit} Id, At, Actor, Action, Detail FROM dbo.Admin_AuditLog ORDER BY At DESC", conn);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new((int)rd["Id"], (DateTime)rd["At"], (string)rd["Actor"],
                (string)rd["Action"], rd["Detail"] == DBNull.Value ? null : (string)rd["Detail"]));
        return list;
    }

    // ── Apartment cleaning ────────────────────────────────────────────────────
    public static async Task<int> GetCleaningStateAsync(DateOnly date, int apt)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var val = await Scalar(conn,
            "SELECT State FROM dbo.ApartmentCleaning WHERE Date=@d AND ApartmentNumber=@a",
            ("@d", (object)date.ToString("yyyy-MM-dd")), ("@a", (object)apt));
        return val is null ? 0 : Convert.ToInt32(val);
    }

    public static async Task<List<CleaningRow>> ListCleaningAsync(DateOnly from, DateOnly to)
    {
        var list = new List<CleaningRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Date, ApartmentNumber, State FROM dbo.ApartmentCleaning WHERE Date >= @f AND Date <= @t", conn);
        cmd.Parameters.AddWithValue("@f", from.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("@t", to.ToString("yyyy-MM-dd"));
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new(DateOnly.FromDateTime((DateTime)rd["Date"]), (int)rd["ApartmentNumber"], Convert.ToInt32(rd["State"])));
        return list;
    }

    public static async Task<int> SetCleaningAsync(DateOnly date, int apt, int state)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var dateStr = date.ToString("yyyy-MM-dd");
        if (state == 0)
            await Exec(conn, "DELETE FROM dbo.ApartmentCleaning WHERE Date=@d AND ApartmentNumber=@a",
                ("@d", (object)dateStr), ("@a", (object)apt));
        else
        {
            var exists = await Scalar(conn,
                "SELECT COUNT(*) FROM dbo.ApartmentCleaning WHERE Date=@d AND ApartmentNumber=@a",
                ("@d", (object)dateStr), ("@a", (object)apt));
            if (Convert.ToInt32(exists) == 0)
                await Exec(conn, "INSERT INTO dbo.ApartmentCleaning (Date, ApartmentNumber, State) VALUES (@d, @a, @s)",
                    ("@d", (object)dateStr), ("@a", (object)apt), ("@s", (object)state));
            else
                await Exec(conn, "UPDATE dbo.ApartmentCleaning SET State=@s WHERE Date=@d AND ApartmentNumber=@a",
                    ("@s", (object)state), ("@d", (object)dateStr), ("@a", (object)apt));
        }
        return state;
    }

    public static async Task<int> ToggleCleaningAsync(DateOnly date, int apt)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        var dateStr = date.ToString("yyyy-MM-dd");
        var current = await Scalar(conn,
            "SELECT State FROM dbo.ApartmentCleaning WHERE Date=@d AND ApartmentNumber=@a",
            ("@d", (object)dateStr), ("@a", (object)apt));
        int cur = current is null ? 0 : Convert.ToInt32(current);
        int next = cur == 0 ? 1 : cur == 1 ? 2 : 0;
        if (next == 0)
            await Exec(conn, "DELETE FROM dbo.ApartmentCleaning WHERE Date=@d AND ApartmentNumber=@a",
                ("@d", (object)dateStr), ("@a", (object)apt));
        else if (cur == 0)
            await Exec(conn, "INSERT INTO dbo.ApartmentCleaning (Date, ApartmentNumber, State) VALUES (@d, @a, @s)",
                ("@d", (object)dateStr), ("@a", (object)apt), ("@s", (object)next));
        else
            await Exec(conn, "UPDATE dbo.ApartmentCleaning SET State=@s WHERE Date=@d AND ApartmentNumber=@a",
                ("@s", (object)next), ("@d", (object)dateStr), ("@a", (object)apt));
        return next;
    }

    // ── Maintenance tasks ──────────────────────────────────────────────────────
    public static async Task<List<MaintenanceTaskRow>> ListMaintenanceTasksAsync()
    {
        var list = new List<MaintenanceTaskRow>();
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            "SELECT Id, ApartmentNumber, TaskKey, IntervalWeeks, LastDoneAt, LastReminderCreatedAt FROM dbo.Admin_MaintenanceTasks ORDER BY ApartmentNumber, TaskKey", conn);
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            list.Add(new((int)rd["Id"], (int)rd["ApartmentNumber"], (string)rd["TaskKey"],
                (int)rd["IntervalWeeks"],
                rd["LastDoneAt"] == DBNull.Value ? null : (DateTime)rd["LastDoneAt"],
                rd["LastReminderCreatedAt"] == DBNull.Value ? null : (DateTime)rd["LastReminderCreatedAt"]));
        return list;
    }

    public static async Task MarkMaintenanceDoneAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_MaintenanceTasks SET LastDoneAt = GETUTCDATE(), LastReminderCreatedAt = NULL WHERE Id = @id",
            ("@id", (object)id));
    }

    public static async Task UpdateMaintenanceIntervalAsync(int id, int intervalWeeks)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_MaintenanceTasks SET IntervalWeeks = @w WHERE Id = @id",
            ("@id", (object)id), ("@w", (object)intervalWeeks));
    }

    public static async Task MarkMaintenanceReminderCreatedAsync(int id)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn, "UPDATE dbo.Admin_MaintenanceTasks SET LastReminderCreatedAt = GETUTCDATE() WHERE Id = @id",
            ("@id", (object)id));
    }

    // ── Password helpers ──────────────────────────────────────────────────────
    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split(':');
        if (parts.Length != 2) return false;
        var salt     = Convert.FromBase64String(parts[0]);
        var expected = Convert.FromBase64String(parts[1]);
        var actual   = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    // ── Telegram Log ──────────────────────────────────────────────────────────
    public static async Task AddTelegramLogAsync(string eventType, string messageType, string? channel, string? summary, bool isError)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        await Exec(conn,
            "INSERT INTO dbo.Telegram_Log (EventType, MessageType, Channel, Summary, IsError) VALUES (@et, @mt, @ch, @s, @ie)",
            ("@et", eventType), ("@mt", messageType),
            ("@ch", (object?)channel ?? DBNull.Value),
            ("@s",  (object?)summary ?? DBNull.Value),
            ("@ie", isError ? 1 : 0));
    }

    public static async Task<List<TelegramLogEntry>> GetTelegramLogAsync(int limit = 200)
    {
        using var conn = new SqlConnection(_cs);
        await conn.OpenAsync();
        using var cmd = new SqlCommand(
            $"SELECT TOP {limit} Id, At, EventType, MessageType, Channel, Summary, IsError FROM dbo.Telegram_Log ORDER BY At DESC", conn);
        using var rd = await cmd.ExecuteReaderAsync();
        var list = new List<TelegramLogEntry>();
        while (await rd.ReadAsync())
            list.Add(new TelegramLogEntry(
                (int)rd["Id"],
                (DateTime)rd["At"],
                (string)rd["EventType"],
                (string)rd["MessageType"],
                rd["Channel"] as string,
                rd["Summary"] as string,
                (bool)rd["IsError"]));
        return list;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    static async Task Exec(SqlConnection conn, string sql,
        params (string Name, object Value)[] prms)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in prms)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    static async Task<object?> Scalar(SqlConnection conn, string sql,
        params (string Name, object Value)[] prms)
    {
        using var cmd = new SqlCommand(sql, conn);
        foreach (var (name, value) in prms)
            cmd.Parameters.AddWithValue(name, value);
        return await cmd.ExecuteScalarAsync();
    }
}

record AdminUser(int Id, string Username, string Role, DateTime CreatedAt, string Language, string? GoogleEmail);
record ReminderRow(int Id, string Message, DateTime ScheduledAt, long ChannelId, string BotId, string Language, DateTime CreatedAt);
record AuditLogRow(int Id, DateTime At, string Actor, string Action, string? Detail);
record CleaningRow(DateOnly Date, int ApartmentNumber, int State);
record MaintenanceTaskRow(int Id, int ApartmentNumber, string TaskKey, int IntervalWeeks, DateTime? LastDoneAt, DateTime? LastReminderCreatedAt);
