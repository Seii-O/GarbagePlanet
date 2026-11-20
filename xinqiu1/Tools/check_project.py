#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""离线工程自检（L1）。零第三方依赖，退出码 0=通过 / 1=有错误。

替代外部技能包里的 check_unity_project.py，检查项与其保持一致：
目录与必需文件、工程版本、包清单、.meta 配对、GUID 唯一、
场景↔构建列表双向一致、C# 括号配平、悬空 GUID 引用、机器绝对路径、
@trace 可追溯覆盖率、真实编译日志。

用法（工程根目录）：
  python Tools/check_project.py . \
    --spec docs/project-spec.json \
    --freeze docs/design-freeze.json \
    --compile-log docs/unity-compile-final.log \
    --json docs/unity-check-report.json
"""

import argparse
import json
import os
import re
import sys
from datetime import datetime

GUID_RE = re.compile(r"guid:\s*([0-9a-fA-F]{32})")
ABS_PATH_RE = re.compile(r"[A-Za-z]:[\\/][^\s\"',;)\]}]*")
TRACE_RE = re.compile(r"@trace\s+([A-Z]+-\d+(?:\s*,\s*[A-Z]+-\d+)*)")
TEXT_EXT = {".unity", ".prefab", ".asset", ".mat", ".controller", ".anim",
            ".overrideController", ".playable", ".physicMaterial", ".lighting",
            ".renderTexture", ".mixer", ".spriteatlas", ".shadervariants"}
SKIP_DIRS = {"Library", "Temp", "obj", "Logs", "UserSettings", ".git", ".vs"}


class Report:
    def __init__(self):
        self.items = []

    def add(self, level, check, message, path="", hint="", files=None):
        self.items.append({
            "level": level, "check": check, "message": message,
            "path": path or "", "hint": hint or "", "files": files or [],
        })

    def error(self, *a, **kw):
        self.add("error", *a, **kw)

    def warn(self, *a, **kw):
        self.add("warning", *a, **kw)

    def info(self, *a, **kw):
        self.add("info", *a, **kw)

    @property
    def errors(self):
        return sum(1 for i in self.items if i["level"] == "error")

    @property
    def warnings(self):
        return sum(1 for i in self.items if i["level"] == "warning")


def read_json(path, report, check):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except Exception as e:  # noqa: BLE001
        report.error(check, "无法解析 %s：%s" % (path, e), path)
        return None


def walk_assets(root):
    """返回 (文件列表, 目录列表)，均已排除 .meta 与噪音目录。"""
    files, dirs = [], []
    base = os.path.join(root, "Assets")
    for cur, dnames, fnames in os.walk(base):
        dnames[:] = [d for d in dnames if d not in SKIP_DIRS and not d.startswith(".")]
        rel_cur = os.path.relpath(cur, root).replace("\\", "/")
        if rel_cur != "Assets":
            dirs.append(rel_cur)
        for fn in fnames:
            if fn.endswith(".meta") or fn.startswith("."):
                continue
            files.append(rel_cur + "/" + fn)
    return files, dirs


def check_layout(root, spec, report):
    missing_dirs = [d for d in spec.get("requiredDirs", []) if not os.path.isdir(os.path.join(root, d))]
    missing_files = [f for f in spec.get("requiredFiles", []) if not os.path.isfile(os.path.join(root, f))]
    if missing_dirs or missing_files:
        report.error("layout", "缺少必需目录 %d 个 / 必需文件 %d 个" % (len(missing_dirs), len(missing_files)),
                     hint="; ".join(missing_dirs + missing_files))
    else:
        report.info("layout", "必需目录与文件齐全（%d 个必需目录 / %d 个必需文件）"
                    % (len(spec.get("requiredDirs", [])), len(spec.get("requiredFiles", []))))

    rec_missing = [d for d in spec.get("recommendedDirs", []) if not os.path.isdir(os.path.join(root, d))]
    if rec_missing:
        report.warn("layout", "推荐目录缺失 %d 个" % len(rec_missing), hint="; ".join(rec_missing))
    else:
        report.info("layout", "推荐目录齐全（%d 个）" % len(spec.get("recommendedDirs", [])))


def check_version(root, report):
    p = os.path.join(root, "ProjectSettings/ProjectVersion.txt")
    if not os.path.isfile(p):
        report.error("projectVersion", "缺少 ProjectVersion.txt", "ProjectSettings/ProjectVersion.txt")
        return
    ver = ""
    with open(p, "r", encoding="utf-8", errors="ignore") as f:
        for line in f:
            if line.startswith("m_EditorVersion:"):
                ver = line.split(":", 1)[1].strip()
                break
    if not ver:
        report.error("projectVersion", "ProjectVersion.txt 无 m_EditorVersion", "ProjectSettings/ProjectVersion.txt")
        return
    if not re.match(r"^\d{4}\.\d+\.\d+[a-z]\d+$", ver):
        report.warn("projectVersion", "版本号格式非通用 20xx.x.xxf1：%s" % ver, "ProjectSettings/ProjectVersion.txt")
    else:
        report.info("projectVersion", "工程版本 %s" % ver, "ProjectSettings/ProjectVersion.txt")


def check_manifest(root, report):
    manifest = read_json(os.path.join(root, "Packages/manifest.json"), report, "manifest")
    if manifest is None:
        return
    deps = manifest.get("dependencies", {})
    if not deps:
        report.error("manifest", "manifest.json 无 dependencies", "Packages/manifest.json")
        return
    bad = [k for k, v in deps.items() if not isinstance(v, str) or not v.strip()]
    if bad:
        report.error("manifest", "包依赖值非法：%s" % ", ".join(bad), "Packages/manifest.json")
    else:
        report.info("manifest", "包依赖 %d 项，格式合法" % len(deps), "Packages/manifest.json")


def check_meta_pairing(root, files, dirs, report):
    missing, orphan = [], []
    for rel in files + dirs:
        if not os.path.isfile(os.path.join(root, rel + ".meta")):
            missing.append(rel)
    # 反向：.meta 没有对应资产
    for cur, dnames, fnames in os.walk(os.path.join(root, "Assets")):
        dnames[:] = [d for d in dnames if d not in SKIP_DIRS and not d.startswith(".")]
        for fn in fnames:
            if not fn.endswith(".meta"):
                continue
            owner = os.path.join(cur, fn[:-5])
            if not os.path.exists(owner):
                orphan.append(os.path.relpath(os.path.join(cur, fn), root).replace("\\", "/"))
    if missing:
        report.error("metaPairing", "%d 个资产缺 .meta" % len(missing),
                     files=missing[:30], hint="先运行 Tools/ensure_meta.py 补齐")
    if orphan:
        report.error("metaPairing", "%d 个 .meta 无对应资产（悬空）" % len(orphan), files=orphan[:30])
    if not missing and not orphan:
        report.info("metaPairing", "%d 个文件与 %d 个目录的元数据配对完整" % (len(files), len(dirs)))


def collect_guids(root, report):
    guid_map, dup = {}, []
    for cur, dnames, fnames in os.walk(os.path.join(root, "Assets")):
        dnames[:] = [d for d in dnames if d not in SKIP_DIRS and not d.startswith(".")]
        for fn in fnames:
            if not fn.endswith(".meta"):
                continue
            mp = os.path.join(cur, fn)
            rel = os.path.relpath(mp, root).replace("\\", "/")
            try:
                with open(mp, "r", encoding="utf-8", errors="ignore") as f:
                    head = f.read(400)
            except OSError:
                continue
            m = GUID_RE.search(head)
            if not m:
                continue
            g = m.group(1).lower()
            if g in guid_map:
                dup.append("%s <-> %s" % (guid_map[g], rel))
            else:
                guid_map[g] = rel
    if dup:
        report.error("guidUnique", "%d 个重复 GUID" % len(dup), files=dup[:20])
    else:
        report.info("guidUnique", "%d 个 GUID 全部唯一" % len(guid_map))
    return set(guid_map.keys())


def check_scenes(root, spec, report):
    scene_files = set()
    for cur, dnames, fnames in os.walk(os.path.join(root, "Assets")):
        dnames[:] = [d for d in dnames if d not in SKIP_DIRS]
        for fn in fnames:
            if fn.endswith(".unity"):
                scene_files.add(os.path.relpath(os.path.join(cur, fn), root).replace("\\", "/"))

    ebs = os.path.join(root, "ProjectSettings/EditorBuildSettings.asset")
    build_scenes, build_disabled = [], []
    if os.path.isfile(ebs):
        with open(ebs, "r", encoding="utf-8", errors="ignore") as f:
            lines = f.read().splitlines()
        enabled = True
        for line in lines:
            s = line.strip()
            if s.startswith("enabled:"):
                enabled = s.split(":", 1)[1].strip() == "1"
            elif s.startswith("path:"):
                p = s.split(":", 1)[1].strip()
                if p.endswith(".unity"):
                    (build_scenes if enabled else build_disabled).append(p)

    ignored = {(i.get("check"), i.get("path")) for i in spec.get("ignore", [])}
    ignored_scenes = {p for (c, p) in ignored if c == "scenes"}

    missing_from_build = sorted(scene_files - set(build_scenes) - ignored_scenes)
    missing_on_disk = sorted(set(build_scenes) - scene_files)
    if missing_from_build:
        report.error("scenes", "%d 个场景未写入构建列表" % len(missing_from_build),
                     "ProjectSettings/EditorBuildSettings.asset", files=missing_from_build)
    if missing_on_disk:
        report.error("scenes", "%d 个构建条目指向不存在的场景" % len(missing_on_disk),
                     "ProjectSettings/EditorBuildSettings.asset", files=missing_on_disk)
    if not missing_from_build and not missing_on_disk:
        suppressed = len(ignored_scenes & scene_files)
        report.info("scenes", "%d 个场景均已写入构建列表（另有 %d 条按规格抑制）"
                    % (len(build_scenes), suppressed), "ProjectSettings/EditorBuildSettings.asset")
    if build_disabled:
        report.warn("buildSettings", "%d 个构建条目被禁用" % len(build_disabled),
                    files=build_disabled)


def strip_csharp(src):
    """去掉注释与字符串字面量，避免其中的括号干扰配平统计。"""
    out, i, n = [], 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            while i < n and src[i] != "\n":
                i += 1
        elif c == "/" and i + 1 < n and src[i + 1] == "*":
            i += 2
            while i + 1 < n and not (src[i] == "*" and src[i + 1] == "/"):
                i += 1
            i += 2
        elif c == '"':
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    break
                i += 1
        elif c == "'":
            i += 1
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == "'":
                    i += 1
                    break
                i += 1
        else:
            out.append(c)
            i += 1
    return "".join(out)


def check_csharp(root, files, report):
    bad = []
    cs = [f for f in files if f.endswith(".cs")]
    for rel in cs:
        try:
            with open(os.path.join(root, rel), "r", encoding="utf-8", errors="ignore") as f:
                src = strip_csharp(f.read())
        except OSError:
            continue
        for op, cl in (("{", "}"), ("(", ")"), ("[", "]")):
            if src.count(op) != src.count(cl):
                bad.append("%s: %s%s 不配平 (%d/%d)" % (rel, op, cl, src.count(op), src.count(cl)))
                break
    if bad:
        report.error("csharp", "%d 个 C# 文件括号不配平" % len(bad), files=bad[:20])
    else:
        report.info("csharp", "%d 个 C# 文件括号与字面量配平" % len(cs))


def collect_package_guids(root, report):
    """包内资产的 GUID（PackageCache + 编辑器内置包），否则包引用会被误判为悬空。"""
    targets = [os.path.join(root, "Library", "PackageCache")]
    for base in (r"C:\Program Files\Unity\Hub\Editor", r"D:\Unity\Hub\Editor"):
        if os.path.isdir(base):
            for ver in os.listdir(base):
                builtin = os.path.join(base, ver, "Editor", "Data", "Resources",
                                       "PackageManager", "BuiltInPackages")
                if os.path.isdir(builtin):
                    targets.append(builtin)
    found = set()
    scanned = 0
    for t in targets:
        if not os.path.isdir(t):
            continue
        for cur, dnames, fnames in os.walk(t):
            for fn in fnames:
                if not fn.endswith(".meta"):
                    continue
                scanned += 1
                try:
                    with open(os.path.join(cur, fn), "r", encoding="utf-8", errors="ignore") as f:
                        head = f.read(300)
                except OSError:
                    continue
                m = GUID_RE.search(head)
                if m:
                    found.add(m.group(1).lower())
    if found:
        report.info("packageGuid", "包内 GUID %d 个（扫描 %d 个 .meta）" % (len(found), scanned))
    else:
        report.warn("packageGuid", "未能读取包内 GUID，包引用会被误判为悬空")
    return found


def check_dangling_guids(root, files, guids, spec, report):
    allow = {g.lower() for g in spec.get("builtinGuidAllowlist", [])}
    known = guids | allow
    dangling, referenced, scanned = {}, set(), 0
    for rel in files:
        ext = os.path.splitext(rel)[1].lower()
        if ext not in TEXT_EXT:
            continue
        try:
            with open(os.path.join(root, rel), "r", encoding="utf-8", errors="ignore") as f:
                txt = f.read()
        except OSError:
            continue
        scanned += 1
        for g in GUID_RE.findall(txt):
            g = g.lower()
            referenced.add(g)
            if g not in known:
                dangling.setdefault(g, []).append(rel)
    level = "warn" if spec.get("danglingGuidLevel", "warn") == "warn" else "error"
    if dangling:
        detail = ["%s <- %s" % (g, ", ".join(v[:3])) for g, v in list(dangling.items())[:15]]
        report.add(level, "danglingGuid", "%d/%d 个被引用 GUID 无法解析（扫描 %d 个资源文件）"
                   % (len(dangling), len(referenced), scanned), files=detail)
    else:
        report.info("danglingGuid", "%d 个被引用 GUID 全部可解析（扫描 %d 个资源文件）"
                    % (len(referenced), scanned))


def check_absolute_paths(root, files, report):
    hits = []
    for rel in files:
        ext = os.path.splitext(rel)[1].lower()
        if ext not in TEXT_EXT and ext not in (".cs", ".json", ".md", ".txt"):
            continue
        try:
            with open(os.path.join(root, rel), "r", encoding="utf-8", errors="ignore") as f:
                txt = f.read()
        except OSError:
            continue
        for m in ABS_PATH_RE.findall(txt):
            if m[1] == ":" and re.match(r"^[A-Za-z]:[\\/](Users|Program Files|Windows|ProgramData)", m):
                hits.append("%s: %s" % (rel, m))
    if hits:
        report.warn("absolutePath", "%d 处机器绝对路径（换机/协作会失效）" % len(hits), files=hits[:20])
    else:
        report.info("absolutePath", "未发现机器绝对路径")


def check_trace(root, files, freeze, spec, report):
    if not freeze or not spec.get("requireFrozenDesign"):
        report.info("trace", "未要求可追溯覆盖率，跳过")
        return
    ids = [i.get("id") for i in freeze.get("items", []) if i.get("confirmed")]
    if not ids:
        report.info("trace", "冻结清单无 confirmed 条目，跳过")
        return
    found = set()
    for rel in files:
        if not rel.endswith(".cs"):
            continue
        try:
            with open(os.path.join(root, rel), "r", encoding="utf-8", errors="ignore") as f:
                head = f.read()
        except OSError:
            continue
        for grp in TRACE_RE.findall(head):
            for g in grp.split(","):
                found.add(g.strip())
    covered = [i for i in ids if i in found]
    pct = 100.0 * len(covered) / len(ids)
    missing = [i for i in ids if i not in found]
    msg = "可追溯覆盖率 %d/%d = %.0f%%" % (len(covered), len(ids), pct)
    if missing:
        report.warn("trace", msg, hint="未覆盖：" + ", ".join(missing[:15]))
    else:
        report.info("trace", msg)


def check_compile_log(root, log_path, report):
    if not log_path or not os.path.isfile(log_path):
        report.info("compileLog", "未提供编译日志，跳过", log_path or "")
        return
    with open(log_path, "r", encoding="utf-8", errors="ignore") as f:
        txt = f.read()
    errs = [ln for ln in txt.splitlines() if "error CS" in ln]
    nres = [ln for ln in txt.splitlines() if "NullReferenceException" in ln]
    if errs:
        report.error("compileLog", "编译日志含 %d 条 C# 编译错误" % len(errs), log_path, files=errs[:15])
    elif nres:
        report.warn("compileLog", "编译日志含 %d 条空引用异常" % len(nres), log_path, files=nres[:10])
    else:
        report.info("compileLog", "编译日志 0 条 C# 错误、0 条空引用", log_path)


def apply_ignores(report, spec):
    """把 spec.ignore 命中的条目降级为 info，并在 hint 里留下抑制依据。"""
    rules = [(i.get("check"), i.get("path")) for i in spec.get("ignore", [])]
    if not rules:
        return
    out = []
    for it in report.items:
        hit = None
        for c, p in rules:
            if it["check"] == c and (p == it["path"] or p in it["files"]):
                hit = p
                break
        if hit:
            it = dict(it)
            it["level"] = "info"
            it["message"] = "（按规格抑制）" + it["message"]
            it["hint"] = ((it["hint"] + " | ") if it["hint"] else "") + "spec.ignore: " + str(hit)
        out.append(it)
    report.items = out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("project", nargs="?", default=".")
    ap.add_argument("--spec", default="docs/project-spec.json")
    ap.add_argument("--freeze", default="docs/design-freeze.json")
    ap.add_argument("--compile-log", default="")
    ap.add_argument("--json", default="docs/unity-check-report.json")
    args = ap.parse_args()

    root = os.path.abspath(args.project)
    report = Report()

    spec = read_json(os.path.join(root, args.spec), report, "spec")
    if spec is None:
        emit(report, root, args.json)
        return 1
    report.info("spec", "已加载规格文件: %s" % os.path.basename(args.spec), args.spec)

    freeze = None
    fp = os.path.join(root, args.freeze)
    if os.path.isfile(fp):
        freeze = read_json(fp, report, "freeze")
    else:
        report.warn("freeze", "未找到设计冻结清单", args.freeze)

    files, dirs = walk_assets(root)

    check_layout(root, spec, report)
    check_version(root, report)
    check_manifest(root, report)
    check_meta_pairing(root, files, dirs, report)
    guids = collect_guids(root, report)
    guids |= collect_package_guids(root, report)
    check_scenes(root, spec, report)
    check_csharp(root, files, report)
    check_dangling_guids(root, files, guids, spec, report)
    check_absolute_paths(root, files, report)
    check_trace(root, files, freeze, spec, report)
    check_compile_log(root, args.compile_log and os.path.join(root, args.compile_log), report)

    apply_ignores(report, spec)
    emit(report, root, args.json)
    return 0 if report.errors == 0 else 1


def emit(report, root, json_path):
    data = {
        "projectDir": root.replace("/", "\\"),
        "generatedAt": datetime.now().strftime("%Y-%m-%d %H:%M:%S"),
        "generator": "Tools/check_project.py",
        "errorCount": report.errors,
        "warningCount": report.warnings,
        "items": report.items,
    }
    out = os.path.join(root, json_path)
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    with open(out, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
    print("[check] %d 错误 / %d 警告 -> %s" % (report.errors, report.warnings, json_path))
    for it in report.items:
        if it["level"] != "info":
            print("  %-7s %-14s %s" % (it["level"].upper(), it["check"], it["message"]))


if __name__ == "__main__":
    sys.exit(main())
