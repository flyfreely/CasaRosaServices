"""
Casa Rosa — Translate all Q&A pairs to English
Run: python translate.py --api-key sk-ant-XXXX
"""

import json, re, time, argparse
import anthropic

INPUT_FILE  = 'casa_rosa_qa_ready.json'
OUTPUT_FILE = 'casa_rosa_qa_final.json'
BATCH_SIZE  = 5
SLEEP_SEC   = 0.3

SYSTEM_PROMPT = """You are a translation assistant for an Airbnb host in Cascais, Portugal.
Translate each Q&A pair to natural English. Rules:
- If already English, copy exactly as-is
- Preserve [GUEST] placeholders exactly — never translate or alter them
- Preserve URLs, door codes, phone numbers, restaurant/place names exactly
- Keep the same JSON structure with idx, q, a fields
- Return ONLY a JSON array, no markdown, no preamble"""

def translate_batch(client, batch):
    payload = [{'idx': i, 'q': qa['q'], 'a': qa['a']} for i, qa in enumerate(batch)]
    response = client.messages.create(
        model='claude-sonnet-4-20250514',
        max_tokens=8192,
        system=SYSTEM_PROMPT,
        messages=[{'role': 'user', 'content': json.dumps(payload, ensure_ascii=False)}]
    )
    raw = response.content[0].text.strip()
    raw = re.sub(r'^```(?:json)?\s*|\s*```$', '', raw)
    translated = json.loads(raw)
    return [{'q': item['q'], 'a': item['a'], 'topics': batch[item['idx']]['topics']}
            for item in translated]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--api-key', required=True)
    parser.add_argument('--input',  default=INPUT_FILE)
    parser.add_argument('--output', default=OUTPUT_FILE)
    args = parser.parse_args()

    with open(args.input, encoding='utf-8') as f:
        data = json.load(f)

    pairs = data['qa_pairs']
    client = anthropic.Anthropic(api_key=args.api_key)
    total_batches = (len(pairs) + BATCH_SIZE - 1) // BATCH_SIZE
    print(f"Translating {len(pairs)} pairs in {total_batches} batches...")

    result = []
    for i in range(0, len(pairs), BATCH_SIZE):
        batch = pairs[i:i + BATCH_SIZE]
        bn = i // BATCH_SIZE + 1
        print(f"  Batch {bn}/{total_batches}...", end=' ', flush=True)
        try:
            result.extend(translate_batch(client, batch))
            print("OK")
        except Exception as e:
            print(f"FAILED: {e} - keeping original")
            result.extend(batch)
        time.sleep(SLEEP_SEC)

    output = {**data['meta'], 'translated': True, 'total_qa_pairs': len(result)}
    final = {'meta': output, 'qa_pairs': result}

    with open(args.output, 'w', encoding='utf-8') as f:
        json.dump(final, f, ensure_ascii=False, indent=2)

    print(f"\nDone -> {args.output}  ({len(result)} pairs)")

if __name__ == '__main__':
    main()
