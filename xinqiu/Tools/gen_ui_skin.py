"""生成「中转站」UI 皮肤贴图（无烘焙文字、可直接做 9 宫格拉伸）。

用途：
  1. 概念图（05_UI界面/04_中转站_垃圾接收.png）里的拆件大多带烘焙文字，
     直接拉到 uGUI 面板上会和运行时文字重叠，所以这里按同一套配色重画一套干净拆件。
  2. 输出到 Assets/Art/UI/中转站皮肤/，文件名 = 语义，美术后续直接覆盖同名 PNG 即可
     （9 宫格边距由 Assets/Scripts/Editor/UiArtImportSettings.cs 统一写入，不用手调）。

跑法：python Tools/gen_ui_skin.py
"""
from PIL import Image, ImageDraw
import os

SS = 4  # 超采样倍数，保证圆角/描边干净
OUT = os.path.join(os.path.dirname(__file__), "..", "Assets", "Art", "UI", "中转站皮肤")

PANEL = (31, 28, 25, 255)
PANEL_EDGE = (62, 58, 50, 255)
TEAL = (61, 214, 192)
TEAL_HI = (86, 232, 210)
TEAL_LO = (42, 176, 158)
AMBER = (238, 187, 85)


def canvas(w, h, scale=SS):
    return Image.new("RGBA", (w * scale, h * scale), (0, 0, 0, 0))


def rrect(d, box, r, fill=None, outline=None, width=1):
    d.rounded_rectangle(box, radius=r, fill=fill, outline=outline, width=width)


def save(img, name, w, h):
    img = img.resize((w, h), Image.LANCZOS)
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, name))
    print("  ", name, w, "x", h)


def panel():
    """主面板：深色底 + 1px 描边 + 内层高光，圆角 14（9 宫格边距 24）"""
    w = h = 160
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 14 * s, fill=PANEL, outline=PANEL_EDGE, width=2 * s)
    rrect(d, (3 * s, 3 * s, w * s - 3 * s, h * s - 3 * s), 12 * s, outline=(255, 255, 255, 10), width=s)
    save(img, "pn_主面板.png", w, h)


def card():
    """内层卡片：比面板略亮一点的半透明白，圆角 10（边距 20）"""
    w = h = 128
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 10 * s, fill=(38, 35, 32, 255), outline=(255, 255, 255, 12), width=s)
    save(img, "pn_卡片.png", w, h)


def list_row(name, fill, edge):
    """列表行底：圆角 8（边距 12）"""
    w, h = 128, 64
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=fill, outline=edge, width=s)
    save(img, name, w, h)


def button(name, r, fill_top, fill_bottom, edge=None, edge_w=1, inner_hi=None):
    """按钮：上下渐变 + 可选描边，圆角 8（边距 16）"""
    w, h = 160, 96
    img = canvas(w, h)
    s = SS
    grad = Image.new("RGBA", (1, h * s), (0, 0, 0, 0))
    for y in range(h * s):
        t = y / (h * s - 1)
        c = tuple(int(fill_top[i] + (fill_bottom[i] - fill_top[i]) * t) for i in range(4))
        grad.putpixel((0, y), c)
    grad = grad.resize((w * s, h * s), Image.NEAREST)
    mask = canvas(w, h)
    md = ImageDraw.Draw(mask)
    rrect(md, (0, 0, w * s - 1, h * s - 1), r * s, fill=(255, 255, 255, 255))
    img.paste(grad, (0, 0), mask.split()[3])
    d = ImageDraw.Draw(img)
    if inner_hi is not None:
        rrect(d, (s, s, w * s - s, h * s - s), (r - 1) * s, outline=inner_hi, width=s)
    if edge is not None:
        rrect(d, (0, 0, w * s - 1, h * s - 1), r * s, outline=edge, width=edge_w * s)
    save(img, name, w, h)


def badge():
    """状态徽章：胶囊（边距 18）"""
    w, h = 120, 40
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 20 * s, fill=(*TEAL, 26), outline=(*TEAL, 96), width=s)
    save(img, "cp_状态徽章.png", w, h)


def bar_bg():
    w, h = 48, 16
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=(0, 0, 0, 105), outline=(255, 255, 255, 16), width=s)
    save(img, "pg_条_底.png", w, h)


def bar_fill(name, c1, c2):
    """填充条：Image.Type.Filled 用，不设 9 宫格边距"""
    w, h = 48, 16
    img = canvas(w, h)
    s = SS
    grad = Image.new("RGBA", (1, h * s))
    for y in range(h * s):
        t = y / (h * s - 1)
        grad.putpixel((0, y), tuple(int(c1[i] + (c2[i] - c1[i]) * t) for i in range(3)) + (255,))
    grad = grad.resize((w * s, h * s), Image.NEAREST)
    mask = canvas(w, h)
    md = ImageDraw.Draw(mask)
    rrect(md, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=(255, 255, 255, 255))
    img.paste(grad, (0, 0), mask.split()[3])
    save(img, name, w, h)


def divider():
    w, h = 64, 2
    img = Image.new("RGBA", (w, h), (255, 255, 255, 22))
    save(img, "de_分隔线.png", w, h)


def close_key():
    w = h = 48
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=(255, 255, 255, 10), outline=(255, 255, 255, 46), width=s)
    save(img, "bt_关闭方键.png", w, h)


def scroll_track():
    w, h = 16, 64
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=(255, 255, 255, 12))
    save(img, "pg_滚动条底.png", w, h)


def handle():
    """滑块手柄：竖向胶囊（不拉伸，直接按尺寸用）"""
    w, h = 16, 28
    img = canvas(w, h)
    d = ImageDraw.Draw(img)
    s = SS
    rrect(d, (0, 0, w * s - 1, h * s - 1), 8 * s, fill=(*TEAL, 235), outline=(255, 255, 255, 90), width=s)
    save(img, "cp_滑块手柄.png", w, h)


if __name__ == "__main__":
    print("生成中转站皮肤 ->", os.path.abspath(OUT))
    panel()
    card()
    list_row("pn_列表行.png", (46, 43, 39, 255), (255, 255, 255, 10))
    list_row("pn_列表行_亮.png", (40, 52, 49, 255), (*TEAL, 90))
    button("bt_主按钮.png", 8, (*TEAL_HI, 255), (*TEAL_LO, 255), inner_hi=(255, 255, 255, 60))
    button("bt_次按钮.png", 8, (*AMBER, 24), (*AMBER, 16), edge=(*AMBER, 170), edge_w=1)
    button("bt_幽灵按钮.png", 8, (255, 255, 255, 9), (255, 255, 255, 6), edge=(255, 255, 255, 34))
    button("bt_危险按钮.png", 8, (232, 96, 74, 26), (232, 96, 74, 18), edge=(232, 96, 74, 160))
    badge()
    bar_bg()
    bar_fill("pg_条_填充_青.png", TEAL_HI, TEAL_LO)
    bar_fill("pg_条_填充_琥珀.png", (245, 205, 120), AMBER)
    divider()
    close_key()
    scroll_track()
    handle()
    print("完成")
