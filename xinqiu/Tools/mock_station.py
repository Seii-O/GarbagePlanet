"""按 C# 构建脚本里的同一套坐标，用 PIL 先渲一张 1920x1080 的排版预览图。
用途：改坐标前先看效果，避免在 Unity 里盲调。输出到 Logs/_station_mock.png（不参与工程）。
"""
from PIL import Image, ImageDraw, ImageFont
import os

ROOT = os.path.join(os.path.dirname(__file__), "..")
SKIN = os.path.join(ROOT, "Assets", "Art", "UI", "中转站皮肤")
ICONS = os.path.join(ROOT, "Assets", "Art", "UI", "垃圾星球_UI资源库")

W, H = 1920, 1080
TEAL = (61, 214, 192)
AMBER = (238, 187, 85)
TEXT = (237, 234, 227)
MUTED = (150, 143, 131)
DIM = (110, 104, 95)

FONT = "C:/Windows/Fonts/msyh.ttc"
FONT_B = "C:/Windows/Fonts/msyhbd.ttc"


def f(size, bold=False):
    try:
        return ImageFont.truetype(FONT_B if bold else FONT, size)
    except OSError:
        return ImageFont.truetype(FONT, size)


def sprite(name, folder=SKIN):
    p = os.path.join(folder, name)
    return Image.open(p).convert("RGBA")


def put(img, name, box, folder=SKIN, tint=None, alpha=1.0):
    x, y, w, h = box
    s = sprite(name, folder)
    s = s.resize((int(w), int(h)), Image.LANCZOS)
    if tint:
        r, g, b, a = s.split()
        solid = Image.new("RGBA", s.size, tint + (255,))
        from PIL import ImageChops
        s = Image.composite(solid, Image.new("RGBA", s.size, (0, 0, 0, 0)), a.point(lambda v: v))
        s.putalpha(a.point(lambda v: int(v * (tint[3] if len(tint) > 3 else 255) / 255)))
    if alpha < 1.0:
        a = s.split()[3].point(lambda v: int(v * alpha))
        s.putalpha(a)
    img.alpha_composite(s, (int(x), int(y)))


def text(d, xy, s, size, color=TEXT, bold=False, anchor="la"):
    d.text(xy, s, font=f(size, bold), fill=color, anchor=anchor)


base = Image.new("RGBA", (W, H), (0, 0, 0, 255))
# 背景：模拟游戏画面
for y in range(H):
    t = y / H
    base.paste(Image.new("RGB", (1, 1), (int(26 + 40 * (1 - t)), int(22 + 30 * (1 - t)), int(20 + 24 * (1 - t)))), (0, y))
d = ImageDraw.Draw(base, "RGBA")

PX, PY = 120, 120
PW, PH = 1680, 840


def pbox(x, y, w, h):
    return (PX + x, PY + y, w, h)


# ---------- 面板 ----------
put(base, "pn_主面板.png", (PX, PY, PW, PH))
d = ImageDraw.Draw(base, "RGBA")

# 角标
for cx, cy, ang in [(8, 8, 0), (PW - 8, 8, 90), (PW - 8, PH - 8, 180), (8, PH - 8, 270)]:
    img_corner = sprite("de_角标.png", os.path.join(ICONS, "03_界面拆件")).resize((16, 16), Image.LANCZOS).rotate(ang)
    base.alpha_composite(img_corner, (PX + cx - 8, PY + cy - 8))

# ---------- 头部 ----------
put(base, "ic_回收.png", pbox(24, 22, 44, 44), os.path.join(ICONS, "03_界面拆件"), tint=(255, 255, 255))
text(d, (PX + 84, PY + 16), "垃圾中转站", 29, TEXT, True)
text(d, (PX + 86, PY + 56), "TRANSFER  STATION   ·   SORT  ·  PROCESS  ·  CRAFT", 11, DIM)
put(base, "cp_状态徽章.png", pbox(PW - 24 - 36 - 16 - 132, 24, 132, 30))
d.ellipse((PX + PW - 24 - 36 - 16 - 132 + 16, PY + 37, PX + PW - 24 - 36 - 16 - 132 + 22, PY + 43), fill=TEAL)
text(d, (PX + PW - 24 - 36 - 16 - 132 + 34, PY + 3 + 24), "运营中", 12, TEAL)
put(base, "bt_关闭方键.png", pbox(PW - 24 - 36, 22, 36, 36))
text(d, (PX + PW - 24 - 18, PY + 25), "×", 20, TEXT, anchor="ma")
put(base, "de_分隔线.png", pbox(24, 92, PW - 48, 1))

# ---------- 状态条 ----------
put(base, "pn_列表行.png", pbox(24, 104, PW - 48, 42))
put(base, "ic_警示.png", pbox(24 + 12, 104 + 11, 20, 20), os.path.join(ICONS, "03_界面拆件"), tint=AMBER)
text(d, (PX + 24 + 42, PY + 104 + 11), "已修复: 否（先造 1 块再生建材板）  ·  背包 Lv.1/2（总 6/20）", 14, (201, 194, 180))

# ---------- 三张卡片 ----------
CY, CH = 160, 520
put(base, "pn_卡片.png", pbox(24, CY, 496, CH))
put(base, "pn_卡片.png", pbox(536, CY, 560, CH))
put(base, "pn_卡片.png", pbox(1112, CY, 544, CH))

# ---- 背包卡 ----
text(d, (PX + 42, PY + CY + 12), "玩家背包", 18, TEXT, True)
text(d, (PX + 42, PY + CY + 38), "BACKPACK", 9, DIM)
text(d, (PX + 24 + 496 - 18, PY + CY + 12), "6 / 20", 18, AMBER, True, anchor="ra")
cats = [("ic_类别_塑料.png", "塑料", "PLASTIC"), ("ic_类别_金属.png", "金属", "METAL"),
        ("ic_类别_有机.png", "有机", "ORGANIC"), ("ic_类别_电子.png", "电子", "E-WASTE")]
vals = [("6", "/10"), ("0", "/4"), ("0", "/4"), ("0", "/2")]
for i, (icon, name, en) in enumerate(cats):
    ry = CY + 68 + i * 86
    put(base, "pn_列表行.png", pbox(42, ry, 460, 80))
    put(base, icon, pbox(42 + 12, ry + 22, 36, 36), os.path.join(ICONS, "02_类别图标"))
    text(d, (PX + 42 + 58, PY + ry + 12), name, 15, TEXT)
    text(d, (PX + 42 + 58, PY + ry + 34), en, 9, DIM)
    put(base, "pg_条_底.png", pbox(42 + 58, ry + 62, 214, 6))
    if i == 0:
        put(base, "pg_条_填充_青.png", pbox(42 + 58, ry + 62, 150, 6))
    text(d, (PX + 42 + 366, PY + ry + 12), vals[i][0], 20, AMBER, True, anchor="ra")
    text(d, (PX + 42 + 374, PY + ry + 20), vals[i][1], 11, MUTED)
put(base, "de_分隔线.png", pbox(42, CY + 416, 460, 1))
text(d, (PX + 42, PY + CY + 426), "待加工垃圾  塑/金/有/电: 0/0/0/0", 12, MUTED)
put(base, "bt_主按钮.png", pbox(42, CY + 456, 460, 48))
text(d, (PX + 42 + 230, PY + CY + 456 + 12), "① 投放背包全部垃圾", 16, (18, 26, 25), True, anchor="ma")

# ---- 材料 / 加工卡 ----
text(d, (PX + 554, PY + CY + 12), "材料库存", 18, TEXT, True)
text(d, (PX + 554, PY + CY + 38), "MATERIALS", 9, DIM)
mats = [("ic_材料_再生塑料.png", "再生塑料", "12"), ("ic_材料_金属零件.png", "金属零件", "15"),
        ("ic_材料_有机原料.png", "有机原料", "2"), ("ic_材料_电子元件.png", "电子元件", "6")]
for i, (icon, name, v) in enumerate(mats):
    sx = 554 + (i % 2) * 264
    sy = CY + 66 + (i // 2) * 84
    put(base, "pn_列表行.png", pbox(sx, sy, 260, 76))
    put(base, icon, pbox(sx + 10, sy + 20, 36, 36), os.path.join(ICONS, "06_UI界面拆件/图标_材料与成品"))
    text(d, (PX + sx + 54, PY + sy + 12), name, 11, MUTED)
    text(d, (PX + sx + 54, PY + sy + 30), v, 19, AMBER, True)
    text(d, (PX + sx + 232, PY + sy + 36), "件", 10, DIM, anchor="ra")
put(base, "de_分隔线.png", pbox(554, CY + 240, 524, 1))
text(d, (PX + 554, PY + CY + 250), "② 材料加工", 14, TEAL, True)
text(d, (PX + 554 + 524, PY + CY + 252), "投入 ×N → 产物", 10, DIM, anchor="ra")
procs = ["加工塑料 ×5→再生塑料  可3次", "加工金属 ×4→金属零件  可2次",
         "加工有机 ×5→有机原料  可0次", "加工电子 ×2→电子元件  可0次"]
for i, p in enumerate(procs):
    ry = CY + 276 + i * 57
    put(base, "pn_列表行.png", pbox(554, ry, 524, 52))
    put(base, "ic_功能_加工.png", pbox(554 + 10, ry + 13, 26, 26), os.path.join(ICONS, "06_UI界面拆件/图标_功能"), tint=TEAL if i < 2 else (110, 104, 95))
    text(d, (PX + 554 + 44, PY + ry + 15), p, 13, TEXT if i < 2 else MUTED)
    text(d, (PX + 554 + 500, PY + ry + 15), "›", 18, MUTED, anchor="ra")

# ---- 制造 / 升级卡 ----
text(d, (PX + 1130, PY + CY + 12), "③ 产品制造", 14, TEAL, True)
text(d, (PX + 1130, PY + CY + 34), "CRAFT", 9, DIM)
crafts = [("ic_产品_再生建材板.png", "再生建材板（塑料板×2 金属×1）可1次"),
          ("ic_产品_生态肥料包.png", "生态肥料包（有机×3）可0次"),
          ("ic_产品_水质过滤器.png", "水质过滤器（塑料板+芯片）可0次")]
for i, (icon, label) in enumerate(crafts):
    ry = CY + 54 + i * 74
    put(base, "pn_列表行.png", pbox(1130, ry, 508, 68))
    put(base, icon, pbox(1130 + 10, ry + 14, 40, 40), os.path.join(ICONS, "06_UI界面拆件/图标_材料与成品"))
    text(d, (PX + 1130 + 58, PY + ry + 24), label, 12.5, TEXT if i == 0 else MUTED)
    text(d, (PX + 1130 + 492, PY + ry + 24), "›", 18, MUTED, anchor="ra")
put(base, "de_分隔线.png", pbox(1130, CY + 286, 508, 1))
text(d, (PX + 1130, PY + CY + 296), "④ 工具与背包", 14, TEAL, True)
put(base, "bt_次按钮.png", pbox(1130, CY + 320, 508, 48))
text(d, (PX + 1130 + 16, PY + CY + 320 + 13), "④ 升级工具（Lv.1 → 2）", 13, AMBER)
put(base, "bt_幽灵按钮.png", pbox(1130, CY + 376, 508, 48))
text(d, (PX + 1130 + 16, PY + CY + 376 + 13), "⑤ 升级背包（总 20 → 40，需 15 金属零件）", 13, (205, 198, 184))
text(d, (PX + 1130, PY + CY + 436), "升级后立即生效；材料不足时按钮会提示缺口", 10, DIM)

# ---------- 后台任务 ----------
put(base, "pn_列表行.png", pbox(24, 692, PW - 48, 44))
text(d, (PX + 40, PY + 692 + 14), "后台任务", 12, MUTED)
put(base, "pg_条_底.png", pbox(110, 692 + 18, 1330, 8))
put(base, "pg_条_填充_琥珀.png", pbox(110, 692 + 18, 598, 8))
text(d, (PX + PW - 40, PY + 692 + 14), "加工塑料 45%", 12, AMBER, anchor="ra")

# ---------- 底部 ----------
put(base, "de_分隔线.png", pbox(24, 748, PW - 48, 1))
text(d, (PX + 24, PY + 758), "M / ESC 关闭  ·  加工与制造会弹出数量选择  ·  按钮 OnClick 已持久化绑定到 WhiteboxUiActions", 12, DIM)
text(d, (PX + PW - 24, PY + 758), "中转站面板 · 美术版排版", 11, DIM, anchor="ra")

base.convert("RGB").save(os.path.join(ROOT, "Logs", "_station_mock.png"))
print("mock saved")
