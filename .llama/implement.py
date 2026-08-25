#!/usr/bin/env python3
"""Drive the local llama-server to implement a spec, compiling until it builds.

Design notes:
  * The model's output is written straight to disk and NEVER printed. The point is to
    keep generated code out of the orchestrating agent's context; only the build result
    is surfaced.
  * Retries restate the FULL spec every time, not just the errors. Tested behaviour of
    Llama-3.3-70B: "here is your code, fix these N things" makes it drop the contract
    (changed parameter types, dropped callbacks, hardcoded English over localized
    strings). Regenerating from a complete spec plus the compiler output does not.
  * Compiler output is truncated and de-duplicated, because repeated identical errors
    across projects crowd out the spec in the model's context window.
"""
import argparse, os, json, pathlib, re, subprocess, sys, urllib.request

FENCE = re.compile(r"```(?:[a-zA-Z]*)\n(.*?)```", re.DOTALL)


def strip_fence(text: str) -> str:
    """Models wrap answers in markdown fences even when told not to."""
    blocks = FENCE.findall(text)
    if blocks:
        return max(blocks, key=len).strip() + "\n"
    return text.strip() + "\n"


def ask(base_url: str, system: str, user: str, max_tokens: int, temperature: float,
        timeout: float, api_key: str | None = None, model: str | None = None,
        chat_path: str = "/v1/chat/completions") -> str:
    payload = {
        "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}],
        "temperature": temperature,
        "max_tokens": max_tokens,
        "stream": False,
    }
    # llama-server serves whatever model it was started with and ignores this field;
    # hosted endpoints require it.
    if model:
        payload["model"] = model

    headers = {"Content-Type": "application/json"}
    if api_key:
        headers["Authorization"] = "Bearer " + api_key

    req = urllib.request.Request(
        base_url.rstrip("/") + chat_path,
        data=json.dumps(payload).encode(),
        headers=headers,
    )
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        body = json.load(resp)
    choice = body["choices"][0]
    if choice.get("finish_reason") == "length":
        print("  ! hit max_tokens; output may be truncated", file=sys.stderr)
    return choice["message"]["content"]

def is_environment_failure(output: str) -> bool:
    lowered = output.lower()
    return any(marker in lowered for marker in ENVIRONMENT_ERRORS)


def compress_errors(output: str, limit: int = 14) -> str:
    """Extract actionable feedback from a build OR a test run.

    Compiler errors, NUnit failure names and assertion messages all look different, and
    an earlier version of this matched only lines containing "error". `dotnet test`
    reports failures without that word, so the model received an EMPTY error report and
    retried blind four times, producing byte-identical output each time. Never return
    nothing: if no pattern matches, fall back to the tail of the raw output.
    """
    patterns = ("error cs", "error rz", "error msb", " error ", "failed ",
                "error message", "expected:", "but was:", "assert")
    seen, lines = set(), []
    for line in output.splitlines():
        low = line.lower()
        if not any(p in low for p in patterns):
            continue
        if low.strip().startswith("at "):
            continue
        line = re.sub(r"\[[A-Za-z]:\\.*?\]", "", line).strip()
        if line and line not in seen:
            seen.add(line)
            lines.append(line)
        if len(lines) >= limit:
            lines.append("... (further errors omitted)")
            break

    if not lines:
        tail = [l for l in output.splitlines() if l.strip()][-20:]
        return "Build/test failed but no recognised error lines. Raw tail:\n" + "\n".join(tail)
    return "\n".join(lines)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--spec", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--build", required=True, help="shell command; non-zero exit = failure")
    ap.add_argument("--system", default=None)
    ap.add_argument("--attempts", type=int, default=4)
    ap.add_argument("--max-tokens", type=int, default=4000)
    ap.add_argument("--temperature", type=float, default=0.15)
    ap.add_argument("--base-url", default="http://127.0.0.1:8080")
    ap.add_argument("--api-key", default=os.environ.get("GEMINI_API_KEY"),
                    help="Bearer token. Defaults to $GEMINI_API_KEY. Local llama-server needs none.")
    ap.add_argument("--model", default=None,
                    help="Model id for hosted endpoints, e.g. gemini-3.1-pro-preview.")
    ap.add_argument("--chat-path", default="/v1/chat/completions",
                    help="Gemini's OpenAI surface already ends in /openai, so pass /chat/completions.")
    ap.add_argument("--timeout", type=float, default=1800)
    args = ap.parse_args()

    spec = pathlib.Path(args.spec).read_text(encoding="utf-8")
    system = pathlib.Path(args.system).read_text(encoding="utf-8") if args.system else "You are an expert software engineer."
    out = pathlib.Path(args.out)
    backup = out.read_text(encoding="utf-8") if out.exists() else None

    prompt = spec
    for attempt in range(1, args.attempts + 1):
        print(f"[attempt {attempt}/{args.attempts}] generating...", flush=True)
        try:
            raw = ask(args.base_url, system, prompt, args.max_tokens, args.temperature, args.timeout,
                   api_key=args.api_key, model=args.model,
                   chat_path=args.chat_path)
        except Exception as exc:
            print(f"FAILED: llama-server error: {exc}")
            return 2

        code = strip_fence(raw)
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(code, encoding="utf-8", newline="\n")
        print(f"  wrote {len(code)} bytes to {out}", flush=True)

        proc = subprocess.run(args.build, shell=True, capture_output=True, text=True)
        if proc.returncode == 0:
            print(f"BUILD OK on attempt {attempt}")
            return 0

        combined = proc.stdout + "\n" + proc.stderr
        errors = compress_errors(combined)
        if is_environment_failure(combined):
            print("  build failed for an ENVIRONMENT reason, not the generated code:")
            print(errors)
            print("ABORTED: fix the environment and re-run. No further attempts made.")
            if backup is not None:
                out.write_text(backup, encoding="utf-8", newline="\n")
                print("restored previous file contents")
            return 2
        print(f"  build failed:\n{errors}", flush=True)
        # Full spec again, plus the failed attempt and its errors.
        prompt = (
            f"{spec}\n\n"
            f"=== YOUR PREVIOUS ATTEMPT FAILED TO BUILD ===\n"
            f"This is the file you produced:\n```\n{code}```\n\n"
            f"The compiler reported:\n{errors}\n\n"
            f"Produce the COMPLETE corrected file. Re-read the specification above and "
            f"satisfy every requirement in it - do not drop parameters, types, callbacks "
            f"or localized strings that the specification requires. Return only code."
        )

    print(f"FAILED after {args.attempts} attempts")
    if backup is not None:
        out.write_text(backup, encoding="utf-8", newline="\n")
        print("restored previous file contents")
    return 1


if __name__ == "__main__":
    sys.exit(main())
