#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""补齐 Assets 下缺失的 .meta（新增脚本/目录/文本资产时用）。

用法（在工程根目录执行，或把工程路径作为第一个参数）：
    python Tools/ensure_meta.py [工程根目录]

只补缺失项，不覆盖已有 .meta；GUID 用 uuid4 生成，并与工程里已有 GUID 去重。
生成目录/资源都由 Unity 的 AssetDatabase 生成时会自动补写，本脚本是离线兜底，
用于「编辑器还没刷新但要先跑工程自检」的场景。
"""
import os
import sys
import uuid
import re

SKIP_DIRS = {".git", ".vs", "Library", "Temp", "Logs", "Obj", "UserSettings", "Build", "Builds"}
GUID_RE = re.compile(r"^guid:\s*([0-9a-fA-F]{32})\s*$", re.M)

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
CS_META = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
TEXT_META = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
DEFAULT_META = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEXT_EXTS = {".txt", ".json", ".bytes", ".csv", ".xml", ".html", ".htm", ".md"}


def collect_guids(root):
    used = set()
    for base, dirs, files in os.walk(root):
        dirs[:] = [d for d in dirs if d not in SKIP_DIRS]
        for f in files:
            if not f.endswith(".meta"):
                continue
            try:
                with open(os.path.join(base, f), "r", encoding="utf-8", errors="replace") as fh:
                    m = GUID_RE.search(fh.read())
                if m:
                    used.add(m.group(1).lower())
            except OSError:
                pass
    return used


def new_guid(used):
    while True:
        g = uuid.uuid4().hex
        if g not in used:
            used.add(g)
            return g


def template_for(path, is_dir):
    if is_dir:
        return FOLDER_META
    ext = os.path.splitext(path)[1].lower()
    if ext == ".cs":
        return CS_META
    if ext in TEXT_EXTS:
        return TEXT_META
    return DEFAULT_META


def main():
    project = sys.argv[1] if len(sys.argv) > 1 else os.getcwd()
    assets = os.path.join(project, "Assets")
    if not os.path.isdir(assets):
        print("找不到 Assets 目录：", assets)
        return 2

    used = collect_guids(project)
    created = 0
    for base, dirs, files in os.walk(assets):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS)
        for name in sorted(dirs):
            p = os.path.join(base, name)
            meta = p + ".meta"
            if os.path.exists(meta):
                continue
            with open(meta, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(template_for(p, True).format(guid=new_guid(used)))
            created += 1
        for name in sorted(files):
            if name.endswith(".meta") or name.startswith("."):
                continue
            p = os.path.join(base, name)
            meta = p + ".meta"
            if os.path.exists(meta):
                continue
            with open(meta, "w", encoding="utf-8", newline="\n") as fh:
                fh.write(template_for(p, False).format(guid=new_guid(used)))
            created += 1

    print("补齐 .meta：%d 个（已有 GUID %d 个，全部唯一）" % (created, len(used)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
