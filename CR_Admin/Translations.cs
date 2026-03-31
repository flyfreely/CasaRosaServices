using System.Globalization;

static class T
{
    static readonly CultureInfo _ruCulture = new("ru-RU");
    static readonly CultureInfo _enCulture = new("en-US");

    public static CultureInfo Culture(string lang) => lang == "ru" ? _ruCulture : _enCulture;

    /// Returns the Russian translation of key, or key itself for English (or missing RU entry).
    public static string Get(string lang, string key) =>
        lang == "ru" && _ru.TryGetValue(key, out var v) ? v : key;

    static readonly Dictionary<string, string> _ru = new()
    {
        // ── Header nav ───────────────────────────────────────────────────────────
        ["Settings"]      = "Настройки",
        ["Reservations"]  = "Бронирования",
        ["Calendar"]      = "Календарь",
        ["Admin"]         = "Админ",
        ["Reminders"]     = "Напоминания",
        ["Users"]         = "Пользователи",
        ["Sign out"]      = "Выйти",

        // ── Google login ─────────────────────────────────────────────────────────
        ["Sign in with Google"] = "Войти через Google",
        ["Google sign-in failed. Please try again."]  = "Ошибка входа Google. Попробуйте снова.",
        ["Google account has no email address."]      = "У аккаунта Google нет email.",
        ["Google Email"]                              = "Google Email",
        ["Link"]                                      = "Привязать",

        // ── Login ────────────────────────────────────────────────────────────────
        ["Username"]      = "Имя пользователя",
        ["Password"]      = "Пароль",
        ["Sign in"]       = "Войти",
        ["Invalid username or password."] = "Неверное имя пользователя или пароль.",

        // ── Dashboard ────────────────────────────────────────────────────────────
        ["Auto-Bot – Tomorrow's Briefing"] = "Auto-Bot – Брифинг на завтра",
        ["Auto-Bot – Today's Briefing"]    = "Auto-Bot – Брифинг на сегодня",
        ["Send time (Portugal time)"]      = "Время отправки (Португалия)",
        ["Destination channel"]            = "Канал назначения",
        ["Save"]                           = "Сохранить",
        ["Saved."]                         = "Сохранено.",
        ["Auto-Bot – Triple Cleaning Alert"] = "Auto-Bot – Тройная уборка",
        ["Auto_Bot sends a triple cleaning warning when 3 apartments need cleaning on the same day within the next 7 days."] =
            "Auto_Bot отправляет предупреждение о тройной уборке, когда в течение 7 дней в один день нужна уборка в 3 апартаментах.",
        ["Auto_Bot sends the tomorrow checkout / check-in summary to this channel."] =
            "Auto_Bot отправляет сводку о заездах и выездах на завтра в этот канал.",
        ["Auto_Bot sends the today checkout / check-in summary to this channel."] =
            "Auto_Bot отправляет сводку о заездах и выездах на сегодня в этот канал.",

        // ── Reservation list ─────────────────────────────────────────────────────
        ["+ New Reservation"]  = "+ Новое бронирование",
        ["Apartment"]          = "Апартамент",
        ["From"]               = "С",
        ["To"]                 = "По",
        ["Status"]             = "Статус",
        ["Guest"]              = "Гость",
        ["Filter"]             = "Фильтр",
        ["All"]                = "Все",
        ["Active"]             = "Активные",
        ["Cancelled"]          = "Отменённые",
        ["Archived"]           = "Архивные",
        ["Check-in"]           = "Заезд",
        ["Check-out"]          = "Выезд",
        ["Nights"]             = "Ночи",
        ["Guests"]             = "Гости",
        ["Edit"]               = "Изм.",
        ["View"]               = "Просмотр",
        ["Search name…"]       = "Поиск…",
        ["No reservations found for these filters."] = "Нет бронирований по данным фильтрам.",
        ["Apt"]                = "Апт",

        // ── Status badges ────────────────────────────────────────────────────────
        ["Disabled"]           = "Отключено",
        ["Checked in"]         = "Заехал",
        ["Checking in"]        = "Заезжает",
        ["Checked out"]        = "Выехал",
        ["Checking out"]       = "Выезжает",
        ["Hosting"]            = "Принимаем",
        ["Completed"]          = "Завершено",
        ["Confirmed"]          = "Подтверждено",
        ["Checking in today"]  = "Заезд сегодня",
        ["Checking out today"] = "Выезд сегодня",
        ["Currently hosting"]  = "Сейчас гость",

        // ── Breadcrumbs / page titles ────────────────────────────────────────────
        ["← Reservations"]    = "← Бронирования",
        ["New Reservation"]   = "Новое бронирование",

        // ── Reservation detail – Stay ────────────────────────────────────────────
        ["Stay Details"]      = "Детали проживания",
        ["Guest Info"]        = "Информация о госте",
        ["Apartment 1"]       = "Апартамент 1",
        ["Apartment 2"]       = "Апартамент 2",
        ["Apartment 3"]       = "Апартамент 3",
        ["Listing"]           = "Апартамент",
        ["Confirmation Code"] = "Код подтверждения",
        ["Payout (€)"]        = "Выплата (€)",
        ["Nightly Rate (€)"]  = "Ставка за ночь (€)",
        ["Cleaning Fee (€)"]  = "Уборка (€)",
        ["Flags"]             = "Флаги",
        ["Enabled"]           = "Активно",
        ["Private"]           = "Приватное",
        ["Guest Name"]        = "Имя гостя",
        ["Phone"]             = "Телефон",
        ["Lives In"]          = "Проживает в",
        ["Adults"]            = "Взрослые",
        ["Children"]          = "Дети",
        ["Infants"]           = "Младенцы",
        ["nights"]            = "ночей",
        ["night"]             = "ночь",
        ["Guest paid"]        = "Гость заплатил",
        ["Save Reservation"]  = "Сохранить бронирование",
        ["Country"]           = "Страна",
        ["e.g. HMY8BMNFN8"]  = "напр. HMY8BMNFN8",

        // ── Registration ─────────────────────────────────────────────────────────
        ["Registration"]      = "Регистрация",
        ["No registration form has been filled out yet."] = "Форма регистрации ещё не заполнена.",
        ["+ Create Registration"] = "+ Создать регистрацию",
        ["Email"]             = "Email",
        ["Arrival Method"]    = "Способ прибытия",
        ["Arrival Time"]      = "Время прибытия",
        ["e.g. 3PM-4PM"]      = "напр. 15:00–16:00",
        ["Flight Number"]     = "Номер рейса",
        ["TP123"]             = "TP123",
        ["Arrival Notes"]     = "Заметки о прибытии",
        ["Special Requests"]  = "Особые пожелания",
        ["Early check-in"]    = "Ранний заезд",
        ["Crib"]              = "Кроватка",
        ["Crib needed:"]      = "Нужна кроватка:",
        ["Sofa bed"]          = "Диван-кровать",
        ["Foldable bed"]      = "Раскладная кровать",
        ["Other Requests"]    = "Другие пожелания",
        ["Invoice Details"]   = "Данные для счёта",
        ["NIF"]               = "NIF",
        ["Name"]              = "Имя",
        ["Address"]           = "Адрес",
        ["Invoice Email"]     = "Email для счёта",
        ["Save Registration"] = "Сохранить регистрацию",

        // ── Guests ───────────────────────────────────────────────────────────────
        ["Nationality"]       = "Гражданство",
        ["Age"]               = "Возраст",
        ["Remove"]            = "Удалить",
        ["Add Guest"]         = "Добавить гостя",
        ["Full name"]         = "Полное имя",
        ["Date of Birth"]     = "Дата рождения",
        ["Create a registration first to add guests."] = "Сначала создайте регистрацию для добавления гостей.",
        ["Remove this guest?"] = "Удалить этого гостя?",

        // ── New reservation ───────────────────────────────────────────────────────
        ["Create Reservation"] = "Создать бронирование",
        ["Cancel"]             = "Отмена",

        // ── Calendar ─────────────────────────────────────────────────────────────
        ["Today"]              = "Сегодня",
        ["Tomorrow"]           = "Завтра",
        ["Check-in ↓"]         = "Заезд ↓",
        ["Check-out ↑"]        = "Выезд ↑",
        ["Occupied"]           = "Занято",
        ["Transition Day"]     = "День перехода",
        ["Vacant"]             = "Свободно",
        ["Arrival:"]           = "Прибытие:",
        ["Unknown"]            = "Неизвестно",
        ["Checkout:"]          = "Выезд:",
        ["↑ Checking Out"]     = "↑ Выезжает",
        ["↓ Checking In"]      = "↓ Заезжает",
        ["1st Floor"]          = "1-й этаж",
        ["2nd Floor"]          = "2-й этаж",
        ["3rd Floor"]          = "3-й этаж",
        ["Apt 1 check-in"]     = "Апт 1 заезд",
        ["Apt 1 check-out"]    = "Апт 1 выезд",
        ["Apt 2 check-in"]     = "Апт 2 заезд",
        ["Apt 2 check-out"]    = "Апт 2 выезд",
        ["Apt 3 check-in"]     = "Апт 3 заезд",
        ["Apt 3 check-out"]    = "Апт 3 выезд",
        ["Transition"]         = "Переход",

        // ── Reminders ────────────────────────────────────────────────────────────
        ["Scheduled (Portugal)"] = "Запланировано (Португалия)",
        ["Message"]              = "Сообщение",
        ["Language"]             = "Язык",
        ["Bot"]                  = "Бот",
        ["No pending reminders."] = "Нет ожидающих напоминаний.",
        ["Edit Reminder"]         = "Изменить напоминание",
        ["New Reminder"]          = "Новое напоминание",
        ["Channel"]               = "Канал",

        // ── Users ────────────────────────────────────────────────────────────────
        ["Role"]               = "Роль",
        ["Created"]            = "Создан",
        ["Change"]             = "Изменить",
        ["New password"]       = "Новый пароль",
        ["Confirm"]            = "Подтвердить",
        ["Set"]                = "Установить",
        ["Delete"]             = "Удалить",
        ["Add New User"]       = "Добавить пользователя",
        ["Confirm Password"]   = "Подтвердить пароль",
        ["Add User"]           = "Добавить",
        ["Delete user"]        = "Удалить пользователя",

        // ── Telegram log ─────────────────────────────────────────────────────────
        ["Telegram Log"]    = "Журнал Telegram",
        ["Event"]           = "Событие",
        ["No log entries yet."] = "Записей пока нет.",
        ["Auto-refreshes every 30 seconds"] = "Обновляется каждые 30 секунд",

        // ── Audit log ────────────────────────────────────────────────────────────
        ["Audit Log"]    = "Журнал действий",
        ["Time"]         = "Время",
        ["Actor"]        = "Пользователь",
        ["Action"]       = "Действие",
        ["Detail"]       = "Детали",
        ["No audit entries."] = "Записей нет.",

        // ── Statistics ───────────────────────────────────────────────────────────
        ["Statistics"]            = "Статистика",
        ["This Year"]             = "Этот год",
        ["Total Nights"]          = "Всего ночей",
        ["Total Revenue"]         = "Выручка",
        ["Avg Occupancy"]         = "Ср. заполняемость",
        ["across all apartments"] = "по всем апартаментам",
        ["total payout"]          = "общая выплата",
        ["Nights per Month"]      = "Ночи по месяцам",
        ["Revenue per Month"]     = "Выручка по месяцам",

        // ── Occupancy stats ──────────────────────────────────────────────────────
        ["Monthly Occupancy"]     = "Заполняемость за месяц",
        ["Occupancy"]             = "Заполняемость",
        ["days"]                  = "дней",
        ["occupied"]              = "занято",

        // ── Cleaning dialog ───────────────────────────────────────────────────────
        ["is it clean?"]             = "убрано?",
        ["Yes, it's clean"]          = "Да, убрано",
        ["No, still needs cleaning"] = "Нет, нужна уборка",
        ["Not needed"]               = "Не нужна",

        // ── Error / success messages ──────────────────────────────────────────────
        ["Username and password are required."] = "Имя пользователя и пароль обязательны.",
        ["Passwords do not match."]             = "Пароли не совпадают.",
        ["Cannot remove the last admin."]       = "Нельзя удалить последнего администратора.",
        ["Password cannot be empty."]           = "Пароль не может быть пустым.",
        ["Password updated."]                   = "Пароль обновлён.",
        ["Cannot delete the last user."]        = "Нельзя удалить последнего пользователя.",
        ["User deleted."]                       = "Пользователь удалён.",
        ["Cannot delete the last admin user."]  = "Нельзя удалить последнего администратора.",
    };
}
