"""Read-only cross-check of the candidate PDF against its HTML marker ledger."""
import json
import re
import sys
from pathlib import Path

from pypdf import PdfReader

root = Path(sys.argv[1])
if "--renderer" in sys.argv:
    results = []
    for item in json.loads((root / "summary.json").read_text(encoding="utf-8")):
        folder = root / item["name"]
        source = json.loads((folder / "input.json").read_text(encoding="utf-8"))
        if not source["ok"]:
            continue
        reader = PdfReader(folder / "result.pdf")
        output = json.loads((folder / "result.json").read_text(encoding="utf-8"))
        text = re.sub(r"\s+", "", "\n".join(page.extract_text() or "" for page in reader.pages))
        passed = len(reader.pages) == len(output["pages"])
        if item["name"] == "long-record":
            passed &= all(len(re.findall(rf"LINE{i}(?![0-9])", text)) == 1 for i in range(100))
        if item["name"] == "filled-record-scopes":
            passed &= text.count("FILLED-ONE") == 1 and text.count("FILLED-TWO") == 1 and "original" not in text
        if item["name"] == "encoded-rich-record":
            passed &= text.count("RICH-FILLED") == 1
        if item["name"] == "repeated-page-bands":
            passed &= all((page.extract_text() or "").count("REPEATED-HEADER") == 1 and (page.extract_text() or "").count("REPEATED-FOOTER") == 1 for page in reader.pages)
        if item["name"].startswith("simple-"):
            passed &= all(len(re.findall(rf"FIRST{i}(?![0-9])", text)) == 1 and len(re.findall(rf"SECOND{i}(?![0-9])", text)) == 1 for i in range(45))
            first_second = next(i for i, page in enumerate(reader.pages) if "SECOND0" in (page.extract_text() or ""))
            passed &= first_second > 0
            for index, page in enumerate(reader.pages):
                dimensions = tuple(float(value) * 25.4 / 72 for value in (page.mediabox.width, page.mediabox.height))
                expected_dimensions = (210, 297) if index < first_second else (297, 210)
                passed &= all(abs(a - b) < 1 for a, b in zip(dimensions, expected_dimensions))
                if item["name"] == "simple-preserve":
                    local = index + 1 if index < first_second else index - first_second + 1
                    total = first_second if index < first_second else len(reader.pages) - first_second
                else:
                    local = index + 1
                    total = len(reader.pages)
                passed &= f"{local}/{total}" in re.sub(r"\s+", "", page.extract_text() or "")
        if item["name"] in ("a3-landscape", "custom-paper"):
            expected = (420, 297) if item["name"] == "a3-landscape" else (160, 220)
            actual = tuple(float(value) * 25.4 / 72 for value in (reader.pages[0].mediabox.width, reader.pages[0].mediabox.height))
            passed &= all(abs(a - b) < 1 for a, b in zip(actual, expected))
        result = {"name": item["name"], "pdfPages": len(reader.pages), "passed": bool(passed)}
        results.append(result)
        print(json.dumps(result))
    (root / "pdf-check.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
    sys.exit(0 if results and all(item["passed"] for item in results) else 1)
manifest = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
results = []
for fixture in manifest["fixtures"]:
    if "markers" not in fixture:
        continue
    reader = PdfReader(root / (fixture["name"] + ".pdf"))
    content = re.sub(r"\s+", "", "\n".join(page.extract_text() or "" for page in reader.pages))
    expected = {marker["token"] for marker in fixture["markers"]}
    missing = sorted(token for token in expected if token not in content)
    repeated = sorted(token for token in expected if content.count(token) != 1)
    result = {
        "name": fixture["name"],
        "htmlPages": fixture["pages"],
        "pdfPages": len(reader.pages),
        "missing": missing,
        "repeated": repeated,
        "passed": len(reader.pages) == fixture["pages"] and not missing and not repeated,
    }
    results.append(result)
    print(json.dumps(result, ensure_ascii=False))
(root / "pdf-check.json").write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")
if not results or any(not result["passed"] for result in results):
    sys.exit(1)
