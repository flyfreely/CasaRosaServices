static class ReservationPages
{
    public static readonly string Css = """
        *, *::before, *::after { box-sizing: border-box; }
        body { margin: 0; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
               background: #FA8072; color: #333; }
        a { color: rgb(33,37,41); text-decoration: none; }
        a:hover { text-decoration: underline; }

        /* ── Header ── */
        header { background: rgb(33,37,41); color: #fff; padding: .7rem 1.5rem;
                 display: flex; align-items: center; gap: .8rem; flex-wrap: wrap; }
        header .brand { display: flex; align-items: center; gap: .4rem; white-space: nowrap; }
        header .brand .brand-text { height: 28px; width: auto; mix-blend-mode: screen; }
        .site-footer { display:flex; align-items:center; justify-content:center;
                       padding:2rem 1rem 2.5rem; }
        .site-footer img.fi { height:72px; width:auto; }
        header nav { display: flex; gap: .15rem; align-items: center; flex: 1; }
        header nav a { color: rgba(255,255,255,.75); padding: .4rem .75rem; border-radius: 5px;
                       font-size: .87rem; font-weight: 500; white-space: nowrap; }
        header nav a:hover, header nav a.on { background: rgba(255,255,255,.2); color: #fff;
                                              text-decoration: none; }
        /* Dropdown */
        .dropdown { position: relative; }
        .dropdown-toggle { color: rgba(255,255,255,.75); padding: .4rem .75rem; border-radius: 5px;
                           font-size: .87rem; font-weight: 500; cursor: pointer; white-space: nowrap;
                           user-select: none; display: inline-block; }
        .dropdown-toggle:hover, .dropdown-toggle.on { background: rgba(255,255,255,.2); color: #fff; }
        .dropdown-menu { display: none; position: absolute; top: 100%; left: 0;
                         background: rgb(20,23,26); border-radius: 7px; min-width: 150px;
                         box-shadow: 0 6px 20px rgba(0,0,0,.4); z-index: 300; padding: .5rem 0 .35rem; }
        .dropdown:hover .dropdown-menu { display: block; }
        .dropdown-menu a { display: block; padding: .5rem 1rem; color: rgba(255,255,255,.75);
                           font-size: .87rem; white-space: nowrap; }
        .dropdown-menu a:hover, .dropdown-menu a.on { background: rgba(255,255,255,.1);
                                                      color: #fff; text-decoration: none; }
        /* Header right */
        .header-end { display: flex; align-items: center; gap: .4rem; margin-left: auto; }
        .out-btn { background: rgba(255,255,255,.15); border: none; color: #fff;
                   padding: .38rem .8rem; border-radius: 5px; cursor: pointer; font-size: .82rem; }
        .out-btn:hover { background: rgba(255,255,255,.3); }
        /* Hamburger */
        .burger { display: none; background: none; border: none; color: rgba(255,255,255,.85);
                  font-size: 1.4rem; cursor: pointer; padding: .1rem .4rem; line-height: 1; }
        @media (max-width: 768px) {
          .burger { display: block; }
          header nav { display: none; width: 100%; order: 4; flex-direction: column;
                       align-items: stretch; background: rgb(25,28,31); border-radius: 8px;
                       padding: .5rem; gap: .2rem; }
          header nav.open { display: flex; }
          .header-end { order: 3; margin-left: auto; }
          .dropdown { width: 100%; }
          .dropdown-menu { position: static; display: block !important; background: transparent;
                           box-shadow: none; padding: 0 0 0 .8rem; }
          .dropdown-toggle { width: 100%; display: block; pointer-events: none; }
        }

        /* ── Layout ── */
        main { max-width: 1100px; margin: 1.5rem auto; padding: 0 1rem; }
        .page-header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.2rem; }
        .page-header h1 { margin: 0; font-size: 1.3rem; }
        .breadcrumb { font-size: .85rem; color: #999; margin-bottom: 1rem; }
        .breadcrumb a { color: rgb(33,37,41); }

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
            border-color: rgb(33,37,41); box-shadow: 0 0 0 3px rgba(33,37,41,.15); }
        .field textarea { resize: vertical; min-height: 70px; }
        .check-row { display: flex; flex-wrap: wrap; gap: 1.2rem; margin-top: .3rem; }
        .check-row label { display: flex; align-items: center; gap: .4rem;
                           font-size: .9rem; font-weight: 400; color: #333;
                           cursor: pointer; }

        /* ── Buttons ── */
        .btn        { padding: .55rem 1.3rem; border: none; border-radius: 6px;
                      font-size: .9rem; font-weight: 600; cursor: pointer; }
        .btn-primary { background: rgb(33,37,41); color: #fff; }
        .btn-primary:hover { background: rgb(20,23,26); }
        .btn-secondary { background: #f0f0f0; color: #333; }
        .btn-secondary:hover { background: #e0e0e0; }
        .btn-danger  { background: #fdecea; color: rgb(33,37,41); }
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
        .row-checkin   td { background: #f1f8e9 !important; }
        .row-checkout  td { background: #fff8e1 !important; }
        .row-hosting   td { background: #e8f4fd !important; }
        .row-confirmed td { background: #f0f0f0 !important; }
        .row-past      td { opacity: .6; }
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

        /* ── Sort arrows ── */
        th[data-col]:hover { color: rgb(33,37,41); }
        .sort-arrow { font-size: .7rem; color: #ccc; }
        th[data-col]:hover .sort-arrow { color: rgb(33,37,41); }

        /* ── Empty state ── */
        .empty { text-align: center; padding: 3rem 1rem; color: #bbb; }
        .empty .icon { font-size: 2.5rem; margin-bottom: .5rem; }
        .empty p { margin: 0; font-size: .95rem; }

        /* ── Details/summary (invoice collapse) ── */
        details summary { cursor: pointer; font-size: .8rem; font-weight: 600;
                          color: rgb(33,37,41); padding: .3rem 0; list-style: none; }
        details summary::before { content: '▶ '; font-size: .7rem; }
        details[open] summary::before { content: '▼ '; }
        """;

    public static string Footer() => """
        <footer class="site-footer">
          <img class="fi" src="/logo.png" alt=""/>
        </footer>
        """;

    public static string Header(string active, string lang = "en", bool isAdmin = false)
    {
        var adminActive = active is "settings" or "reminders" or "users" or "statistics" or "auditlog" or "log";
        var adminMenu = isAdmin ? $"""
                <div class="dropdown">
                  <span class="dropdown-toggle {(adminActive ? "on" : "")}">{T.Get(lang, "Admin")} ▾</span>
                  <div class="dropdown-menu">
                    <a href="/statistics" class="{(active == "statistics" ? "on" : "")}">{T.Get(lang, "Statistics")}</a>
                    <a href="/dashboard"  class="{(active == "settings"  ? "on" : "")}">{T.Get(lang, "Settings")}</a>
                    <a href="/reminders"  class="{(active == "reminders" ? "on" : "")}">{T.Get(lang, "Reminders")}</a>
                    <a href="/users"      class="{(active == "users"     ? "on" : "")}">{T.Get(lang, "Users")}</a>
                    <a href="/audit-log"  class="{(active == "auditlog"  ? "on" : "")}">{T.Get(lang, "Audit Log")}</a>
                    <a href="/log"        class="{(active == "log"       ? "on" : "")}">{T.Get(lang, "Telegram Log")}</a>
                  </div>
                </div>
        """ : "";
        return $"""
            <header>
              <div class="brand"><img class="brand-text" src="/casarosa.png" alt="Casa Rosa"/></div>
              <button class="burger" onclick="this.closest('header').querySelector('nav').classList.toggle('open')" aria-label="Menu">☰</button>
              <nav>
                <a href="/calendar"     class="{(active == "calendar"     ? "on" : "")}">{T.Get(lang, "Calendar")}</a>
                <a href="/reservations" class="{(active == "reservations" ? "on" : "")}">{T.Get(lang, "Reservations")}</a>
                {adminMenu}
              </nav>
              <div class="header-end">
                <a href="/lang/{(lang == "ru" ? "en" : "ru")}" class="out-btn">{(lang == "ru" ? "EN" : "RU")}</a>
                <form method="post" action="/logout" style="margin:0">
                  <button class="out-btn" type="submit">{T.Get(lang, "Sign out")}</button>
                </form>
              </div>
            </header>
            """;
    }

    static string BadgeForRow(ReservationRow r, string lang = "en")
    {
        if (r.Archived) return $"<span class='badge badge-gray'>{T.Get(lang, "Archived")}</span>";
        if (!r.Enabled) return $"<span class='badge badge-gray'>{T.Get(lang, "Disabled")}</span>";
        if (r.Status?.Contains("ancel", StringComparison.OrdinalIgnoreCase) == true)
            return $"<span class='badge badge-red'>{T.Get(lang, "Cancelled")}</span>";
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now   = DateTime.Now;
        if (r.CheckInDate  == today)
        {
            var key = now.Hour >= 15 ? "Checked in" : "Checking in";
            return $"<span class='badge badge-green'>{T.Get(lang, key)}</span>";
        }
        if (r.CheckOutDate == today)
        {
            var key = now.Hour >= 11 ? "Checked out" : "Checking out";
            return $"<span class='badge badge-orange'>{T.Get(lang, key)}</span>";
        }
        if (r.CheckInDate < today && r.CheckOutDate > today) return $"<span class='badge badge-blue'>{T.Get(lang, "Hosting")}</span>";
        if (r.CheckOutDate < today) return $"<span class='badge badge-gray'>{T.Get(lang, "Completed")}</span>";
        return $"<span class='badge badge-green'>{T.Get(lang, "Confirmed")}</span>";
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
        if (r.CheckInDate > today) return "row-confirmed";
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

    static string ListingName(int apt) => apt switch
    {
        1 => "Cozy, modern, steps to main square, 2mins to beach",
        2 => "Historic center, artistic under 2min walk to beach",
        3 => "Oceanview studio with 20m2 terrace, historic center",
        _ => ""
    };

    // Renders a read-only listing label + JS to update it on apartment change
    static string ListingDisplay(int apt, string lang = "en") => $"""
        <div class="field" style="margin-bottom:.6rem">
          <label>{T.Get(lang, "Listing")}</label>
          <div id="listing-name" style="font-size:.9rem;color:#555;padding:.4rem 0;font-style:italic">
            {System.Net.WebUtility.HtmlEncode(ListingName(apt))}
          </div>
        </div>
        """;

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
        List<ReservationRow> rows, int apt, DateOnly from, DateOnly to, string status, bool isAdmin = false, string lang = "en")
    {
        var rowsHtml = new System.Text.StringBuilder();
        if (rows.Count == 0)
        {
            rowsHtml.Append($"""
                <tr><td colspan="8">
                  <div class="empty"><div class="icon">📅</div><p>{T.Get(lang, "No reservations found for these filters.")}</p></div>
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
                    <tr class="{RowClass(r)}"
                        data-apt="{r.ApartmentNumber}"
                        data-guest="{H(r.ReservationName.ToLowerInvariant())}"
                        data-checkin="{r.CheckInDate:yyyy-MM-dd}"
                        data-checkout="{r.CheckOutDate:yyyy-MM-dd}"
                        data-nights="{r.Nights}"
                        data-guests="{guests}"
                        data-status="{H((r.Status ?? string.Empty).ToLowerInvariant())}">
                      <td><span class="apt-badge">{r.ApartmentNumber}</span></td>
                      <td><a href="/reservations/{r.Id}"><strong>{System.Net.WebUtility.HtmlEncode(r.ReservationName)}</strong></a>
                          {(r.ConfirmationCode != null ? $"<br><small style='color:#aaa'>{r.ConfirmationCode}</small>" : "")}</td>
                      <td>{r.CheckInDate:MMM d, yyyy}</td>
                      <td>{r.CheckOutDate:MMM d, yyyy}</td>
                      <td style="text-align:center">{r.Nights}</td>
                      <td style="text-align:center">{guestStr}</td>
                      <td>{BadgeForRow(r, lang)}</td>
                      <td><a href="/reservations/{r.Id}" class="btn btn-secondary btn-sm">{(isAdmin ? T.Get(lang, "Edit") : T.Get(lang, "View"))}</a></td>
                    </tr>
                    """);
            }
        }

        var aptL = T.Get(lang, "Apt");
        var countSuffix = lang == "ru" ? "" : (rows.Count != 1 ? "s" : "");
        var countLabel  = lang == "ru"
            ? $"{rows.Count} {T.Get(lang, rows.Count == 1 ? "reservation" : "reservations")}"
            : $"{rows.Count} reservation{countSuffix}";

        return $"""
            <!DOCTYPE html>
            <html lang="{lang}">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>{T.Get(lang, "Reservations")} – Casa Rosa Admin</title>
              <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
              <style>{Css}</style>
            </head>
            <body>
              {Header("reservations", lang, isAdmin)}
              <main>
                <div class="page-header">
                  <h1>{T.Get(lang, "Reservations")}</h1>
                  {(isAdmin ? $"""<a href="/reservations/new" class="btn btn-primary" style="margin-left:auto">{T.Get(lang, "+ New Reservation")}</a>""" : "")}
                </div>

                <form method="get" action="/reservations">
                  <div class="filter-bar">
                    <div class="field">
                      <label>{T.Get(lang, "Apartment")}</label>
                      {Sel("apt", apt.ToString(),
                          ("0", T.Get(lang, "All")), ("1", $"{aptL} 1"), ("2", $"{aptL} 2"), ("3", $"{aptL} 3"))}
                    </div>
                    <div class="field">
                      <label>{T.Get(lang, "From")}</label>
                      <input type="date" name="from" value="{from:yyyy-MM-dd}"/>
                    </div>
                    <div class="field">
                      <label>{T.Get(lang, "To")}</label>
                      <input type="date" name="to" value="{to:yyyy-MM-dd}"/>
                    </div>
                    <div class="field">
                      <label>{T.Get(lang, "Status")}</label>
                      {Sel("status", status,
                          ("active",    T.Get(lang, "Active")),
                          ("all",       T.Get(lang, "All")),
                          ("cancelled", T.Get(lang, "Cancelled")),
                          ("archived",  T.Get(lang, "Archived")))}
                    </div>
                    <div class="field">
                      <label>{T.Get(lang, "Guest")}</label>
                      <input type="text" id="guest-filter" placeholder="{T.Get(lang, "Search name…")}"
                             style="width:160px" autocomplete="off"/>
                    </div>
                    <button class="btn btn-secondary" type="submit">{T.Get(lang, "Filter")}</button>
                  </div>
                </form>

                <div class="card" style="padding:0; overflow:hidden">
                  <table class="res-table">
                    <thead>
                      <tr>
                        <th data-col="apt">{T.Get(lang, "Apt")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="guest">{T.Get(lang, "Guest")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="checkin">{T.Get(lang, "Check-in")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="checkout">{T.Get(lang, "Check-out")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="nights" style="text-align:center">{T.Get(lang, "Nights")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="guests" style="text-align:center">{T.Get(lang, "Guests")}<span class="sort-arrow"> ↕</span></th>
                        <th data-col="status">{T.Get(lang, "Status")}<span class="sort-arrow"> ↕</span></th>
                        <th></th>
                      </tr>
                    </thead>
                    <tbody>{rowsHtml}</tbody>
                  </table>
                </div>
                <p style="font-size:.8rem;color:#aaa;text-align:right">{countLabel}</p>
              </main>
              {ListScript()}
              {Footer()}
            </body>
            </html>
            """;
    }

    // ── New reservation form ──────────────────────────────────────────────────
    public static string NewForm(string lang = "en", bool isAdmin = false) => $"""
        <!DOCTYPE html>
        <html lang="{lang}">
        <head>
          <meta charset="utf-8"/>
          <meta name="viewport" content="width=device-width, initial-scale=1"/>
          <title>{T.Get(lang, "New Reservation")} – Casa Rosa Admin</title>
          <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
          <style>{Css}</style>
        </head>
        <body>
          {Header("reservations", lang, isAdmin)}
          <main>
            <div class="breadcrumb"><a href="/reservations">{T.Get(lang, "← Reservations")}</a></div>
            <div class="page-header"><h1>{T.Get(lang, "New Reservation")}</h1></div>
            <div class="card">
              <form method="post" action="/reservations/new">
                <div class="grid2">
                  <div>
                    <p class="section-title">{T.Get(lang, "Stay Details")}</p>
                    <div class="field">
                      <label>{T.Get(lang, "Apartment")}</label>
                      {Sel("apartment", "1",
                          ("1", T.Get(lang, "Apartment 1")),
                          ("2", T.Get(lang, "Apartment 2")),
                          ("3", T.Get(lang, "Apartment 3")))}
                    </div>
                    {ListingDisplay(1, lang)}
                    <div class="grid2">
                      <div class="field"><label>{T.Get(lang, "Check-in")}</label><input type="date" name="checkin" required/></div>
                      <div class="field"><label>{T.Get(lang, "Check-out")}</label><input type="date" name="checkout" required/></div>
                    </div>
                    <div class="field"><label>{T.Get(lang, "Status")}</label>
                      <input type="text" name="status" value="confirmed" list="status-opts"/>
                      <datalist id="status-opts">
                        <option value="confirmed"/><option value="cancelled_by_guest"/>
                        <option value="cancelled_by_host"/><option value="completed"/>
                      </datalist>
                    </div>
                    <div class="field"><label>{T.Get(lang, "Confirmation Code")}</label>
                      <input type="text" name="code" placeholder="{T.Get(lang, "e.g. HMY8BMNFN8")}"/></div>
                  </div>
                  <div>
                    <p class="section-title">{T.Get(lang, "Guest Info")}</p>
                    <div class="field"><label>{T.Get(lang, "Guest Name")}</label>
                      <input type="text" name="name" required placeholder="{T.Get(lang, "Full name")}"/></div>
                    <div class="grid3">
                      <div class="field"><label>{T.Get(lang, "Adults")}</label><input type="number" name="adults" value="1" min="0"/></div>
                      <div class="field"><label>{T.Get(lang, "Children")}</label><input type="number" name="children" value="0" min="0"/></div>
                      <div class="field"><label>{T.Get(lang, "Infants")}</label><input type="number" name="infants" value="0" min="0"/></div>
                    </div>
                  </div>
                </div>
                <div style="margin-top:1rem;display:flex;gap:.75rem">
                  <button class="btn btn-primary" type="submit">{T.Get(lang, "Create Reservation")}</button>
                  <a href="/reservations" class="btn btn-secondary">{T.Get(lang, "Cancel")}</a>
                </div>
              </form>
            </div>
          </main>
          {ListingScript()}
          {Footer()}
        </body>
        </html>
        """;

    // ── Reservation detail ────────────────────────────────────────────────────
    public static string Detail(
        ReservationDetail res,
        RegistrationDetail? reg,
        List<GuestRow> guests,
        string message = "",
        bool isAdmin = false,
        bool showFinancial = true,
        string lang = "en")
    {
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Status badge
        string statusBadge;
        if (res.Archived) statusBadge = $"<span class='badge badge-gray'>{T.Get(lang, "Archived")}</span>";
        else if (res.Status?.Contains("ancel", StringComparison.OrdinalIgnoreCase) == true)
            statusBadge = $"<span class='badge badge-red'>{T.Get(lang, "Cancelled")}</span>";
        else if (res.CheckInDate == today)  statusBadge = $"<span class='badge badge-green'>{T.Get(lang, "Checking in today")}</span>";
        else if (res.CheckOutDate == today) statusBadge = $"<span class='badge badge-orange'>{T.Get(lang, "Checking out today")}</span>";
        else if (res.CheckInDate < today && res.CheckOutDate > today)
            statusBadge = $"<span class='badge badge-blue'>{T.Get(lang, "Currently hosting")}</span>";
        else if (res.CheckOutDate < today)  statusBadge = $"<span class='badge badge-gray'>{T.Get(lang, "Completed")}</span>";
        else statusBadge = $"<span class='badge badge-green'>{T.Get(lang, "Confirmed")}</span>";

        var okMsg = string.IsNullOrEmpty(message) ? "" : $"<div class='ok-msg'>✓ {message}</div>";

        // Registration section
        string regSection;
        if (reg == null)
        {
            regSection = $"""
                <div class="card">
                  <p class="section-title">{T.Get(lang, "Registration")}</p>
                  <p style="color:#aaa;font-size:.9rem;margin:.5rem 0">{T.Get(lang, "No registration form has been filled out yet.")}</p>
                  {(isAdmin ? $"<form method=\"post\" action=\"/reservations/{res.Id}/registration\"><input type=\"hidden\" name=\"_action\" value=\"create\"/><button class=\"btn btn-secondary\" type=\"submit\">{T.Get(lang, "+ Create Registration")}</button></form>" : "")}
                </div>
                """;
        }
        else
        {
            regSection = $"""
                <div class="card">
                  <p class="section-title">{T.Get(lang, "Registration")}</p>
                  <form method="post" action="/reservations/{res.Id}/registration">
                    <input type="hidden" name="_action" value="update"/>
                    <input type="hidden" name="regId" value="{reg.Id}"/>
                    <div class="grid2">
                      <div>
                        <div class="field"><label>{T.Get(lang, "Email")}</label>
                          <input type="email" name="email" value="{H(reg.Email)}"/></div>
                        <div class="field"><label>{T.Get(lang, "Arrival Method")}</label>
                          <input type="text" name="arrivalMethod" value="{H(reg.ArrivalMethod)}" list="arr-method-opts"/>
                          <datalist id="arr-method-opts">
                            <option value="Self check-in"/><option value="Airport transfer"/>
                            <option value="Rental car"/><option value="Train"/><option value="Not sure"/>
                          </datalist>
                        </div>
                        <div class="grid2">
                          <div class="field"><label>{T.Get(lang, "Arrival Time")}</label>
                            <input type="text" name="arrivalTime" value="{H(reg.ArrivalTime)}"
                                   placeholder="{T.Get(lang, "e.g. 3PM-4PM")}" list="arr-time-opts"/>
                            <datalist id="arr-time-opts">
                              <option value="Before 3PM"/><option value="3PM-4PM"/>
                              <option value="4PM-5PM"/><option value="5PM-6PM"/>
                              <option value="6PM-7PM"/><option value="After 7PM"/>
                            </datalist>
                          </div>
                          <div class="field"><label>{T.Get(lang, "Flight Number")}</label>
                            <input type="text" name="flight" value="{H(reg.FlightNumber)}" placeholder="TP123"/></div>
                        </div>
                        <div class="field"><label>{T.Get(lang, "Arrival Notes")}</label>
                          <textarea name="arrivalNotes">{H(reg.ArrivalNotes)}</textarea></div>
                      </div>
                      <div>
                        <div class="field">
                          <label>{T.Get(lang, "Special Requests")}</label>
                          <div class="check-row">
                            {Chk(reg.EarlyCheckIn, "earlyCI",  T.Get(lang, "Early check-in"))}
                            {Chk(reg.Crib,         "crib",     T.Get(lang, "Crib"))}
                            {Chk(reg.Sofa,         "sofa",     T.Get(lang, "Sofa bed"))}
                          </div>
                        </div>
                        <div class="field"><label>{T.Get(lang, "Other Requests")}</label>
                          <textarea name="otherRequests">{H(reg.OtherRequests)}</textarea></div>
                        <details style="margin-top:.5rem">
                          <summary>{T.Get(lang, "Invoice Details")}</summary>
                          <div style="margin-top:.8rem">
                            <div class="grid2">
                              <div class="field"><label>{T.Get(lang, "NIF")}</label>
                                <input type="text" name="invoiceNif" value="{H(reg.InvoiceNif)}"/></div>
                              <div class="field"><label>{T.Get(lang, "Name")}</label>
                                <input type="text" name="invoiceName" value="{H(reg.InvoiceName)}"/></div>
                            </div>
                            <div class="field"><label>{T.Get(lang, "Address")}</label>
                              <input type="text" name="invoiceAddr" value="{H(reg.InvoiceAddr)}"/></div>
                            <div class="field"><label>{T.Get(lang, "Invoice Email")}</label>
                              <input type="email" name="invoiceEmail" value="{H(reg.InvoiceEmail)}"/></div>
                          </div>
                        </details>
                      </div>
                    </div>
                    {(isAdmin ? $"""<button class="btn btn-primary" style="margin-top:.5rem" type="submit">{T.Get(lang, "Save Registration")}</button>""" : "")}
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
                    {(isAdmin ? $"<form method=\"post\" action=\"/reservations/{res.Id}/guests/{g.Id}/delete\" style=\"margin:0\"><button class=\"btn btn-danger btn-sm\" type=\"submit\" onclick=\"return confirm('{T.Get(lang, "Remove this guest?")}')\">{T.Get(lang, "Remove")}</button></form>" : "")}</td>
                </tr>
                """);
        }

        var addGuestForm = reg != null && isAdmin ? $"""
            <tr class="add-guest-row">
              <td colspan="4">
                <form method="post" action="/reservations/{res.Id}/guests"
                      style="display:flex;gap:.5rem;align-items:flex-end;flex-wrap:wrap">
                  <input type="hidden" name="registrationId" value="{reg.Id}"/>
                  <div class="field" style="margin:0;flex:2;min-width:120px">
                    <label>{T.Get(lang, "Name")}</label><input type="text" name="guestName" placeholder="{T.Get(lang, "Full name")}"/>
                  </div>
                  <div class="field" style="margin:0;flex:1;min-width:100px">
                    <label>{T.Get(lang, "Nationality")}</label><input type="text" name="guestNat" placeholder="{T.Get(lang, "Country")}"/>
                  </div>
                  <div class="field" style="margin:0;flex:1;min-width:120px">
                    <label>{T.Get(lang, "Date of Birth")}</label><input type="date" name="guestDob"/>
                  </div>
                  <button class="btn btn-secondary" type="submit">{T.Get(lang, "Add Guest")}</button>
                </form>
              </td>
            </tr>
            """ : $"""<tr><td colspan="4" style="color:#aaa;font-size:.85rem;padding:.6rem">{T.Get(lang, "Create a registration first to add guests.")}</td></tr>""";

        var guestsSection = $"""
            <div class="card">
              <p class="section-title">{T.Get(lang, "Guests")} ({guests.Count})</p>
              <table class="guest-table">
                <thead><tr><th>{T.Get(lang, "Name")}</th><th>{T.Get(lang, "Nationality")}</th><th>{T.Get(lang, "Age")}</th><th></th></tr></thead>
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
            <html lang="{lang}">
            <head>
              <meta charset="utf-8"/>
              <meta name="viewport" content="width=device-width, initial-scale=1"/>
              <title>{System.Net.WebUtility.HtmlEncode(res.ReservationName)} – Casa Rosa Admin</title>
              <link rel="icon" href="/favicon.svg" type="image/svg+xml"/>
              <style>{Css}</style>
            </head>
            <body>
              {Header("reservations", lang, isAdmin)}
              <main>
                <div class="breadcrumb"><a href="/reservations">{T.Get(lang, "← Reservations")}</a></div>
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
                        <p class="section-title">{T.Get(lang, "Stay Details")}</p>
                        <div class="field"><label>{T.Get(lang, "Apartment")}</label>
                          {Sel("apartment", res.ApartmentNumber.ToString(),
                              ("1", T.Get(lang, "Apartment 1")),
                              ("2", T.Get(lang, "Apartment 2")),
                              ("3", T.Get(lang, "Apartment 3")))}
                        </div>
                        {ListingDisplay(res.ApartmentNumber, lang)}
                        <div class="grid2">
                          <div class="field"><label>{T.Get(lang, "Check-in")}</label>
                            <input type="date" name="checkin" value="{res.CheckInDate:yyyy-MM-dd}"/></div>
                          <div class="field"><label>{T.Get(lang, "Check-out")}</label>
                            <input type="date" name="checkout" value="{res.CheckOutDate:yyyy-MM-dd}"/></div>
                        </div>
                        <div class="field"><label>{T.Get(lang, "Status")}</label>
                          <input type="text" name="status" value="{H(res.Status)}" list="status-opts"/>
                          <datalist id="status-opts">
                            <option value="confirmed"/><option value="cancelled_by_guest"/>
                            <option value="cancelled_by_host"/><option value="completed"/>
                          </datalist>
                        </div>
                        <div class="field"><label>{T.Get(lang, "Confirmation Code")}</label>
                          <input type="text" name="code" value="{H(res.ConfirmationCode)}"/></div>
                        {(showFinancial ? $"""
                        <div class="grid3">
                          <div class="field"><label>{T.Get(lang, "Payout (€)")}</label>
                            <input type="number" step=".01" name="payout" id="payout" value="{res.Payout:F2}"
                                   oninput="calcRate()"/></div>
                          <div class="field"><label>{T.Get(lang, "Nightly Rate (€)")}</label>
                            <input type="number" step=".01" name="rate" id="rate" value="{res.NightlyRate:F2}"
                                   style="background:#f5f5f5" readonly title="Calculated from payout ÷ nights"/></div>
                          <div class="field"><label>{T.Get(lang, "Cleaning Fee (€)")}</label>
                            <input type="number" step=".01" name="cleaning" value="{res.CleaningFee:F2}"/></div>
                        </div>
                        """ : "<input type='hidden' name='payout' value='0'/><input type='hidden' name='rate' value='0'/><input type='hidden' name='cleaning' value='0'/>")}
                        <div class="field">
                          <label>{T.Get(lang, "Flags")}</label>
                          <div class="check-row">
                            {Chk(res.Enabled,  "enabled",  T.Get(lang, "Enabled"))}
                            {Chk(res.Archived, "archived", T.Get(lang, "Archived"))}
                            {Chk(res.Private,  "private",  T.Get(lang, "Private"))}
                          </div>
                        </div>
                      </div>
                      <div>
                        <p class="section-title">{T.Get(lang, "Guest Info")}</p>
                        <div class="field"><label>{T.Get(lang, "Guest Name")}</label>
                          <input type="text" name="name" value="{H(res.ReservationName)}"/></div>
                        <div class="grid2">
                          <div class="field"><label>{T.Get(lang, "Phone")}</label>
                            <input type="tel" name="phone" value="{H(res.PhoneNumber)}"/></div>
                          <div class="field"><label>{T.Get(lang, "Lives In")}</label>
                            <input type="text" name="livesIn" value="{H(res.LivesIn)}" placeholder="{T.Get(lang, "Country")}"/></div>
                        </div>
                        <div class="grid3">
                          <div class="field"><label>{T.Get(lang, "Adults")}</label>
                            <input type="number" name="adults" min="0" value="{res.Adults}"/></div>
                          <div class="field"><label>{T.Get(lang, "Children")}</label>
                            <input type="number" name="children" min="0" value="{res.Children}"/></div>
                          <div class="field"><label>{T.Get(lang, "Infants")}</label>
                            <input type="number" name="infants" min="0" value="{res.Infants}"/></div>
                        </div>
                        <div style="margin-top:.5rem;padding:.8rem;background:#f9f9f9;border-radius:6px;font-size:.85rem">
                          <strong id="nights-display">{res.Nights}</strong> {T.Get(lang, res.Nights != 1 ? "nights" : "night")}
                          {(showFinancial ? $" &nbsp;·&nbsp; {T.Get(lang, "Guest paid")}: <strong>€{res.GuestPaid:F2}</strong>" : "")}
                        </div>
                      </div>
                    </div>
                    {(isAdmin ? $"""<button class="btn btn-primary" style="margin-top:1rem" type="submit">{T.Get(lang, "Save Reservation")}</button>""" : "")}
                  </form>
                </div>

                {regSection}
                {guestsSection}
              </main>
              {CalcRateScript()}
              {ListingScript()}
              {Footer()}
            </body>
            </html>
            """;
    }

    static string CalcRateScript() => """
        <script>
          function calcRate() {
            var ci  = document.querySelector('[name=checkin]').value;
            var co  = document.querySelector('[name=checkout]').value;
            var pay = parseFloat(document.getElementById('payout').value) || 0;
            if (ci && co) {
              var n = Math.round((new Date(co) - new Date(ci)) / 86400000);
              if (n > 0) {
                document.getElementById('rate').value = (pay / n).toFixed(2);
                var el = document.getElementById('nights-display');
                if (el) el.textContent = n;
              }
            }
          }
          document.querySelector('[name=checkin]').addEventListener('change', calcRate);
          document.querySelector('[name=checkout]').addEventListener('change', calcRate);
        </script>
        """;

    static string ListScript() => """
        <script>
          // ── Sorting ──────────────────────────────────────────────────────────
          var sortCol = null, sortAsc = true;

          document.querySelectorAll('th[data-col]').forEach(function(th) {
            th.style.cursor = 'pointer';
            th.title = 'Click to sort';
            th.addEventListener('click', function() {
              var col = this.getAttribute('data-col');
              if (sortCol === col) { sortAsc = !sortAsc; }
              else { sortCol = col; sortAsc = true; }
              sortTable(col, sortAsc);
              document.querySelectorAll('th[data-col] .sort-arrow').forEach(function(s) {
                s.textContent = s.closest('th').getAttribute('data-col') === col
                  ? (sortAsc ? ' ↑' : ' ↓') : ' ↕';
              });
            });
          });

          function sortTable(col, asc) {
            var tbody = document.querySelector('.res-table tbody');
            var rows = Array.from(tbody.querySelectorAll('tr[data-' + col + ']'));
            rows.sort(function(a, b) {
              var av = a.getAttribute('data-' + col);
              var bv = b.getAttribute('data-' + col);
              var n = isNaN(av) || isNaN(bv) ? av.localeCompare(bv) : parseFloat(av) - parseFloat(bv);
              return asc ? n : -n;
            });
            rows.forEach(function(r) { tbody.appendChild(r); });
          }

          // ── Guest name filter ─────────────────────────────────────────────
          var guestFilter = document.getElementById('guest-filter');
          if (guestFilter) {
            guestFilter.addEventListener('input', function() {
              var val = this.value.toLowerCase().trim();
              document.querySelectorAll('.res-table tbody tr[data-guest]').forEach(function(tr) {
                tr.style.display = val === '' || tr.getAttribute('data-guest').includes(val) ? '' : 'none';
              });
            });
          }
        </script>
        """;

    static string ListingScript() => """
        <script>
          var listingNames = {
            '1': 'Cozy, modern, steps to main square, 2mins to beach',
            '2': 'Historic center, artistic under 2min walk to beach',
            '3': 'Oceanview studio with 20m2 terrace, historic center'
          };
          var aptSel = document.querySelector('[name=apartment]');
          if (aptSel) {
            aptSel.addEventListener('change', function() {
              var el = document.getElementById('listing-name');
              if (el) el.textContent = listingNames[this.value] || '';
            });
          }
        </script>
        """;

    // HTML-encode helper
    static string H(string? s) => s == null ? "" : System.Net.WebUtility.HtmlEncode(s);
}
