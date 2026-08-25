#!/usr/bin/env python3
"""Fill in missing locale strings using Gemini's OpenAI-compatible endpoint.

Only en.json and ge.json were ever complete; the other fifteen locales carry 91 of 192
keys and fall back to English for the rest. This walks each locale, asks for just its
missing keys, and validates the reply before writing anything.

Deliberately prints only a per-locale report, never the translations themselves — the
point is to keep 1,500 strings out of the orchestrating agent's context.

Validation is structural, not semantic. It can prove the placeholders survived and that
nothing was silently left in English; it cannot judge whether the Korean reads well.
"""
import argparse
import json
import os
import re
import sys
import time
import urllib.error
import urllib.request
from collections import Counter
from pathlib import Path

LOCALE_DIR = Path("src/SPTQuestMap/Localization/Locales")
NOT_A_LOCALE = {"en", "spt-global-keys"}

LANGUAGES = {
    "ch": "Simplified Chinese", "cz": "Czech", "es": "Spanish (Spain)",
    "es-mx": "Spanish (Latin American / Mexican)", "fr": "French", "ge": "German",
    "hu": "Hungarian", "it": "Italian", "jp": "Japanese", "kr": "Korean",
    "pl": "Polish", "po": "Portuguese", "ro": "Romanian", "ru": "Russian",
    "sk": "Slovak", "tu": "Turkish",
}

# Values that are legitimately identical across languages, so "still English" is not a
# defect for them: single-letter badges, abbreviations, and proper nouns.
ALLOWED_IDENTICAL = {
    "badge.branch", "badge.event", "gates.loyaltyShort", "state.NotApplicable",
    "repeatable.scav", "route.collector", "route.lightkeeper", "common.or",
    "metrics.overview", "compare.category.PrimaryOnly", "compare.category.ComparisonOnly",
    "compare.filter.PrimaryOnly", "compare.filter.ComparisonOnly", "gates.reputationShort",
    # Product and proper nouns, plus format strings that are mostly abbreviations.
    "app.title", "app.heading", "details.wiki", "trader.summary", "metrics.summary",
}

PLACEHOLDER = re.compile(r"\{[^}]*\}")

SYSTEM = """You are a professional game-localization translator working on a mod for
Escape from Tarkov (SPT). You translate UI strings only. You reply with a single JSON
object and nothing else - no prose, no markdown fence."""


def build_prompt(language: str, existing: dict, missing: dict) -> str:
    # A sample of what is already translated pins terminology and register, so the new
    # strings do not drift into a different voice than the ones already shipped.
    sample = dict(list(existing.items())[:45])
    return f"""Translate these Escape from Tarkov quest-map UI strings into {language}.

RULES, all mandatory:

1. Reply with ONE JSON object. Its keys must be EXACTLY the keys given below - all
   {len(missing)} of them, no more, no fewer. Never translate the keys themselves.
2. Placeholders in braces are code substitutions. Copy them CHARACTER FOR CHARACTER and
   never translate the text inside them: {{count}}, {{trader}}, {{quest}}, {{value}},
   {{profile}}, {{0}}, {{visible}}, {{applicable}}, {{edges}}, {{layout}}, {{overlay}},
   {{render}}, {{primary}}, {{comparison}}, {{compare}}, {{statuses}}, {{kind}},
   {{availability}}, {{loyalty}}, {{standing}}. You may reorder them within a sentence if
   the target language requires it, but every placeholder present in the source must be
   present in your translation, exactly once each.
3. Keep Escape from Tarkov terminology in whatever form that game's own {language}
   localization uses. Terms like Scav, PMC, Flea Market, Lightkeeper, Collector, Prestige
   and Loyalty Level are usually kept or have established conventional renderings - use
   those, do not invent new ones.
4. These are UI labels, buttons and tooltips: keep them SHORT. A label that doubles in
   length will overflow its control. Match the brevity of the source.
5. Single letters and abbreviations (like "B", "E", "LL", "N/A") stay as they are unless
   the language genuinely uses a different convention.
6. Preserve leading and trailing spaces exactly where the source has them.

Here are strings ALREADY translated into {language} in this same file. Match their
terminology, tone and capitalisation conventions:

{json.dumps(sample, ensure_ascii=False, indent=1)}

Now translate these {len(missing)} strings. Reply with the JSON object only:

{json.dumps(missing, ensure_ascii=False, indent=1)}"""


def ask(prompt: str, model: str, api_key: str, timeout: float) -> str:
    payload = {
        "model": model,
        "messages": [
            {"role": "system", "content": SYSTEM},
            {"role": "user", "content": prompt},
        ],
        "temperature": 0.2,
        "max_tokens": 16000,
    }
    req = urllib.request.Request(
        "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions",
        data=json.dumps(payload).encode("utf-8"),
        headers={"Content-Type": "application/json", "Authorization": "Bearer " + api_key},
    )
    # 503 (model busy) and 429 (rate limit) are both transient and both common on the
    # free tier. Backing off is the difference between a run that finishes and one that
    # gives up a third of the way through.
    for attempt in range(5):
        try:
            with urllib.request.urlopen(req, timeout=timeout) as resp:
                body = json.load(resp)
            break
        except urllib.error.HTTPError as exc:
            if exc.code not in (429, 503) or attempt == 4:
                raise
            wait = 15 * (attempt + 1)
            print(f"         HTTP {exc.code}, retrying in {wait}s", flush=True)
            time.sleep(wait)
    else:
        raise ValueError("exhausted retries")
    choice = body["choices"][0]
    if choice.get("finish_reason") == "length":
        raise ValueError("reply hit max_tokens and is truncated")
    return choice["message"]["content"]


def parse_object(text: str) -> dict:
    text = text.strip()
    if text.startswith("```"):
        text = re.sub(r"^```[a-zA-Z]*\n", "", text)
        text = re.sub(r"\n```$", "", text.rstrip())
    start, end = text.find("{"), text.rfind("}")
    if start == -1 or end == -1:
        raise ValueError("no JSON object in reply")
    return json.loads(text[start:end + 1])


def translatable_residue(source: str) -> int:
    """How much actual prose a string contains once placeholders are removed.

    "{trader} {kind} {compare} {value}" is entirely substitutions - an identical
    "translation" of it is correct, not a skipped one. Flagging those as untranslated
    buries the real misses in noise.
    """
    return len(re.sub(r"[^A-Za-z]", "", PLACEHOLDER.sub("", source)))


def validate(english: dict, translated: dict) -> list:
    problems = []
    missing_keys = set(english) - set(translated)
    extra_keys = set(translated) - set(english)
    if missing_keys:
        problems.append(f"{len(missing_keys)} key(s) not returned: {sorted(missing_keys)[:5]}")
    if extra_keys:
        problems.append(f"{len(extra_keys)} invented key(s): {sorted(extra_keys)[:5]}")

    for key, source in english.items():
        value = translated.get(key)
        if value is None:
            continue
        if not isinstance(value, str):
            problems.append(f"{key}: not a string")
            continue
        if Counter(PLACEHOLDER.findall(source)) != Counter(PLACEHOLDER.findall(value)):
            problems.append(f"{key}: placeholders changed ({source!r} -> {value!r})")
        if value == source and key not in ALLOWED_IDENTICAL and translatable_residue(source) > 4:
            problems.append(f"{key}: left in English ({source!r})")
    return problems


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="gemini-flash-latest")
    ap.add_argument("--timeout", type=float, default=300)
    ap.add_argument("--sleep", type=float, default=6, help="pause between calls, for RPM limits")
    ap.add_argument("--only", default=None, help="comma-separated locale codes")
    ap.add_argument("--dry-run", action="store_true")
    ap.add_argument("--refresh", action="store_true", help="ignore cached replies")
    args = ap.parse_args()

    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        print("GEMINI_API_KEY is not set", file=sys.stderr)
        return 2

    english = json.loads((LOCALE_DIR / "en.json").read_text(encoding="utf-8"))
    wanted = [c.strip() for c in args.only.split(",")] if args.only else None

    total_written = 0
    failures = []
    for path in sorted(LOCALE_DIR.glob("*.json")):
        code = path.stem
        if code in NOT_A_LOCALE or (wanted and code not in wanted):
            continue
        existing = json.loads(path.read_text(encoding="utf-8"))
        missing = {k: v for k, v in english.items() if k not in existing}
        if not missing:
            print(f"{code:6} complete already")
            continue

        language = LANGUAGES.get(code, code)
        print(f"{code:6} {language:32} requesting {len(missing)} strings...", flush=True)
        if args.dry_run:
            continue

        cache = Path(".llama/cache") / f"locale-{code}.txt"
        try:
            if cache.exists() and not args.refresh:
                print(f"{code:6} using cached reply", flush=True)
                reply = cache.read_text(encoding="utf-8")
            else:
                reply = ask(build_prompt(language, existing, missing), args.model, api_key, args.timeout)
                cache.parent.mkdir(parents=True, exist_ok=True)
                cache.write_text(reply, encoding="utf-8")
            translated = parse_object(reply)
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", "replace")[:200]
            print(f"{code:6} HTTP {exc.code}: {detail}", flush=True)
            failures.append(code)
            continue
        except Exception as exc:                                  # noqa: BLE001
            print(f"{code:6} FAILED: {exc}", flush=True)
            failures.append(code)
            continue

        problems = validate(missing, translated)
        if problems:
            print(f"{code:6} REJECTED, {len(problems)} problem(s):", flush=True)
            for problem in problems[:6]:
                print(f"         - {problem}", flush=True)
            failures.append(code)
            continue

        # Write in en.json's key order so the files stay diffable against each other.
        merged = {k: translated.get(k, existing.get(k, v)) for k, v in english.items()}
        for k, v in existing.items():
            merged.setdefault(k, v)
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            json.dump(merged, handle, ensure_ascii=False, indent=2)
            handle.write("\n")
        print(f"{code:6} OK, wrote {len(translated)} strings ({len(merged)} total)", flush=True)
        total_written += 1
        if not cache.exists() or args.refresh:
            time.sleep(args.sleep)

    print(f"\ndone: {total_written} locale(s) written, {len(failures)} failed"
          + (f" ({', '.join(failures)})" if failures else ""))
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
