# 新手说明浮层 · 布局设计稿

> **状态**：布局已确认，**暂不实施**（2026-11-24）。代码未做任何改动。
> **来源**：用户提供的 `10_任务日志.png` 风格参考；在既有任务日志视觉语言上改造为全屏新手说明。
> **定位**：游戏启动时覆盖屏幕的文本说明，10 秒后自动消失，讲清移动方式、游戏玩法、功能打开方式。

---

## 1. 视觉规范

沿用任务日志面板的视觉语言，保证与现有 UI 一致：

| 项 | 值 |
|---|---|
| 主卡片底色 | `#fbf7ec`（米白） |
| 卡片圆角 | `20px` |
| 卡片边框 | `0.5px solid #e6dcc6` |
| 背景遮罩 | `rgba(10, 14, 12, 0.55)` 压暗游戏画面 |
| 卡片尺寸 | 屏幕宽 `86%` × 高 `90%`，水平垂直居中 |
| 强调青绿 | `#2bae9e`（标题圆点、完成态、进度条、统计数字） |
| 强调橙 | `#e8952f` / `#e8a33d`（倒计时数字、按钮、当前项） |
| 主文本 | `#4a443a` |
| 次文本 | `#8a8375` / `#948c7d` |
| 虚线分隔 | `0.5px dashed #e5dccb` |
| 高亮条目底 | `#fdf3dc` |
| 字体 | 与任务日志一致（运行时 OS 中文字体回退，不引入新字体） |

## 2. 布局结构

```
全屏遮罩
└─ 米白主卡片
   ├─ 顶部：3px 青绿进度条（倒计时可视化）
   ├─ 标题栏：青绿圆点 + "新手指南" + 右侧"停留 10 秒 · 任意键跳过"
   ├─ 主体（左右分栏）
   │  ├─ 左栏（约 186px，虚线右边界）
   │  │  ├─ 阅读剩余：橙色大号 "10 秒" + 5 个进度点
   │  │  ├─ 统计块：5 条说明 / 3 个板块
   │  │  ├─ 当前重点：① 走起来 ② 捡起垃圾 ③ 回中转站 + 橙色进度条
   │  │  ├─ 橙色胶囊按钮"开始探索"
   │  │  └─ 注脚：核心循环 拾取 → 加工 → 修复
   │  └─ 右栏：5 条说明条目（序号圆点 / 标题 / 描述 / 进度 / 状态）
   └─ 底部：左侧跳过提示 + 右侧"跳过 / 开始游玩"胶囊按钮
```

## 3. 右栏 5 条说明文案

| # | 标题 | 描述 | 状态 |
|---|---|---|---|
| 1 | 移动与视角 | WASD 走路 · Shift 加速 · 鼠标看向四周 | 完成 |
| 2 | 拾取与拆解 | 准星对准垃圾左键拾取 · 大型垃圾多次拆解 | 完成 |
| 3 | 伙伴狗 | 身后跟随 · 自动叼回附近垃圾 | 进行中（高亮） |
| 4 | 中转站 | 按 M 开门 · 存入 → 加工 → 制造 → 修复 | 待了解 |
| 5 | 功能面板 | J 任务 · G 垃圾包 · I 工具包 · B 交易 · R 转盘 | 待了解 |

> 条目右侧的"完成 / 进行中 / 待了解"仅为视觉读数，表示"读到哪一条"，**不写入 `questStep`**，与十步任务链完全解耦。

## 4. 工程落地映射（待实施时按此执行）

| 位置 | 改动 |
|---|---|
| `docs/design-freeze.json` + `Whitebox_Spec_Notes.txt` | 新增 `UI-010`「全屏新手说明浮层」，`version` 25 → 26，更新 `scopeNote`；脚本头写 `// @trace UI-010` |
| `Assets/Scripts/UI/IntroPanelView.cs` | 新增：显隐 + `unscaledDeltaTime` 倒计时 + 淡出；暴露 `IsOpen` |
| `Assets/Scripts/Data/WhiteboxConfig.cs` + `Assets/Data/WhiteboxConfig.asset` | 新增 `introSeconds=10`、`introLines`（5 条）、`introShowOnlyOnNewGame`；`CreateDefault()` 兜底，`OnValidate` 钳制 |
| `Assets/Scripts/Editor/WhiteboxUiBuilder.cs` | `EnsureUiInScene` 幂等补建 `Panel_Intro` 全套控件；`SessionKey` v3 → v4 |
| `Assets/Scripts/UI/WhiteboxCanvasHud.cs` | `AutoResolveRefs()` 补 `IntroOverlay` 绑定 |
| `Assets/Scripts/Boot/WhiteboxGame.cs` | 加 `WireIntro(IntroPanelView v)`（不改 `Wire()` 主签名）；`Start()` 中 `ApplyUiState()` 后调 `intro.Show(config.IntroSeconds)` |
| `Assets/Scripts/Editor/WhiteboxValidator.cs` | 加校验：面板存在、引用非空、`introSeconds>0` |

**红线提醒**：
- 不手写 `.unity` / `.prefab` 文本；Builder 幂等补齐，不动玩家双手与用户调过的 UI 格式。
- 遮罩 Image 关闭 Raycast Target，否则吃掉点击。
- 旧底栏 `Txt_Hint`（`WhiteboxHud.cs:186-189`、`WhiteboxCanvasHud.cs:405`）仍跑 12 秒，浮层显示时应隐藏，避免两层提示。
- 倒计时用 `Time.unscaledDeltaTime`，避免 `timeScale=0` 时卡住。
- 若需"仅新档首次显示"，在 `WhiteboxState` 加 `introSeen`，`NormalizeLoaded` 补默认 `false`，**不升存档 version**。

## 5. 待拍板（实施前确认）

1. 每次进游戏显示，还是仅新档 / 首次显示？
2. 10 秒内是否冻结玩家移动（并入 `IsUiBlocking`）？推荐不冻结。
3. 是否支持按任意键 / 点击提前跳过？推荐支持。
4. 文案是否保持此版 5 条，还是增删？

> 第 1 项决定是否需要动 `WhiteboxState` 与存档；其余可按推荐默认先落地，后续改配置即可。
