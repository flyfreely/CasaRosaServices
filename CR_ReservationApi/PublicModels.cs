// ── Public registration API models ────────────────────────────────────────────

record PublicGuestRequest(
    string? Name,
    string? Nationality,
    string? BirthDate,        // "yyyy-MM-dd"
    string? Residency,
    string? CountryOfBirth,
    string? TypeOfId,
    string? IdNumber);

record PublicRegisterRequest(
    string?  ReservationGuid,
    string?  ReservationName,
    int      GuestCount,
    string?  CheckInDate,     // "yyyy-MM-dd"
    string?  CheckOutDate,    // "yyyy-MM-dd"
    int?     ApartmentNumber,
    string?  ArrivalMethod,
    string?  ArrivalTime,
    string?  FlightNumber,
    string?  ArrivalNotes,
    bool     EarlyCheckIn,
    bool     CribSetup,
    bool     SofaSetup,
    bool     FoldableBed,
    string?  OtherRequests,
    string?  InvoiceNif,
    string?  InvoiceName,
    string?  InvoiceAddress,
    string?  InvoiceEmail,
    List<PublicGuestRequest> Guests);

record PublicReservationInfoResponse(
    bool     AlreadySubmitted,
    string   ReservationName,
    int      GuestCount,
    int      ApartmentNumber,
    string   CheckInDate,
    string   CheckOutDate,
    string?  Email,
    string?  ArrivalMethod,
    string?  ArrivalTime,
    string?  FlightNumber,
    string?  ArrivalNotes,
    bool     EarlyCheckIn,
    bool     CribSetup,
    bool     SofaSetup,
    bool     FoldableBed,
    string?  OtherRequests,
    string?  InvoiceNif,
    string?  InvoiceName,
    string?  InvoiceAddress,
    string?  InvoiceEmail,
    List<PublicGuestInfo> Guests);

record PublicGuestInfo(
    string?  Name,
    string?  Nationality,
    string?  BirthDate,
    string?  Residency,
    string?  CountryOfBirth,
    string?  TypeOfId,
    string?  IdNumber);
