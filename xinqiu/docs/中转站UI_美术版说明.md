# 中转站 UI · 美术版重排说明

> 更新时间：2026-11-16。范围：`Panel_Station` + `Panel_Quantity` 两块面板的排版与美术。
> **没有改动任何玩法 / 核心代码**：`StationSystem`、`WhiteboxGame`、`WhiteboxCanvasHud`、`StationQuantityDialog` 全部原样，
> 所有功能对象沿用原名字（代码按名字找回），按钮 OnClick 依旧持久化绑到 `WhiteboxUiActions`。

## 排版结构（1920×1080，面板 1680×840 居中）

```
┌ 头部 ─ 标题「垃圾中转站」+ 英文副标 ····· 运营中徽章 · 关闭(×)
├ 状态条 ─ 已修复状态 / 背包 Lv / 总容量（Txt_Repaired）
├ 卡片A 玩家背包 ─ 4 类行（图标/名称/容量条/数量/上限）+ 汇总 + ①投放
├ 卡片B 材料库存 ─ 4 格（图标/名称/数量）· 材料加工 4 行（按钮）
├ 卡片C 产品制造 ─ 3 行（图标+按钮）· 工具与背包（④⑤ 按钮）
├ 后台任务 ─ JobBar + JobFill + Txt_Job
└ 底部 ─ 提示文字 + 版本标注
```

## 美术换图（两种，都不用改代码）

1. **改单张图**：Hierarchy 里选中任意 `Img_*` 节点，在 Inspector 把新的 Sprite 拖进 Image 的
   **Source Image** 即可。9 宫格边距在 Sprite 的导入设置里，换图时保持同名边距习惯就行。
2. **整套换皮**：直接覆盖 `Assets/Art/UI/中转站皮肤/` 下的同名 PNG，
   然后跑一次菜单 `Tools/垃圾星球白膜/⑥ 应用 UI 贴图导入设置`（会重新写入 9 宫格边距）。

皮肤 PNG 由 `Tools/gen_ui_skin.py` 生成（无烘焙文字、可直接 9 宫格拉伸），要换配色/圆角
改脚本重跑即可；拆件图标来自 `Assets/Art/UI/垃圾星球_UI资源库/`。

## 布局调整 / 重建

- 菜单：`Tools/垃圾星球白膜/⑤ 重排中转站 UI（美术版）`（幂等，随时可重跑）。
- 批处理：
  ```
  Unity.exe -batchmode -quit -projectPath <工程>
    -executeMethod TrashPlanet.Whitebox.Editor.StationUiBuilder.RebuildBatch -logFile Logs/rebuild.log
  ```
- 离线预览（重建 + 校验 + 渲出 PNG 到 docs/）：
  `TrashPlanet.Whitebox.Editor.StationUiPreview.RenderBatch`

## 新增文件

| 文件 | 作用 |
| --- | --- |
| `Assets/Art/UI/中转站皮肤/*.png` | 干净拆件（面板/卡片/行/按钮/徽章/进度条/分隔线/滑块手柄） |
| `Assets/Scripts/Editor/StationUiBuilder.cs` | 美术版面板构建器（幂等、可批处理） |
| `Assets/Scripts/Editor/StationUiPreview.cs` | 离线渲染预览图 + 重建后校验 |
| `Assets/Scripts/Editor/UiArtImportSettings.cs` | 统一 Sprite 导入设置与 9 宫格边距 |
| `Assets/Scripts/UI/StationBoardView.cs` | 纯展示数据填充（只读 State，只写文本/进度，可安全删除） |
| `Tools/gen_ui_skin.py` | 皮肤贴图生成脚本 |
| `Tools/mock_station.py` | 排版预览（PIL 先行图，调坐标用） |

## 注意

- `Btn_BagUpgrade` 是 `Panel_Station` 的**直接子物体**（`WhiteboxValidator` 用 `transform.Find` 检查），
  视觉上仍落在「工具与背包」区块，移动它请用绝对坐标。
- 面板里任何「美术想换图」的元素都叫 `Img_*`；功能按钮/文本沿原命名，不要改名。
- `docs/preview-station.png`、`docs/preview-quantity.png` 是离线渲染的成品图，可直接放进设计评审。

## 文字为什么糊 / 怎么变清晰（2026-11-16 修）

现象：面板在 1080p 下文字发灰发虚，小字（材料汇总、副标、提示）几乎糊成一团。

原因（三条叠加，没有 bug）：

1. **字号太小**。原排版中文正文只有 11–13px、辅助说明 9–10px。CJK 在 13px 以下笔画只有 1px 宽，
   再叠灰度抗锯齿，字形本身就是「糊」的，这是最主因。
2. **对比度不足**。灰字亮度 0.58 压在 `#1F1C19` 深底上，对比度约 3.5:1，笔画边缘和背景几乎连成一片。
3. **字体是内置 `LegacyRuntime.ttf`**（Liberation Sans），本身没有中文字形，中文靠系统字体回退渲染，
   小字号下没有 hinting、笔画粗细不匀。
   另外 uGUI 的 `Text` 是按 `fontSize` 光栅化字形再交给 Canvas 变换缩放：
   Canvas 参考分辨率是 1920×1080，**只有窗口正好 1080p 时缩放系数才是 1.0（1:1 逐像素）**；
   Game 视图拉小（缩放 0.3 之类）或 4K（缩放 2.0）都会重采样，必然比 1:1 更虚。
   → 判断清晰度请把 Game 视图设成 **1920×1080** 再看，别在小窗口里下结论。

本次改法（`StationUiBuilder.cs`，只动排版参数，玩法/对象名/绑定全不变）：

| 位置 | 改前 | 改后 |
| --- | --- | --- |
| 面板主标题 | 29 | 32 |
| 卡片标题 / 英文副标 | 16 / 9 | 18 / 10 |
| 状态条正文 | 14 | 15 |
| 背包类别名 / 数量 / 上限 | 15 / 20 / 12 | 17 / 22 / 14 |
| 材料名 / 数值 / 单位 | 11 / 19 / 10 | 13 / 21 / 12 |
| 加工/制造/升级按钮 | 13 | 15 |
| 区块标题 | 14 | 16 |
| 说明文字（提示/底部/版本） | 10–12 | 12–13 |
| 正文灰（TextDim） | 0.58 | 0.71 |
| 次要文字（TextSoft） | 0.79 | 0.875 |

另外新增 `Assets/Scripts/Editor/UiFont.cs`：两个构建器的 `LoadFont()` 都改成走它。

- 它会优先用 `Assets/Art/Fonts/` 下**任意** `.ttf/.otf`，找不到才退回内置字体；
- 想再清晰一档，把一份带 hinting 的中文字体丢进 `Assets/Art/Fonts/`（推荐思源黑体 / Noto Sans SC，
  OFL 可商用），重跑 ⑤ 即可，**不用改代码、不用逐个 Text 拖引用**；
- 换字体前先把内置字体的中文回退行为当成基线，换完对比一次 `docs/preview-station.png`。

如果以后要求「4K / 任意缩放下都锐利」，只剩一条路：换成 TextMeshPro（SDF 字形，纹理分辨率与缩放解耦）。
那会改动 `WhiteboxCanvasHud` 里的 `Text` 字段类型与数量弹窗的 `InputField`，属于另一档改造，本轮没做。
