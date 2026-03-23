# CasaRosaServices

A pair of .NET 9 services for Airbnb host automation at Casa Rosa. **CR_EmailChecker** monitors a Gmail inbox via IMAP and exposes an HTTP API for querying email events and managing webhook subscribers. **CR_NotificationService** subscribes to CR_EmailChecker and acts on guest message notifications.

---

## Repository Structure

```
CasaRosaServices/
  ├── EmailChecker.sln
  ├── docker-compose.yml
  ├── .env                        # credentials (gitignored)
  ├── build.bat                   # build both Docker images
  ├── push.bat                    # push both images to Docker Hub
  ├── r.bat                       # run CR_EmailChecker via docker compose
  ├── CR_EmailChecker/
  │   ├── CR_EmailChecker.csproj
  │   ├── Dockerfile
  │   ├── appsettings.json        # local dev config
  │   ├── appsettings.docker.json # baked into Docker image (credentials empty)
  │   ├── Program.cs
  │   ├── Config.cs
  │   ├── ImapService.cs
  │   ├── PollingService.cs
  │   ├── ApiServer.cs
  │   ├── WebhookSender.cs
  │   ├── SubscriberRegistry.cs
  │   └── AirbnbSubjectParser.cs
  ├── CR_NotificationService/
  │   ├── CR_NotificationService.csproj
  │   ├── Dockerfile
  │   ├── appsettings.json
  │   ├── appsettings.docker.json
  │   └── Program.cs
  └── CR_Telegram/
      ├── Program.cs
      ├── TelegramSender.csproj
      ├── Dockerfile
      ├── 02_ingest.py
      ├── translate.py
      ├── casa_rosa_qa_final.json
      └── HANDOFF.md
```

---

## CR_EmailChecker

### What It Does

Runs two background threads:

1. **Polling thread** — connects to Gmail over IMAP on a configurable interval, scans recent emails, and detects whether any Airbnb guest message has arrived during an active reservation window. When a new guest message is detected (state flips from none → pending), it fires a webhook to all registered subscribers.
2. **HTTP API thread** — listens for incoming HTTP requests to query email status, retrieve 2FA codes, and manage webhook subscriptions.

All API endpoints require `?Token=<HTTP_AUTH_TOKEN>` or they return `404`.

---

### HTTP API

Listens on port **5000** inside the container (mapped to host port **8237** in Docker).

#### `GET /GetStatus?Token=`

Returns the cached result of the last poll without triggering a new one.

| Response | Meaning |
|---|---|
| `200 OK` | Airbnb guest messages are pending |
| `204 No Content` | No pending messages |

---

#### `GET /AirbnbSMSPin?Token=`

Scans recent emails for a Google Voice SMS-to-email containing an Airbnb verification code.

Looks for `"Airbnb verification code is XXXXXX"` in the email body (sender must contain `"voice"`). Returns the 6-digit code as plain text, or empty string if not found.

---

#### `GET /AirbnbEmailPin?Token=`

Scans recent emails for an Airbnb email with subject `"Your security code is XXXXXX"`. Returns the 6-digit code as plain text, or empty if not found.

---

#### `GET /AirbnbMessages?Token=`

The main endpoint for guest message detection. Behavior depends on polling state:

- **Slow mode** (default, 60 min interval) — signals the poller to run immediately, drops the interval to 1 minute, waits up to `ImmediateRefreshTimeoutSeconds` for the result, then returns.
- **Fast mode** (1 min interval, already active) — returns the cached result immediately.

| Response | Meaning |
|---|---|
| `200 OK` | A current guest message is pending (rate-limited to once per `ResetToDefaultMinutes` window) |
| `204 No Content` | No current guest message |
| `504 Gateway Timeout` | Refresh did not complete within the timeout |

After `ResetToDefaultMinutes` of no `/AirbnbMessages` calls, the polling interval resets back to the default slow rate.

---

#### `POST /subscribe?Token=`

Registers a webhook consumer. Body: `{ "url": "https://...", "token": "secret" }`.

CR_EmailChecker will `POST` to `url` with `X-Webhook-Token: secret` whenever a guest message is detected. Re-registering the same URL updates its token. A subscriber is automatically removed after **3 consecutive delivery failures** and must re-register.

| Response | Meaning |
|---|---|
| `200 OK` | Subscriber registered |
| `400 Bad Request` | Missing or invalid `url` field |

---

#### `DELETE /subscribe?Token=&url=`

Unregisters a webhook consumer by URL.

| Response | Meaning |
|---|---|
| `200 OK` | Subscriber removed |
| `400 Bad Request` | Missing `url` query parameter |

---

### Webhook Payload

```json
{
  "event": "GuestMessagePending",
  "timestamp": "2025-10-26T14:00:00Z"
}
```

The webhook fires only on the **false → true edge** — once per detection event, not on every poll.

---

### Guest Message Detection Logic

On each poll:

1. Sends `NOOP` to Gmail to refresh the inbox.
2. Scans the last `MaxMessagesToScan` (5) emails received within `LookbackMinutes`.
3. For each email, checks if the subject contains `"Reservation for "`.
4. Parses the date range from the subject using `AirbnbDateParser`.
5. If today falls between check-in and check-out, the message is treated as a current guest message.

#### Date Parsing

`AirbnbDateParser` handles formats like:

- `Oct 26 – Nov 2`
- `October 26–November 2, 2025`
- `Oct 26 - Nov 2` (any dash variant including Unicode en/em dashes)

Normalizes Unicode dashes and non-breaking spaces before parsing. Infers year from context; handles stays that cross a year boundary.

---

### CR_EmailChecker Configuration

Loaded from `appsettings.json`, overridden by environment variables (`__` as section separator).

```json
{
  "Http": {
    "Prefixes": [ "http://+:5000/" ],
    "AuthToken": ""
  },
  "Imap": {
    "Scheme": "imaps",
    "Host": "imap.gmail.com",
    "Port": 993,
    "CheckCertificateRevocation": false,
    "Username": "",
    "Password": "",
    "UseOAuth2": false
  },
  "Airbnb": {
    "ResetToDefaultMinutes": 10
  },
  "Polling": {
    "DefaultIntervalMinutes": 60,
    "ImmediateRefreshTimeoutSeconds": 30,
    "LookbackMinutes": 10,
    "MaxMessagesToScan": 5
  }
}
```

| Key | Default | Description |
|---|---|---|
| `Http:Prefixes` | `["http://+:5000/"]` | HTTP listener prefixes. `+` binds all interfaces. |
| `Http:AuthToken` | _(required)_ | Shared secret for all API requests (`?Token=`). |
| `Imap:Scheme` | `imaps` | Connection scheme (`imaps` for TLS). |
| `Imap:Host` | `imap.gmail.com` | IMAP server hostname. |
| `Imap:Port` | `993` | IMAP port. |
| `Imap:CheckCertificateRevocation` | `false` | Whether to check TLS certificate revocation. |
| `Imap:Username` | _(required)_ | Gmail address. |
| `Imap:Password` | _(required)_ | Gmail App Password (not your Google account password). |
| `Imap:UseOAuth2` | `false` | Use OAuth2 instead of password auth. |
| `Airbnb:ResetToDefaultMinutes` | `10` | Inactivity window before polling returns to slow mode. Also the minimum time between `200` responses on `/AirbnbMessages`. |
| `Polling:DefaultIntervalMinutes` | `60` | Slow-mode poll interval. |
| `Polling:ImmediateRefreshTimeoutSeconds` | `30` | Max wait for a forced refresh before returning `504`. |
| `Polling:LookbackMinutes` | `10` | Only consider emails newer than this. |
| `Polling:MaxMessagesToScan` | `5` | Number of most-recent emails to inspect per poll. |

---

## CR_NotificationService

Receives webhook events from CR_EmailChecker and acts on them.

On startup it automatically registers itself with CR_EmailChecker via `POST /subscribe`, with exponential back-off retry (up to 5 attempts: 2s, 4s, 8s, 16s, 32s). If CR_EmailChecker removes it after 3 consecutive failures, it re-registers on its next restart.

Add notification logic (SMS, push, Slack, smart home, etc.) inside `HandleNotificationAsync` in [CR_NotificationService/Program.cs](CR_NotificationService/Program.cs).

### Webhook Endpoint

#### `POST /notify`

Receives a `GuestMessagePending` event from CR_EmailChecker. Requires `X-Webhook-Token` header matching `Webhook:Token`.

| Response | Meaning |
|---|---|
| `200 OK` | Event received and handled |
| `404 Not Found` | Token mismatch |

---

### CR_NotificationService Configuration

```json
{
  "EmailChecker": {
    "BaseUrl": "http://localhost:8237",
    "ApiToken": "a812"
  },
  "Webhook": {
    "SelfUrl": "http://localhost:8238/notify",
    "Token": "wh_secret_42"
  }
}
```

| Key | Description |
|---|---|
| `EmailChecker:BaseUrl` | URL of CR_EmailChecker to register with on startup. |
| `EmailChecker:ApiToken` | API token for CR_EmailChecker's `/subscribe` endpoint. |
| `Webhook:SelfUrl` | This service's own `/notify` URL — what CR_EmailChecker will POST to. |
| `Webhook:Token` | Secret CR_EmailChecker must include as `X-Webhook-Token`. |

---

## Credentials

Stored in `.env` at the repo root (gitignored). Used by Docker Compose.

```
IMAP_USERNAME=casarosahouse@gmail.com
IMAP_PASSWORD=dzpq xidy uosf qdmf
HTTP_AUTH_TOKEN=a812
WEBHOOK_TOKEN=wh_secret_42
```

- `IMAP_PASSWORD` is a **Gmail App Password** (16-character). Generate one at: Google Account > Security > 2-Step Verification > App passwords.
- `HTTP_AUTH_TOKEN` (`a812`) protects all CR_EmailChecker API endpoints including `/subscribe`.
- `WEBHOOK_TOKEN` (`wh_secret_42`) is the shared secret between CR_EmailChecker and CR_NotificationService for webhook delivery.

---

## Running with Docker Compose

```bash
docker compose up -d
```

Both services start together. CR_NotificationService waits for CR_EmailChecker (`depends_on`), then registers its webhook on startup.

```yaml
services:
  cr_emailchecker:
    image: cesarpenafiel2/cr_emailchecker:latest
    ports:
      - "8237:5000"

  cr_notificationservice:
    image: cesarpenafiel2/cr_notificationservice:latest
    ports:
      - "8238:5001"
    depends_on:
      - cr_emailchecker
```

### Example API calls

```bash
# Check if a current guest has a pending message
curl "http://localhost:8237/AirbnbMessages?Token=a812"

# Get the Airbnb SMS 2FA pin
curl "http://localhost:8237/AirbnbSMSPin?Token=a812"

# Get the Airbnb email 2FA pin
curl "http://localhost:8237/AirbnbEmailPin?Token=a812"

# Get cached status without triggering a refresh
curl "http://localhost:8237/GetStatus?Token=a812"

# Manually register a webhook consumer
curl -X POST "http://localhost:8237/subscribe?Token=a812" \
  -H "Content-Type: application/json" \
  -d '{"url":"http://myservice/notify","token":"mysecret"}'

# Unregister a webhook consumer
curl -X DELETE "http://localhost:8237/subscribe?Token=a812&url=http://myservice/notify"
```

---

## Building and Pushing Docker Images

```bash
build.bat   # builds cesarpenafiel2/cr_emailchecker:latest and cesarpenafiel2/cr_notificationservice:latest
push.bat    # pushes both images to Docker Hub
```

Each Dockerfile copies `appsettings.docker.json` over `appsettings.json` at build time so the image ships with empty credentials, which are injected at runtime via environment variables.

---

## Building Locally

```bash
cd CR_EmailChecker && dotnet run
cd CR_NotificationService && dotnet run
```

Each project reads its own `appsettings.json` for local config.

---

## IMAP Connection Management

CR_EmailChecker maintains a single persistent `ImapClient` (MailKit). On each poll:

1. `EnsureConnected()` checks if the client is connected and authenticated; reconnects if not, opening the inbox read-only.
2. `Refresh()` sends a `NOOP` to prompt the server to flush new message counts.
3. On any exception during message reading, the client is disposed and recreated for the next attempt.

The main thread sleeps indefinitely (`Thread.Sleep(Timeout.Infinite)`) to keep the process alive in Docker where there is no attached console.

---

## Adding a New Notification Consumer

Any service can subscribe without modifying CR_EmailChecker. On startup, `POST /subscribe`:

```http
POST http://cr_emailchecker:5000/subscribe?Token=a812
Content-Type: application/json

{ "url": "http://myservice/notify", "token": "mysecret" }
```

CR_EmailChecker will fan out to all registered subscribers concurrently when a guest message is detected. A subscriber is auto-removed after 3 consecutive delivery failures and must re-register on restart.

---

## CR_Telegram

A C# .NET 8 Telegram bot serving as an AI-powered assistant for **Casa Rosa**, an Airbnb property in Cascais, Portugal. It handles 4 Telegram groups.

---

### Telegram Groups

| Group | Chat ID | Purpose |
|---|---|---|
| Russian (helper) | `-5186091931` | Anastasia's messages auto-translated to English and forwarded to the English group |
| English (owner) | `-5129864639` | Cesar's messages auto-translated to Russian and forwarded to the Russian group |
| Translator | `-5209557963` | On-demand translation to RU / PT / ES / PL / HE via inline keyboard buttons; supports voice |
| Airbnb Rapid Response | `-5271439382` | AI-powered guest Q&A using semantic search over 412 real past Airbnb Q&A pairs |

Bot: `@RosaHelper_bot`

---

### Features

#### 1. Russian <-> English Auto-Translation (Groups -5186091931 / -5129864639)

- All text messages from the Russian group are translated to English and forwarded to the English group, and vice versa
- Uses **Claude Haiku** (`claude-haiku-4-5-20251001`) with a rolling 20-message English conversation history for context (pronoun/reference resolution)
- Built-in Casa Rosa property context injected into every translation prompt
- Russian translations always use formal address (Вы/Вам, never ты)
- Voice messages: transcribed via **Google STT**, translated, synthesized via **ElevenLabs**
  - Russian -> English: Anna real voice
  - English -> Russian: Cesar real voice

#### 2. Multi-Language Translator (Group -5209557963)

- Send any text or voice message and receive an inline keyboard prompt:

  ```
  [ Russian ]  [ Portuguese ]  [ Spanish ]
  [ Polish  ]  [   Hebrew   ]
  ```

- Select a language and the bot replies with a translated text message
- If the original was a voice message, the bot replies with a voice translation using **Cesar real voice**

#### 3. Airbnb Rapid Response (Group -5271439382)

- Handles both text and voice messages in any language
- Voice messages are transcribed using Google STT with multi-language auto-detection (English, Hebrew, Russian, Portuguese, Spanish, Polish, French, German, Italian, Dutch, Arabic)
- Semantic search against a knowledge base of 412 real past host Q&A pairs using **OpenAI embeddings** (cosine similarity in-memory)
- Top 5 matching Q&A pairs passed as context to **Claude Sonnet** (`claude-sonnet-4-6`) to generate a warm, concise reply
- Conversation history (last 20 turns) maintained per chat session
- All interactions logged to `ConversationLog` table in MSSQL

##### Language Mode

- Default reply language: **English**
- To switch: send a voice or text message saying `Language <name>` (e.g. `Language Hebrew`, `Language Russian`)
- The bot confirms the switch and all subsequent replies (text and voice) will be in that language only
- Nothing other than the `Language` command can change the reply language

---

### Key Tech Stack

| Service | Usage |
|---|---|
| Telegram Bot API | Message polling (long polling / GetUpdates) |
| Anthropic Claude Haiku | Russian<->English translation with conversation history context |
| Anthropic Claude Sonnet | Airbnb guest reply generation |
| OpenAI text-embedding-3-small | Embeds guest questions for cosine similarity search (1536 dims) |
| Google Cloud Speech-to-Text | Voice transcription (v1 for known language, v1p1beta1 for auto-detect) |
| ElevenLabs TTS | Voice synthesis — "Cesar real voice" and "Anna real voice" |
| MSSQL Server | Stores 412 KB Q&A pairs, their embeddings, and conversation logs |

Deployed as a Docker container on Hetzner (`root@penafiel.org`).

---

### Database

- **Server:** `penafiel.org,2367`
- **Database:** `Telegram_Agent`
- **User:** `Telegram_Agent_RW`

#### Tables

| Table | Purpose |
|---|---|
| `dbo.KnowledgeBase` | 412 Q&A pairs — Question, Answer, Topics, SourceHash |
| `dbo.KnowledgeBase_Embeddings` | float32 embeddings stored as VARBINARY(MAX), 1536 dims (6144 bytes each) |
| `dbo.ConversationLog` | Session logs — SessionId, Role, Message, MatchedKbIds |

#### Knowledge Base

Built from 893 real Airbnb conversation threads (12,194 messages):
1. Cleaned — removed boilerplate, trivial replies
2. Anonymized — 298 guest names replaced with `[GUEST]`
3. Filtered — kept only genuine guest Q&A pairs
4. Translated — all non-English content translated to English via Claude API
5. Final: `casa_rosa_qa_final.json` — 412 pairs

Topic tags: `availability`, `beds_setup`, `bicycle`, `car_rental`, `check_in`, `children`, `directions`, `early_checkin`, `late_checkout`, `lisbon_trips`, `parking`, `payment`, `restaurants`, `tourist_tax`, `transport`, `wifi`

---

### Property Facts (injected into every Airbnb prompt)

- **Address:** Beco dos Inválidos 5, Cascais, Portugal
- **Check-in:** 15:00 (self check-in, keypad code, no physical keys)
- **Check-out:** 11:00
- **Tourist tax:** €4 per adult per night (children under 13 exempt), payable cash or via Airbnb
- **Emergency phone:** +351-912-947-429
- **GPS landmark:** Restaurant "Armazém 22" — pink building right next to it
- **Website:** https://casarosahouse.com
- **Helper:** Anastasia (on-site cleaning/handover)
- **Parking:** Estacionamento Marechal Carmona (€27/day, 8 min walk); free parking ~12 min walk near Cascais Beauty Clinic

---

### Deployment

Hosted on Hetzner server (`root@penafiel.org`) as a Docker container.

```bash
# Build image locally
cd CR_Telegram
docker build -t voice-translator .

# Transfer and run on server
docker save voice-translator | ssh root@penafiel.org "docker load && \
  docker stop voice-translator; docker rm voice-translator; \
  docker run -d --name voice-translator --restart unless-stopped voice-translator"

# Check logs
ssh root@penafiel.org "docker logs voice-translator --tail 20"
```

### Populate KB embeddings (run once, idempotent)

```bash
pip install pyodbc openai numpy
python 02_ingest.py --server "penafiel.org,2367" --openai-key sk-... --test-query
```

---

### Architecture Flow

```
Incoming Telegram message
        |
        +-- Russian group?   --> Translate (Claude Haiku) --> English group
        |
        +-- English group?   --> Translate (Claude Haiku) --> Russian group
        |
        +-- Translator group? --> Inline keyboard --> Translate (Claude Haiku) --> voice or text reply
        |
        +-- Airbnb group?
              |
              +-- "Language <X>"? --> Set reply language (persists until next command)
              |
              +-- Voice? --> Google STT (auto-detect) --> embed question (OpenAI)
              +-- Text?  --> embed question (OpenAI)
                               |
                               v
                     Cosine similarity search (in-memory, 412 vectors)
                               |
                               v
                     Top 5 KB matches --> Claude Sonnet --> reply in language mode
                               |
                               v
                     Voice reply (ElevenLabs Cesar) or text reply --> Airbnb group
```

GitHub: `flyfreely/TelegramAgent` (private)
