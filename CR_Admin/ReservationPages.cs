static class ReservationPages
{
    static readonly string Css = """
        *, *::before, *::after { box-sizing: border-box; }
        body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               background: #f4f4f4; color: #333; }
        a { color: #c0392b; text-decoration: none; }
        a:hover { text-decoration: underline; }

        /* ── Header ── */
        header { background: #c0392b; color: #fff; padding: .8rem 2rem;
                 display: flex; align-items: center; gap: 2rem; }
        header .brand { font-size: 1rem; font-weight: 700; white-space: nowrap; }
        header nav { display: flex; gap: .25rem; flex: 1; }
        header nav a { color: rgba(255,255,255,.75); padding: .4rem .8rem; border-radius: 5px;
                       font-size: .9rem; font-weight: 500; }
        header nav a:hover, header nav a.on { background: rgba(255,255,255,.2); color: #fff;
                                              text-decoration: none; }
        .out-btn { background: rgba(255,255,255,.15); border: none; color: #fff;
                   padding: .4rem .9rem; border-radius: 5px; cursor: pointer; font-size: .85rem;
                   margin-left: auto; }
        .out-btn:hover { background: rgba(255,255,255,.3); }

        /* ── Layout ── */
        main { max-width: 1100px; margin: 1.5rem auto; padding: 0 1rem; }
        .page-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.2rem; }
        .page-header h1 { margin: 0; font-size: 1.3rem; }
        .breadcrumb { font-size: .85rem; color: #999; margin-bottom: 1rem; }
        .breadcrumb a { color: #c0392b; }

        /* ── Cards / Sections ── */
        .card { background: #fff; border-radius: 10px;
                box-shadow: 0 2px 8px rgba(0,0,0,.07); padding: 1.5rem;
                margin-bottom: 1rem; }
        .section-title { font-size: .75rem; font-weight: 700; text-transform: uppercase;
                         letter-spacing: .06em; color: #aaa; margin: 0 0 1rem;
                         padding-bottom: .5rem; border-bottom: 1px solid #f0f0f0; }

        /* ── Grid helpers ── */
        .grid2 { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
        .grid3 { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 1rem; }
        .grid4 { display: grid; grid-template-columns: repeat(4, 1fr); gap: 1rem; }
        @media (max-width: 640px) { .grid2, .grid3, .grid4 { grid-template-columns: 1fr; } }

        /* ── Form controls ── */
        .field { margin-bottom: .8rem; }
        .field label { display: block; font-size: .78rem; font-weight: 600;
                       color: #666; margin-bottom: .25rem; }
        .field input, .field select, .field textarea {
            width: 100%; padding: .5rem .7rem; border: 1px solid #ddd;
            border-radius: 6px; font-size: .95rem; outline: none;
            font-family: inherit; background: #fff; }
        .field input:focus, .field select:focus, .field textarea:focus {
            border-color: #c0392b; box-shadow: 0 0 0 3px rgba(192,57,43,.1); }
        .field textarea { resize: vertical; min-height: 70px; }
        .check-row { display: flex; flex-wrap: wrap; gap: 1.2rem; margin-top: .3rem; }
        .check-row label { display: flex; align-items: center; gap: .4rem;
                           font-size: .9rem; font-weight: 400; color: #333;
                           cursor: pointer; }

        /* ── Buttons ── */
        .btn        { padding: .55rem 1.3rem; border: none; border-radius: 6px;
                      font-size: .9rem; font-weight: 600; cursor: pointer; }
        .btn-primary { background: #c0392b; color: #fff; }
        .btn-primary:hover { background: #a93226; }
        .btn-secondary { background: #f0f0f0; color: #333; }
        .btn-secondary:hover { background: #e0e0e0; }
        .btn-danger  { background: #fdecea; color: #c0392b; }
        .btn-danger:hover { background: #fbc9c7; }
        .btn-sm { padding: .3rem .7rem; font-size: .8rem; }

        /* ── Badges ── */
        .badge { display: inline-block; padding: .2rem .65rem; border-radius: 20px;
                 font-size: .75rem; font-weight: 700; white-space: nowrap; }
        .badge-green  { background: #c8e6c9; color: #1b5e20; }
        .badge-blue   { background: #bbdefb; color: #0d47a1; }
        .badge-orange { background: #ffe0b2; color: #e65100; }
        .badge-red    { background: #ffcdd2; color: #b71c1c; }
        .badge-gray   { background: #e0e0e0; color: #616161; }
        .badge-apt    { background: #ede7f6; color: #4a148c; font-size: .7rem; }

        /* ── Filter bar ── */
        .filter-bar { display: flex; align-items: flex-end; gap: .75rem; flex-wrap: wrap;
                      background: #fff; border-radius: 10px;
                      box-shadow: 0 2px 8px rgba(0,0,0,.07);
                      padding: 1rem 1.25rem; margin-bottom: 1rem; }
        .filter-bar .field { margin: 0; }
        .filter-bar .field label { font-size: .75rem; }
        .filter-bar .field select, .filter-bar .field input[type=date] {
            padding: .4rem .6rem; font-size: .9rem; }

        /* ── Table ── */
        .res-table { width: 100%; border-collapse: collapse; }
        .res-table th { font-size: .75rem; font-weight: 700; text-transform: uppercase;
                        letter-spacing: .05em; color: #aaa; text-align: left;
                        padding: .6rem .8rem; border-bottom: 2px solid #f0f0f0; }
        .res-table td { padding: .65rem .8rem; font-size: .9rem;
                        border-bottom: 1px solid #f5f5f5; vertical-align: middle; }
        .res-table tr:hover td { background: rgba(0,0,0,.015); }
        .row-checkin  td { background: #f1f8e9 !important; }
        .row-checkout td { background: #fff8e1 !important; }
        .row-hosting  td { background: #e8f4fd !important; }
        .row-past     td { opacity: .6; }
        .row-cancelled td { background: #fce4ec !important; opacity: .8; }
        .row-archived  td { opacity: .5; }
        .apt-badge { display: inline-flex; align-items: center; justify-content: center;
                     width: 24px; height: 24px; border-radius: 50%;
                     background: #ede7f6; color: #4a148c; font-size: .75rem; font-weight: 700; }

        /* ── Detail page ── */
        .detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; }
        @media (max-width: 760px) { .detail-grid { grid-template-columns: 1fr; } }
        .ok-msg  { padding: .6rem 1rem; background: #e8f5e9; color: #1b5e20;
                   border-radius: 6px; font-size: .85rem; font-weight: 600;
                   margin-bottom: .8rem; }

        /* ── Guest table ── */
        .guest-table { width: 100%; border-collapse: collapse; font-size: .9rem; }
        .guest-table th { font-size: .75rem; font-weight: 700; color: #aaa;
                          text-transform: uppercase; letter-spacing: .05em;
                          padding: .4rem .6rem; border-bottom: 2px solid #f0f0f0; text-align: left; }
        .guest-table td { padding: .55rem .6rem; border-bottom: 1px solid #f5f5f5; }
        .add-guest-row td { padding-top: .8rem; background: #fafafa; }

        /* ── Empty state ── */
        .empty { text-align: center; padding: 3rem 1rem; color: #bbb; }
        .empty .icon { font-size: 2.5rem; margin-bottom: .5rem; }
        .empty p { margin: 0; font-size: .95rem; }

        /* ── Details/summary (invoice collapse) ── */
        details summary { cursor: pointer; font-size: .8rem; font-weight: 600;
                          color: #c0392b; padding: .3rem 0; list-style: none; }
        details summary::before { content: '▶ '; font-size: .7rem; }
        details[open] summary::before { content: '▼ '; }
        """;

    static string Header(string active) => $"""
        <header>
          <div class="brand">Casa Rosa Admin</div>
          <nav>
            <a href="/dashboard" class="{(active == "settings" ? "on" : "")}">Settings</a>
            <a href="/reservations" class="{(active == "reservations" ? "on" : "")}">Reservations</a>
          </nav>
          <form method="post" action="/logout" style="margin:0">
            <button class="out-btn" type="submit">Sign out</button>
          </form>
        </header>
        """;

    static string BadgeForRow(ReservationRow r)
    {
        if (r.Archived) return "<span class='badge badge-gray'>Archived</span>";
        if (!r.Enabled) return "<span class='badge badge-gray'>Disabled</span>";
        if (r.Status?.Contains("ancel", StringComparison.OrdinalIgnoreCase) == true)
            return "<span class='badge badge-red'>Cancelled</span>";
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (r.CheckInDate  == today) return "<span class='badge badge-green'>Checking in</span>";
        if (r.CheckOutDate == today) return "<span class='badge badge-orange'>Checking out</span>";
        if (r.CheckInDate < today && r.CheckOutDate > today) return "<span class='badge badge-blue'>Hosting</span>";
        if (r.CheckOutDate < today) return "<span class='badge badge-gray'>Completed</span>";
        return "<span class='badge badge-green'>Confirmed</span>";
    }

    static string RowClass(ReservationRow r)
    {
        if (r.Archived) return "row-archived";
        if (r.Status?.Contains("ancel", StringComparison.OrdinalIgnoreCase) == true) return "row-cancelled";
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (r.CheckInDate  == today) return "row-checkin";
        if (r.CheckOutDate == today) return "row-checkout";
        if (r.CheckInDate < today && r.CheckOutDate > today) return "row-hosting";
        if (r.CheckOutDate < today) return "row-past";
        return "";
    }

    static int? CalcAge(DateTime? dob)
    {
        if (dob == null) return null;
        var today = DateTime.Today;
        var age = today.Year - dob.Value.Year;
        if (today < dob.Value.AddYears(age)) age--;
        return age;
    }

    static string Chk(bool v, string name, string label) =>
        $"<label><input type='checkbox' name='{name}'{(v ? " checked" : "")}/> {label}</label>";

    static string Sel(string name, string val, params (string V, string L)[] opts)
    {
        var sb = new System.Text.StringBuilder($"<select name='{name}'>");
        foreach (var (v, l) in opts)
            sb.Append($"<option value='{v}'{(v == val ? " selected" : "")}>{l}</option>");
        sb.Append("</select>");
        return sb.ToString();
    }

    // ── Reservations list ─────────────────────────────────────────────────────
    public static string List(
        List<ReservationRow> rows, int apt, DateOnly from, DateOnly to, string status)
    {
        var rowsHtml = new System.Text.StringBuilder();
        if (rows.Count == 0)
        {
            rowsHtml.Append("""
                <tr><td colspan="8">
                  <div class="empty"><div class="icon">📅</div><p>No reservations found for these filters.</p></div>
                </td></tr>
                """);
        }
        else
        {
            foreach (var r in rows)
            {
                var guests = r.Adults + r.Children + r.Infants;
                var guestStr = r.Adults > 0 ? $"{r.Adults}A" : "";
                if (r.Children > 0) guestStr += $"+{r.Children}C";
                if (r.Infants  > 0) guestStr += $"+{r.Infants}I";
                rowsHtml.Append($"""
                    <tr class="{RowClass(r)}">
                      <td><span class="apt-badge">{r.ApartmentNumber}</span></td>
                      <td><a href="/reservations/{r.Id}"><strong>{System.Net.WebUtility.HtmlEncode(r.ReservationName)}</strong></a>
                          {(r.ConfirmationCode != null ? $"<br><small style='color:#aaa'>{r.ConfirmationCode}</small>" : "")}</td>
                      <td>{r.CheckInDate:MMM d, yyyy}</td>
                      <td>{r.CheckOutDate:MMM d, yyyy}</td>
                      <td style="text-align:center">{r.Nights}</td>
                      <td style="text-align:center">{guestStr}</td>
                      <td>{BadgeForRow(r)}</td>
                      <td><a href="/reservations/{r.Id}" class="btn btn-secondary btn-sm">Edit</a></td>
                    </tr>
                    """);
            }
        }

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>Reservations – Casa Rosa Admin</title>
              <style>{Css}</style>
            </head>
            <body>
              {Header("reservations")}
              <main>
                <div class="page-header">
                  <h1>Reservations</h1>
                  <a href="/reservations/new" class="btn btn-primary" style="margin-left:auto">+ New Reservation</a>
                </div>

                <form method="get" action="/reservations">
                  <div class="filter-bar">
                    <div class="field">
                      <label>Apartment</label>
                      {Sel("apt", apt.ToString(),
                          ("0","All"), ("1","Apt 1"), ("2","Apt 2"), ("3","Apt 3"))}
                    </div>
                    <div class="field">
                      <label>From</label>
                      <input type="date" name="from" value="{from:yyyy-MM-dd}"/>
                    </div>
                    <div class="field">
                      <label>To</label>
                      <input type="date" name="to" value="{to:yyyy-MM-dd}"/>
                    </div>
                    <div class="field">
                      <label>Status</label>
                      {Sel("status", status,
                          ("active","Active"), ("all","All"), ("cancelled","Cancelled"), ("archived","Archived"))}
                    </div>
                    <button class="btn btn-secondary" type="submit">Filter</button>
                  </div>
                </form>

                <div class="card" style="padding:0; overflow:hidden">
                  <table class="res-table">
                    <thead>
                      <tr>
                        <th>Apt</th><th>Guest</th><th>Check-in</th><th>Check-out</th>
                        <th style="text-align:center">Nights</th>
                        <th style="text-align:center">Guests</th>
                        <th>Status</th><th></th>
                      </tr>
                    </thead>
                    <tbody>{rowsHtml}</tbody>
                  </table>
                </div>
                <p style="font-size:.8rem;color:#aaa;text-align:right">{rows.Count} reservation{(rows.Count != 1 ? "s" : "")}</p>
              </main>
            </body>
            </html>
            """;
    }

    // ── New reservation form ──────────────────────────────────────────────────
    public static string NewForm() => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1"/>
          <title>New Reservation – Casa Rosa Admin</title>
          <style>{Css}</style>
        </head>
        <body>
          {Header("reservations")}
          <main>
            <div class="breadcrumb"><a href="/reservations">← Reservations</a></div>
            <div class="page-header"><h1>New Reservation</h1></div>
            <div class="card">
              <form method="post" action="/reservations/new">
                <div class="grid2">
                  <div>
                    <p class="section-title">Stay Details</p>
                    <div class="field">
                      <label>Apartment</label>
                      {Sel("apartment", "1", ("1","Apartment 1"), ("2","Apartment 2"), ("3","Apartment 3"))}
                    </div>
                    <div class="grid2">
                      <div class="field"><label>Check-in</label><input type="date" name="checkin" required/></div>
                      <div class="field"><label>Check-out</label><input type="date" name="checkout" required/></div>
                    </div>
                    <div class="field"><label>Status</label>
                      <input type="text" name="status" value="confirmed" list="status-opts"/>
                      <datalist id="status-opts">
                        <option value="confirmed"/><option value="cancelled_by_guest"/>
                        <option value="cancelled_by_host"/><option value="completed"/>
                      </datalist>
                    </div>
                    <div class="field"><label>Confirmation Code</label>
                      <input type="text" name="code" placeholder="e.g. HMY8BMNFN8"/></div>
                  </div>
                  <div>
                    <p class="section-title">Guest Info</p>
                    <div class="field"><label>Guest Name</label>
                      <input type="text" name="name" required placeholder="Full name"/></div>
                    <div class="grid3">
                      <div class="field"><label>Adults</label><input type="number" name="adults" value="1" min="0"/></div>
                      <div class="field"><label>Children</label><input type="number" name="children" value="0" min="0"/></div>
                      <div class="field"><label>Infants</label><input type="number" name="infants" value="0" min="0"/></div>
                    </div>
                  </div>
                </div>
                <div style="margin-top:1rem;display:flex;gap:.75rem">
                  <button class="btn btn-primary" type="submit">Create Reservation</button>
                  <a href="/reservations" class="btn btn-secondary">Cancel</a>
                </div>
              </form>
            </div>
          </main>
        </body>
        </html>
        """;

    // ── Reservation detail ────────────────────────────────────────────────────
    public static string Detail(
        ReservationDetail res,
        RegistrationDetail? reg,
        List<GuestRow> guests,
        string message = "")
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Status badge
        string statusBadge;
        if (res.Archived) statusBadge = "<span class='badge badge-gray'>Archived</span>";
        else if (res.Status?.Contains("ancel", StringComparison.OrdinalIgnoreCase) == true)
            statusBadge = "<span class='badge badge-red'>Cancelled</span>";
        else if (res.CheckInDate == today)  statusBadge = "<span class='badge badge-green'>Checking in today</span>";
        else if (res.CheckOutDate == today) statusBadge = "<span class='badge badge-orange'>Checking out today</span>";
        else if (res.CheckInDate < today && res.CheckOutDate > today)
            statusBadge = "<span class='badge badge-blue'>Currently hosting</span>";
        else if (res.CheckOutDate < today)  statusBadge = "<span class='badge badge-gray'>Completed</span>";
        else statusBadge = "<span class='badge badge-green'>Confirmed</span>";

        var okMsg = string.IsNullOrEmpty(message) ? "" : $"<div class='ok-msg'>✓ {message}</div>";

        // Registration section
        string regSection;
        if (reg == null)
        {
            regSection = $"""
                <div class="card">
                  <p class="section-title">Registration</p>
                  <p style="color:#aaa;font-size:.9rem;margin:.5rem 0">No registration form has been filled out yet.</p>
                  <form method="post" action="/reservations/{res.Id}/registration">
                    <input type="hidden" name="_action" value="create"/>
                    <button class="btn btn-secondary" type="submit">+ Create Registration</button>
                  </form>
                </div>
                """;
        }
        else
        {
            regSection = $"""
                <div class="card">
                  <p class="section-title">Registration</p>
                  <form method="post" action="/reservations/{res.Id}/registration">
                    <input type="hidden" name="_action" value="update"/>
                    <input type="hidden" name="regId" value="{reg.Id}"/>
                    <div class="grid2">
                      <div>
                        <div class="field"><label>Email</label>
                          <input type="email" name="email" value="{H(reg.Email)}"/></div>
                        <div class="field"><label>Arrival Method</label>
                          <input type="text" name="arrivalMethod" value="{H(reg.ArrivalMethod)}" list="arr-method-opts"/>
                          <datalist id="arr-method-opts">
                            <option value="Self check-in"/><option value="Airport transfer"/>
                            <option value="Rental car"/><option value="Train"/><option value="Not sure"/>
                          </datalist>
                        </div>
                        <div class="grid2">
                          <div class="field"><label>Arrival Time</label>
                            <input type="text" name="arrivalTime" value="{H(reg.ArrivalTime)}"
                                   placeholder="e.g. 3PM-4PM" list="arr-time-opts"/>
                            <datalist id="arr-time-opts">
                              <option value="Before 3PM"/><option value="3PM-4PM"/>
                              <option value="4PM-5PM"/><option value="5PM-6PM"/>
                              <option value="6PM-7PM"/><option value="After 7PM"/>
                            </datalist>
                          </div>
                          <div class="field"><label>Flight Number</label>
                            <input type="text" name="flight" value="{H(reg.FlightNumber)}" placeholder="TP123"/></div>
                        </div>
                        <div class="field"><label>Arrival Notes</label>
                          <textarea name="arrivalNotes">{H(reg.ArrivalNotes)}</textarea></div>
                      </div>
                      <div>
                        <div class="field">
                          <label>Special Requests</label>
                          <div class="check-row">
                            {Chk(reg.EarlyCheckIn, "earlyCI",  "Early check-in")}
                            {Chk(reg.Crib,         "crib",     "Crib")}
                            {Chk(reg.Sofa,         "sofa",     "Sofa bed")}
                            {Chk(reg.Foldable,     "foldable", "Foldable bed")}
                          </div>
                        </div>
                        <div class="field"><label>Other Requests</label>
                          <textarea name="otherRequests">{H(reg.OtherRequests)}</textarea></div>
                        <details style="margin-top:.5rem">
                          <summary>Invoice Details</summary>
                          <div style="margin-top:.8rem">
                            <div class="grid2">
                              <div class="field"><label>NIF</label>
                                <input type="text" name="invoiceNif" value="{H(reg.InvoiceNif)}"/></div>
                              <div class="field"><label>Name</label>
                                <input type="text" name="invoiceName" value="{H(reg.InvoiceName)}"/></div>
                            </div>
                            <div class="field"><label>Address</label>
                              <input type="text" name="invoiceAddr" value="{H(reg.InvoiceAddr)}"/></div>
                            <div class="field"><label>Invoice Email</label>
                              <input type="email" name="invoiceEmail" value="{H(reg.InvoiceEmail)}"/></div>
                          </div>
                        </details>
                      </div>
                    </div>
                    <button class="btn btn-primary" style="margin-top:.5rem" type="submit">Save Registration</button>
                  </form>
                </div>
                """;
        }

        // Guests section
        var guestRows = new System.Text.StringBuilder();
        foreach (var g in guests)
        {
            var age = CalcAge(g.BirthDate);
            guestRows.Append($"""
                <tr>
                  <td>{H(g.Name)}</td>
                  <td>{H(g.Nationality)}</td>
                  <td>{(age.HasValue ? age.ToString() : "—")}</td>
                  <td>
                    <form method="post" action="/reservations/{res.Id}/guests/{g.Id}/delete" style="margin:0">
                      <button class="btn btn-danger btn-sm" type="submit"
                              onclick="return confirm('Remove this guest?')">Remove</button>
                    </form>
                  </td>
                </tr>
                """);
        }

        var addGuestForm = reg != null ? $"""
            <tr class="add-guest-row">
              <td colspan="4">
                <form method="post" action="/reservations/{res.Id}/guests"
                      style="display:flex;gap:.5rem;align-items:flex-end;flex-wrap:wrap">
                  <input type="hidden" name="registrationId" value="{reg.Id}"/>
                  <div class="field" style="margin:0;flex:2;min-width:120px">
                    <label>Name</label><input type="text" name="guestName" placeholder="Full name"/>
                  </div>
                  <div class="field" style="margin:0;flex:1;min-width:100px">
                    <label>Nationality</label><input type="text" name="guestNat" placeholder="Country"/>
                  </div>
                  <div class="field" style="margin:0;flex:1;min-width:120px">
                    <label>Date of Birth</label><input type="date" name="guestDob"/>
                  </div>
                  <button class="btn btn-secondary" type="submit">Add Guest</button>
                </form>
              </td>
            </tr>
            """ : """<tr><td colspan="4" style="color:#aaa;font-size:.85rem;padding:.6rem">Create a registration first to add guests.</td></tr>""";

        var guestsSection = $"""
            <div class="card">
              <p class="section-title">Guests ({guests.Count})</p>
              <table class="guest-table">
                <thead><tr><th>Name</th><th>Nationality</th><th>Age</th><th></th></tr></thead>
                <tbody>
                  {guestRows}
                  {addGuestForm}
                </tbody>
              </table>
            </div>
            """;

        var messagesLink = !string.IsNullOrEmpty(res.MessagesUrl)
            ? $"<a href='{res.MessagesUrl}' target='_blank' class='btn btn-secondary btn-sm'>Airbnb messages ↗</a>"
            : "";

        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>{System.Net.WebUtility.HtmlEncode(res.ReservationName)} – Casa Rosa Admin</title>
              <style>{Css}</style>
            </head>
            <body>
              {Header("reservations")}
              <main>
                <div class="breadcrumb"><a href="/reservations">← Reservations</a></div>
                <div class="page-header">
                  <span class="apt-badge" style="width:32px;height:32px;font-size:.9rem">{res.ApartmentNumber}</span>
                  <h1>{System.Net.WebUtility.HtmlEncode(res.ReservationName)}</h1>
                  {statusBadge}
                  {messagesLink}
                </div>
                {okMsg}

                <!-- Reservation form -->
                <div class="card">
                  <form method="post" action="/reservations/{res.Id}">
                    <div class="detail-grid">
                      <div>
                        <p class="section-title">Stay Details</p>
                        <div class="field"><label>Apartment</label>
                          {Sel("apartment", res.ApartmentNumber.ToString(),
                              ("1","Apartment 1"), ("2","Apartment 2"), ("3","Apartment 3"))}
                        </div>
                        <div class="grid2">
                          <div class="field"><label>Check-in</label>
                            <input type="date" name="checkin" value="{res.CheckInDate:yyyy-MM-dd}"/></div>
                          <div class="field"><label>Check-out</label>
                            <input type="date" name="checkout" value="{res.CheckOutDate:yyyy-MM-dd}"/></div>
                        </div>
                        <div class="field"><label>Status</label>
                          <input type="text" name="status" value="{H(res.Status)}" list="status-opts"/>
                          <datalist id="status-opts">
                            <option value="confirmed"/><option value="cancelled_by_guest"/>
                            <option value="cancelled_by_host"/><option value="completed"/>
                          </datalist>
                        </div>
                        <div class="field"><label>Confirmation Code</label>
                          <input type="text" name="code" value="{H(res.ConfirmationCode)}"/></div>
                        <div class="grid2">
                          <div class="field"><label>Nightly Rate (€)</label>
                            <input type="number" step=".01" name="rate" value="{res.NightlyRate:F2}"/></div>
                          <div class="field"><label>Cleaning Fee (€)</label>
                            <input type="number" step=".01" name="cleaning" value="{res.CleaningFee:F2}"/></div>
                        </div>
                        <div class="field">
                          <label>Flags</label>
                          <div class="check-row">
                            {Chk(res.Enabled,  "enabled",  "Enabled")}
                            {Chk(res.Archived, "archived", "Archived")}
                            {Chk(res.Private,  "private",  "Private")}
                          </div>
                        </div>
                      </div>
                      <div>
                        <p class="section-title">Guest Info</p>
                        <div class="field"><label>Guest Name</label>
                          <input type="text" name="name" value="{H(res.ReservationName)}"/></div>
                        <div class="grid2">
                          <div class="field"><label>Phone</label>
                            <input type="tel" name="phone" value="{H(res.PhoneNumber)}"/></div>
                          <div class="field"><label>Lives In</label>
                            <input type="text" name="livesIn" value="{H(res.LivesIn)}" placeholder="Country"/></div>
                        </div>
                        <div class="grid3">
                          <div class="field"><label>Adults</label>
                            <input type="number" name="adults" min="0" value="{res.Adults}"/></div>
                          <div class="field"><label>Children</label>
                            <input type="number" name="children" min="0" value="{res.Children}"/></div>
                          <div class="field"><label>Infants</label>
                            <input type="number" name="infants" min="0" value="{res.Infants}"/></div>
                        </div>
                        <div style="margin-top:.5rem;padding:.8rem;background:#f9f9f9;border-radius:6px;font-size:.85rem">
                          <strong>{res.Nights}</strong> night{(res.Nights != 1 ? "s" : "")} &nbsp;·&nbsp;
                          Payout: <strong>€{res.Payout:F2}</strong> &nbsp;·&nbsp;
                          Guest paid: <strong>€{res.GuestPaid:F2}</strong>
                        </div>
                      </div>
                    </div>
                    <button class="btn btn-primary" style="margin-top:1rem" type="submit">Save Reservation</button>
                  </form>
                </div>

                {regSection}
                {guestsSection}
              </main>
            </body>
            </html>
            """;
    }

    // HTML-encode helper
    static string H(string? s) => s == null ? "" : System.Net.WebUtility.HtmlEncode(s);
}
