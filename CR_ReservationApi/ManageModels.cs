// ── Management response models ────────────────────────────────────────────────
record TelegramLogEntry(int Id, DateTime At, string EventType, string MessageType, string? Channel, string? Summary, bool IsError);
record TelegramLogRequest(string EventType, string MessageType, string? Channel, string? Summary, bool IsError = false);
record ManageReservationRow(
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

record ManageReservationDetail(
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

record ManageRegistrationDetail(
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
    string? InvoiceEmail);

record ManageGuestRow(
    int       Id,
    int       RegistrationId,
    string?   Name,
    string?   Nationality,
    DateTime? BirthDate);

// ── Write request models ──────────────────────────────────────────────────────
record ReservationCreateRequest(
    int      ApartmentNumber,
    string   ReservationName,
    string?  ConfirmationCode,
    string?  Status,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int      Adults,
    int      Children,
    int      Infants);

record ReservationUpdateRequest(
    int      ApartmentNumber,
    string   ReservationName,
    string?  ConfirmationCode,
    string?  Status,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int      Adults,
    int      Children,
    int      Infants,
    string?  PhoneNumber,
    string?  LivesIn,
    decimal  Payout,
    decimal  NightlyRate,
    decimal  CleaningFee,
    bool     Enabled,
    bool     Archived,
    bool     Private);

record RegistrationWriteRequest(
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
    string? InvoiceEmail);

record GuestWriteRequest(string? Name, string? Nationality, string? BirthDate);
record ReminderUpdateRequest(string Message, DateTime ScheduledAt);

record CalNote(
    int     ReservationId,
    string? CheckInTime,
    string? CheckOutTime,
    bool    Crib,
    bool    EarlyCheckIn,
    bool    SofaBed,
    bool    LeavingBags);

record CalNoteRequest(
    string? CheckInTime,
    string? CheckOutTime,
    bool    Crib,
    bool    EarlyCheckIn,
    bool    SofaBed,
    bool    LeavingBags);

record MonthlyStatsRow(string Month, int ApartmentNumber, int Nights, decimal Payout);
record AdminSetGoogleEmailRequest(string? Email);
record AuditLogRequest(string Actor, string Action, string? Detail);
