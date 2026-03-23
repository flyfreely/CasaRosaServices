"""
Casa Rosa — Ingest knowledge base into MSSQL + generate embeddings
Requirements:
    pip install pyodbc openai numpy

Usage:
    python 02_ingest.py \
        --server YOUR_SERVER \
        --openai-key sk-... \
        [--driver "ODBC Driver 17 for SQL Server"]

Notes:
    - Embeddings model: text-embedding-3-small (1536 dims, cheapest, great quality)
    - Cost for 412 pairs: ~$0.01
    - Skips rows already in DB (idempotent via SourceHash)
"""

import json, struct, hashlib, time, argparse
import pyodbc
import numpy as np
from openai import OpenAI

# ── Config ────────────────────────────────────────────────────
EMBEDDING_MODEL = 'text-embedding-3-small'
DIMENSIONS      = 1536
BATCH_SIZE      = 50    # OpenAI embeddings batch size
SLEEP_SEC       = 0.2

def get_conn(server, driver, uid="Telegram_Agent_RW", pwd="TelegramAgent93ksJ"):
    conn_str = (
        f"DRIVER={{{driver}}};"
        f"SERVER={server};"
        f"DATABASE=Telegram_Agent;"
        f"UID={uid};PWD={pwd};"
        f"Encrypt=no;"
    )
    return pyodbc.connect(conn_str)

def sha256(text: str) -> str:
    return hashlib.sha256(text.encode('utf-8')).hexdigest()

def floats_to_bytes(floats: list[float]) -> bytes:
    """Pack float32 list as little-endian bytes for VARBINARY storage."""
    return struct.pack(f'<{len(floats)}f', *floats)

def bytes_to_floats(b: bytes) -> np.ndarray:
    """Unpack VARBINARY bytes back to numpy float32 array."""
    n = len(b) // 4
    return np.frombuffer(b, dtype='<f4').astype(np.float32)

def cosine_similarity(a: np.ndarray, b: np.ndarray) -> float:
    return float(np.dot(a, b) / (np.linalg.norm(a) * np.linalg.norm(b)))

# ── Step 1: Insert knowledge base rows ───────────────────────
def ingest_kb(conn, qa_pairs: list) -> dict:
    """Insert Q&A pairs, return mapping of SourceHash → KbId."""
    cursor = conn.cursor()
    hash_to_id = {}
    inserted = 0
    skipped  = 0

    for qa in qa_pairs:
        q = qa['q']
        a = qa['a']
        topics = ','.join(sorted(qa.get('topics', [])))
        h = sha256(q + a)

        # Check if already exists
        cursor.execute(
            "SELECT Id FROM dbo.KnowledgeBase WHERE SourceHash = ?", h
        )
        row = cursor.fetchone()
        if row:
            hash_to_id[h] = row[0]
            skipped += 1
            continue

        cursor.execute("""
            INSERT INTO dbo.KnowledgeBase (Question, Answer, Topics, SourceHash)
            OUTPUT INSERTED.Id
            VALUES (?, ?, ?, ?)
        """, q, a, topics, h)
        kb_id = cursor.fetchone()[0]
        hash_to_id[h] = kb_id
        inserted += 1

    conn.commit()
    print(f"  KnowledgeBase: {inserted} inserted, {skipped} already existed")
    return hash_to_id

# ── Step 2: Generate and store embeddings ─────────────────────
def ingest_embeddings(conn, qa_pairs: list, hash_to_id: dict, openai_client: OpenAI):
    cursor = conn.cursor()

    # Find which KB IDs still need embeddings
    cursor.execute("""
        SELECT kb.Id, kb.SourceHash
        FROM dbo.KnowledgeBase kb
        WHERE NOT EXISTS (
            SELECT 1 FROM dbo.KnowledgeBase_Embeddings e
            WHERE e.KbId = kb.Id AND e.EmbeddingModel = ?
        )
    """, EMBEDDING_MODEL)
    rows = cursor.fetchall()

    if not rows:
        print("  Embeddings: all rows already embedded, nothing to do.")
        return

    id_to_hash = {r[0]: r[1] for r in rows}
    qa_map = {sha256(qa['q'] + qa['a']): qa for qa in qa_pairs}

    pending_ids    = list(id_to_hash.keys())
    pending_texts  = []
    for kb_id in pending_ids:
        h  = id_to_hash[kb_id]
        qa = qa_map[h]
        # Embed Q only — keeps within token limits and is what we'll search against
        pending_texts.append(qa['q'])

    print(f"  Embeddings: generating for {len(pending_ids)} rows...")
    total_batches = (len(pending_ids) + BATCH_SIZE - 1) // BATCH_SIZE
    all_vectors   = []

    for i in range(0, len(pending_texts), BATCH_SIZE):
        batch_texts = pending_texts[i:i + BATCH_SIZE]
        bn = i // BATCH_SIZE + 1
        print(f"    Batch {bn}/{total_batches}...", end=' ', flush=True)
        response = openai_client.embeddings.create(
            model=EMBEDDING_MODEL,
            input=batch_texts
        )
        for item in response.data:
            all_vectors.append(item.embedding)
        print("✓")
        time.sleep(SLEEP_SEC)

    # Insert embeddings
    inserted = 0
    for kb_id, vector in zip(pending_ids, all_vectors):
        raw_bytes = floats_to_bytes(vector)
        cursor.execute("""
            INSERT INTO dbo.KnowledgeBase_Embeddings
                (KbId, EmbeddingVector, EmbeddingModel, Dimensions)
            VALUES (?, ?, ?, ?)
        """, kb_id, raw_bytes, EMBEDDING_MODEL, DIMENSIONS)
        inserted += 1

    conn.commit()
    print(f"  Embeddings: {inserted} vectors stored ({DIMENSIONS} dims each, "
          f"{DIMENSIONS*4} bytes per row)")

# ── Step 3: Query function (for reference / testing) ──────────
def query_kb(conn, question: str, openai_client: OpenAI, top_k: int = 5,
             topic_filter: str = None) -> list:
    """
    Semantic search: embed the question, compute cosine similarity
    against all stored vectors, return top_k matches.
    Called at runtime by your chatbot.
    """
    # Embed the incoming question
    response = openai_client.embeddings.create(
        model=EMBEDDING_MODEL,
        input=question
    )
    q_vec = np.array(response.data[0].embedding, dtype=np.float32)

    # Load all vectors (412 rows × 6KB = ~2.5MB — fast in memory)
    cursor = conn.cursor()
    if topic_filter:
        cursor.execute("""
            SELECT e.KbId, e.EmbeddingVector, kb.Question, kb.Answer, kb.Topics
            FROM dbo.KnowledgeBase_Embeddings e
            JOIN dbo.KnowledgeBase kb ON kb.Id = e.KbId
            WHERE e.EmbeddingModel = ?
              AND kb.Topics LIKE ?
        """, EMBEDDING_MODEL, f'%{topic_filter}%')
    else:
        cursor.execute("""
            SELECT e.KbId, e.EmbeddingVector, kb.Question, kb.Answer, kb.Topics
            FROM dbo.KnowledgeBase_Embeddings e
            JOIN dbo.KnowledgeBase kb ON kb.Id = e.KbId
            WHERE e.EmbeddingModel = ?
        """, EMBEDDING_MODEL)

    rows = cursor.fetchall()
    if not rows:
        return []

    # Compute cosine similarities
    results = []
    for kb_id, raw_bytes, q_text, a_text, topics in rows:
        db_vec = bytes_to_floats(bytes(raw_bytes))
        score  = cosine_similarity(q_vec, db_vec)
        results.append({
            'kb_id':    kb_id,
            'score':    score,
            'question': q_text,
            'answer':   a_text,
            'topics':   topics,
        })

    results.sort(key=lambda x: x['score'], reverse=True)
    return results[:top_k]

# ── Main ──────────────────────────────────────────────────────
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--server',     required=True, help='SQL Server host, e.g. localhost\\SQLEXPRESS')
    parser.add_argument('--openai-key', required=True, help='OpenAI API key for embeddings')
    parser.add_argument('--input',      default='casa_rosa_qa_final.json')
    parser.add_argument('--driver',     default='ODBC Driver 17 for SQL Server')
    parser.add_argument('--test-query', action='store_true', help='Run a test query after ingestion')
    args = parser.parse_args()

    print(f"Loading {args.input}...")
    with open(args.input, encoding='utf-8') as f:
        data = json.load(f)
    qa_pairs = data['qa_pairs']
    print(f"  {len(qa_pairs)} Q&A pairs loaded")

    print(f"\nConnecting to SQL Server: {args.server}...")
    conn = get_conn(args.server, args.driver)
    print("  Connected.")

    openai_client = OpenAI(api_key=args.openai_key)

    print("\n── Step 1: Inserting knowledge base rows ────────────────")
    hash_to_id = ingest_kb(conn, qa_pairs)

    print("\n── Step 2: Generating and storing embeddings ────────────")
    ingest_embeddings(conn, qa_pairs, hash_to_id, openai_client)

    if args.test_query:
        print("\n── Step 3: Test query ────────────────────────────────────")
        test_q = "Is there parking near the house?"
        print(f"Query: '{test_q}'")
        results = query_kb(conn, test_q, openai_client, top_k=3)
        for i, r in enumerate(results, 1):
            print(f"\n  Result {i} (score={r['score']:.3f}, topics={r['topics']})")
            print(f"  Q: {r['question'][:100]}")
            print(f"  A: {r['answer'][:150]}")

    conn.close()
    print("\nDone!")

if __name__ == '__main__':
    main()
