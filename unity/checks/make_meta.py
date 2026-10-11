"""Write the .meta files Unity needs for an immutable package (git or registry install). GUIDs are derived from
each file's path in the package, so they stay the same from one run to the next. Folders ending in ~ (Samples~)
are ignored by Unity and get no metas."""
import os, sys, uuid

ROOT = sys.argv[1]
NS = uuid.UUID("6f3c0f5e-6a3b-4d0b-9a77-b0b0b0b0b0b0")

def guid(rel):
    return uuid.uuid5(NS, "com.parkyn.bounded-buoyancy/" + rel.replace(os.sep, "/")).hex

def body(rel, is_dir):
    g = guid(rel)
    if is_dir:
        return f"fileFormatVersion: 2\nguid: {g}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    ext = os.path.splitext(rel)[1].lower()
    name = os.path.basename(rel)
    if ext == ".cs":
        return (f"fileFormatVersion: 2\nguid: {g}\nMonoImporter:\n  externalObjects: {{}}\n  serializedVersion: 2\n"
                f"  defaultReferences: []\n  executionOrder: 0\n  icon: {{instanceID: 0}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    if ext == ".asmdef":
        return f"fileFormatVersion: 2\nguid: {g}\nAssemblyDefinitionImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    if name == "package.json":
        return f"fileFormatVersion: 2\nguid: {g}\nPackageManifestImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
    return f"fileFormatVersion: 2\nguid: {g}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

count = 0
for dirpath, dirnames, filenames in os.walk(ROOT):
    dirnames[:] = [d for d in dirnames if not d.endswith("~") and not d.startswith(".")]
    rel_dir = os.path.relpath(dirpath, ROOT)
    for d in dirnames:
        rel = os.path.normpath(os.path.join(rel_dir, d))
        open(os.path.join(dirpath, d + ".meta"), "w").write(body(rel, True)); count += 1
    for f in filenames:
        if f.endswith(".meta") or f.startswith("."):
            continue
        rel = os.path.normpath(os.path.join(rel_dir, f))
        open(os.path.join(dirpath, f + ".meta"), "w").write(body(rel, False)); count += 1
print(count, "meta files")
