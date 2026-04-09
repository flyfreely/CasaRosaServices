record ReservationRow(
    int      Id,
    int      ApartmentNumber,
    string   ReservationName,
    string?  ConfirmationCode,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int      Nights,
    int      Adults,
    int      Children,
    int      Infants,
    string?  Status,
    bool     Enabled,
    bool     Archived,
    string?  MessagesUrl = null);

record ReservationDetail(
    int      Id,
    int      ApartmentNumber,
    string   ReservationName,
    string?  ConfirmationCode,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int      Nights,
    int      Adults,
    int      Children,
    int      Infants,
    string?  Status,
    bool     Enabled,
    bool     Archived,
    bool     Private,
    string?  PhoneNumber,
    string?  LivesIn,
    decimal  NightlyRate,
    decimal  Payout,
    decimal  GuestPaid,
    decimal  CleaningFee,
    Guid     RegistrationGuid,
    string?  MessagesUrl);

record RegistrationDetail(
    int     Id,
    string  Guid,
    string? Email,
    string? ArrivalMethod,
    string? ArrivalTime,
    string? FlightNumber,
    string? ArrivalNotes,
    bool    EarlyCheckIn,
    bool    Crib,
    bool    Sofa,
    bool    Foldable,
    string? OtherRequests,
    string? InvoiceNif,
    string? InvoiceName,
    string? InvoiceAddr,
    string? InvoiceEmail,
    string? Sef = null);

record CalNote(
    int     ReservationId,
    string? CheckInTime,
    string? CheckOutTime,
    bool    Crib,
    bool    EarlyCheckIn,
    bool    SofaBed,
    bool    OttomanBed,
    bool    LeavingBags);

record CalNoteRequest(
    string? CheckInTime,
    string? CheckOutTime,
    bool    Crib,
    bool    EarlyCheckIn,
    bool    SofaBed,
    bool    OttomanBed,
    bool    LeavingBags);

record GuestRow(
    int       Id,
    int       RegistrationId,
    string?   Name,
    string?   Nationality,
    DateTime? BirthDate,
    string?   Residency      = null,
    string?   CountryOfBirth = null,
    string?   TypeOfId       = null,
    string?   IdNumber       = null);

record CleaningRow(DateOnly Date, int ApartmentNumber, int State);
record AdminUserRow(int Id, string Username, string Role, DateTime CreatedAt, string Language = "en", string? GoogleEmail = null);
record AuditLogEntry(int Id, DateTime At, string Actor, string Action, string? Detail);
record ReminderAdminRow(int Id, string Message, DateTime ScheduledAt, long ChannelId, string BotId, string Language, DateTime CreatedAt);
record MonthlyStatsRow(string Month, int ApartmentNumber, int Nights, decimal Payout);
record MaintenanceTaskRow(int Id, int ApartmentNumber, string TaskKey, int IntervalWeeks, DateTime? LastDoneAt, DateTime? LastReminderCreatedAt);
