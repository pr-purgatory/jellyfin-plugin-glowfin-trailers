#!/usr/bin/env python3
"""Adds a release to manifest.json (newest first), replacing an entry with the same version.

usage: update_manifest.py <version> <zip> <download url> <changelog file>
targetAbi is the Jellyfin.Controller version the plugin is built against (the csproj), so a server older than
that won't offer the update.
"""
import datetime, hashlib, json, pathlib, re, sys

version, zip_path, url, changelog_path = sys.argv[1:5]
root = pathlib.Path(__file__).resolve().parent.parent
csproj = (root / "Jellyfin.Plugin.GlowfinTrailers/Jellyfin.Plugin.GlowfinTrailers.csproj").read_text()
abi = re.search(r'Include="Jellyfin.Controller" Version="([\d.]+)"', csproj).group(1)
abi = ".".join((abi.split(".") + ["0"] * 4)[:4])

manifest_path = root / "manifest.json"
manifest = json.loads(manifest_path.read_text())
entry = {
    "version": version,
    "changelog": pathlib.Path(changelog_path).read_text().strip(),
    "targetAbi": abi,
    "sourceUrl": url,
    "checksum": hashlib.md5(pathlib.Path(zip_path).read_bytes()).hexdigest(),
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
}
plugin = manifest[0]
plugin["versions"] = [entry] + [v for v in plugin["versions"] if v["version"] != version]
manifest_path.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n")
print(f"manifest.json: {plugin['name']} {version} (targetAbi {abi})")
