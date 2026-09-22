#!/usr/bin/env python3
"""Render a Markdown manual to PDF using headless Chromium.

Usage:
    python3 scripts/build-manual-pdf.py USER_MANUAL_ZH.md              # -> USER_MANUAL_ZH.pdf
    python3 scripts/build-manual-pdf.py USER_GUIDE.md out/guide.pdf

Requires: `pip install markdown`, a Chromium/Chrome binary (auto-detected, or set
CHROME=/path/to/chrome), and a CJK font for Chinese text (e.g. fonts-noto-cjk on
Debian/Ubuntu; on Windows, Microsoft YaHei is used automatically).
"""
import glob
import os
import shutil
import subprocess
import sys
import tempfile

try:
    import markdown
except ImportError:
    sys.exit("python module 'markdown' missing: pip install markdown")

CSS = """
@page { size: A4; margin: 18mm 16mm 20mm 16mm; }
html { font-size: 10.5pt; }
body {
  font-family: "Noto Sans CJK SC", "Noto Sans SC", "Microsoft YaHei", "PingFang SC",
               "WenQuanYi Zen Hei", "Segoe UI", Arial, sans-serif;
  line-height: 1.6; color: #1e1e2e; max-width: 100%;
}
h1 { font-size: 22pt; border-bottom: 2px solid #89b4fa; padding-bottom: 6px; margin-top: 0; }
h2 { font-size: 15pt; color: #1e40af; margin-top: 22pt; padding-top: 6pt;
     border-top: 1px solid #e5e7eb; page-break-after: avoid; }
h3 { font-size: 12pt; margin-top: 14pt; page-break-after: avoid; }
p, li { orphans: 3; widows: 3; }
code { font-family: Consolas, "Noto Sans Mono CJK SC", "DejaVu Sans Mono", monospace;
       font-size: 9.5pt; background: #f3f4f6; padding: 1px 4px; border-radius: 3px; }
pre { background: #f3f4f6; padding: 8px 10px; border-radius: 4px; overflow-x: auto;
      font-size: 9pt; page-break-inside: avoid; }
pre code { background: none; padding: 0; }
table { border-collapse: collapse; width: 100%; margin: 8pt 0; font-size: 9.5pt;
        page-break-inside: auto; }
th, td { border: 1px solid #d1d5db; padding: 5px 8px; vertical-align: top; text-align: left; }
th { background: #eef2ff; }
tr { page-break-inside: avoid; }
blockquote { border-left: 4px solid #f59e0b; background: #fffbeb; margin: 8pt 0;
             padding: 6px 12px; }
hr { border: 0; border-top: 1px solid #e5e7eb; margin: 16pt 0; }
a { color: #1d4ed8; text-decoration: none; }
strong { color: #111827; }
"""


def find_chrome() -> str:
    env = os.environ.get("CHROME")
    if env and os.path.exists(env):
        return env
    for name in ("chromium", "chromium-browser", "google-chrome", "google-chrome-stable", "chrome"):
        path = shutil.which(name)
        if path:
            return path
    for pattern in (
        "/opt/pw-browsers/chromium-*/chrome-linux/chrome",
        os.path.expanduser("~/.cache/ms-playwright/chromium-*/chrome-linux/chrome"),
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    ):
        hits = sorted(glob.glob(pattern))
        if hits:
            return hits[-1]
    sys.exit("No Chromium/Chrome found. Install one or set CHROME=/path/to/chrome")


def main() -> None:
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    src = sys.argv[1]
    dst = sys.argv[2] if len(sys.argv) > 2 else os.path.splitext(src)[0] + ".pdf"

    with open(src, encoding="utf-8") as f:
        body = markdown.markdown(
            f.read(),
            extensions=["tables", "fenced_code", "toc", "sane_lists"],
            extension_configs={"toc": {"slugify": lambda v, s: v.lower().replace(" ", "-")}},
        )

    title = os.path.splitext(os.path.basename(src))[0]
    html = f"<!doctype html><html lang='zh'><head><meta charset='utf-8'><title>{title}</title>" \
           f"<style>{CSS}</style></head><body>{body}</body></html>"

    with tempfile.TemporaryDirectory() as tmp:
        html_path = os.path.join(tmp, "manual.html")
        with open(html_path, "w", encoding="utf-8") as f:
            f.write(html)

        cmd = [
            find_chrome(), "--headless=new", "--disable-gpu", "--no-sandbox",
            "--no-pdf-header-footer", "--print-to-pdf-no-header",
            f"--print-to-pdf={os.path.abspath(dst)}", "file://" + html_path,
        ]
        result = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
        if result.returncode != 0 or not os.path.exists(dst):
            sys.exit(f"chromium failed:\n{result.stderr[-2000:]}")

    print(f"wrote {dst} ({os.path.getsize(dst) // 1024} KB)")


if __name__ == "__main__":
    main()
