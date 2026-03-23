# Casa Rosa — Telegram Agent: Claude Code Handoff

## What This Project Is

An AI-powered Airbnb host assistant for **Casa Rosa**, an apartment in Cascais, Portugal. The app will:
1. Receive guest questions (via Airbnb / Telegram)
2. Search a knowledge base of real past Q&A pairs using vector similarity
3. Generate a suggested reply using Claude API, grounded in past answers

---

## What Has Been Built So Far

### Knowledge Base Pipeline (complete)
Starting from a raw Airbnb conversation export (`AirbnbConversationOutput.json`, 893 threads, 12,194 messages), the following was done in sequence:

1. **Cleaned** — removed boilerplate host templates, trivial acknowledgments, non-Q&A threads → 771 pairs
2. **Anonymized** — 298 unique guest names replaced with `[GUEST]` placeholder
3. **Filtered** — removed host-initiated messages, Airbnb system messages, non-guest-question entries → 412 pairs
4. **Translated** — all non-English content (Spanish, French, Portuguese, German, Italian, Russian, Danish, Japanese, Korean) translated to English via Claude API
5. **Final file**: `casa_rosa_qa_final.json` — 412 Q&A pairs, all English, anonymized

### MSSQL Database (schema created, KB populated)

**Connection details:**
- **Server:** `penafiel.org,2367`
- **Database:** `Telegram_Agent`
- **User:** `Telegram_Agent_RW`
- **Auth:** SQL Server authentication (username + password)
- **Driver:** ODBC Driver 17 for SQL Server

**Example connection string (Python pyodbc):**
```python
conn_str = (
    "DRIVER={ODBC Driver 17 for SQL Server};"
    "SERVER=penafiel.org,2367;"
    "DATABASE=Telegram_Agent;"
    "UID=Telegram_Agent_RW;"
    "PWD=<password>;"
)
```

**Tables:**

| Table | Purpose |
|---|---|
| `dbo.KnowledgeBase` | 412 Q&A pairs — Question, Answer, Topics (comma-separated tags), SourceHash (SHA-256, unique) |
| `dbo.KnowledgeBase_Embeddings` | One row per KB entry — stores raw float32 embedding vector as `VARBINARY(MAX)`, model name, dimensions |
| `dbo.ConversationLog` | Chat session log — SessionId (Telegram chat_id), Role (user/assistant), Message, MatchedKbIds |

**KnowledgeBase schema:**
```sql
Id            INT IDENTITY PK
Question      NVARCHAR(MAX)   -- guest question, anonymized, English
Answer        NVARCHAR(MAX)   -- host answer, English
Topics        NVARCHAR(500)   -- e.g. 'check_in,parking,transport'
SourceHash    CHAR(64)        -- SHA-256(Q+A), unique constraint
CreatedAt     DATETIME2
```

**KnowledgeBase_Embeddings schema:**
```sql
Id              INT IDENTITY PK
KbId            INT FK → KnowledgeBase.Id (CASCADE DELETE)
EmbeddingVector VARBINARY(MAX)  -- float32 little-endian, 1536 dims = 6144 bytes
EmbeddingModel  VARCHAR(100)    -- 'text-embedding-3-small'
Dimensions      INT             -- 1536
CreatedAt       DATETIME2
```

**Status:**
- ✅ `KnowledgeBase` — 412 rows populated (via `03_populate_kb.sql`)
- ⏳ `KnowledgeBase_Embeddings` — **NOT YET POPULATED** — needs `02_ingest.py` to be run with an OpenAI API key to generate and store vectors
- ✅ `ConversationLog` — empty, ready for app use

**Topic tags in use:**
`availability`, `beds_setup`, `bicycle`, `car_rental`, `check_in`, `children`, `directions`, `early_checkin`, `late_checkout`, `lisbon_trips`, `parking`, `payment`, `restaurants`, `tourist_tax`, `transport`, `wifi`

---

## Embeddings Strategy

- **No native vector type** — old MSSQL, so embeddings are stored as `VARBINARY(MAX)` (raw float32 bytes)
- **Model:** `text-embedding-3-small` (OpenAI), 1536 dimensions, ~$0.01 to embed all 412 rows
- **Similarity search:** done in Python/numpy at query time — load all vectors (~2.5MB), compute cosine similarity in memory, return top K
- **What gets embedded:** the Question field only (not Answer), since that's what incoming guest questions are matched against

**Utility functions already written (in `02_ingest.py`):**
```python
floats_to_bytes(floats)    # pack float32 list → bytes for INSERT
bytes_to_floats(b)         # unpack VARBINARY → numpy float32 array
cosine_similarity(a, b)    # numpy dot product similarity
query_kb(conn, question, openai_client, top_k=5, topic_filter=None)
    # → embeds question, loads all vectors, returns top K matches with scores
```

---

## Next Step: Populate Embeddings

Before the app can do semantic search, run:
```bash
pip install pyodbc openai numpy
python 02_ingest.py \
  --server "penafiel.org,2367" \
  --openai-key sk-... \
  --test-query
```

This is idempotent — safe to re-run, skips already-embedded rows.

---

## Scripts Produced

| File | Purpose |
|---|---|
| `01_create_database.sql` | Creates `Telegram_Agent` DB + all 3 tables |
| `02_ingest.py` | Inserts KB rows + generates/stores OpenAI embeddings + `query_kb()` function |
| `03_populate_kb.sql` | 412 plain INSERT statements for KnowledgeBase (already run) |
| `04_create_user.sql` | Creates `Telegram_Agent_RW` SQL login with read/write only |
| `clean_airbnb.py` | Pipeline step 1: clean raw Airbnb JSON → Q&A pairs |
| `anonymize_and_translate.py` | Pipeline step 2: anonymize names + translate via Claude API |
| `translate.py` | Standalone translation script for filtered file |
| `casa_rosa_qa_final.json` | Final 412 Q&A pairs (English, anonymized) |

---

## App Architecture (to be built)

```
Guest message (Telegram / Airbnb)
        ↓
Embed question (OpenAI text-embedding-3-small)
        ↓
Cosine similarity search → top 5 KB matches from MSSQL
        ↓
Claude API (claude-sonnet) with system prompt + top 5 Q&A as context
        ↓
Suggested reply → sent to host for approval or auto-sent
```

**Suggested Claude system prompt pattern:**
```
You are an Airbnb host assistant for Casa Rosa, Cascais, Portugal.
Answer the guest's question based on these past responses from the host:

[top 5 Q&A pairs injected here]

Guest question: {question}
Reply warmly and concisely. If unsure, say the host will follow up.
```

---

## Property Facts (always inject into prompt, separate from RAG)
- **Property:** Casa Rosa, Beco dos Inválidos 5, Cascais, Portugal
- **Check-in:** 15:00 (self check-in, keypad code, no keys)
- **Check-out:** 11:00
- **Tourist tax:** €4 per adult per night (children under 13 exempt), payable in cash or via Airbnb
- **Emergency phone:** +351-912-947-429
- **GPS landmark:** Restaurant "Armazém 22" (house is right next to it, pink)
- **Website:** https://casarosahouse.com
- **Helper:** Anastasia (on-site cleaning/handover)
- **Parking:** Estacionamento Marechal Carmona (€27/day, 8 min walk); free parking ~12 min walk near Cascais Beauty Clinic
