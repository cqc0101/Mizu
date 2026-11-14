"""Create a reviewable source archive; Python 3 is optional developer tooling."""
from pathlib import Path
import hashlib
import zipfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
version = ET.parse(root / "Directory.Build.props").findtext(".//Version")
out = root / "artifacts" / f"Mizu-{version}-github-source.zip"
out.parent.mkdir(exist_ok=True)
directories = {".github", "docs", "licenses", "scripts", "src", "tests", "vendor"}
files = {".gitignore", ".gitattributes", "Directory.Build.props", "NuGet.Config", "Mizu.slnx", "README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", "CONTRIBUTING.md", "CHANGELOG.md", "get_battery.py"}
excluded = {"bin", "obj", "__pycache__", ".vs", ".git"}
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(root.rglob("*")):
        rel = path.relative_to(root)
        if not path.is_file() or path.is_symlink() or any(p in excluded for p in rel.parts):
            continue
        if rel.parts[0] not in directories and str(rel) not in files:
            continue
        if path.suffix.lower() in {".log", ".user", ".pyc"} or path.name.lower() in {"settings.json", ".env", "diagnostics.json", "battery.json", "poetry-token.dat", "poetry-token.dat.tmp", "poetry-cache.json", "weather-cache.json", "poetry-cache.json.local", "poetry-cache.json.local.tmp"}:
            continue
        archive.write(path, Path("Mizu") / rel)
with zipfile.ZipFile(out) as archive:
    assert archive.testzip() is None
print(out)
print(hashlib.sha256(out.read_bytes()).hexdigest())
