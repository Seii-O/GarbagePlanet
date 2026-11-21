# 《垃圾星球》Unity 工程导览（PROJECT_GUIDE）

> **这份文档是什么**：给"现在的我"和"下一个接手的 AI / 开发者"看的工程说明书。用大白话讲清楚这是个什么项目、怎么玩、代码怎么组织、已有的"角色"是怎么做出来的，以及**要新增人物时该动哪些地方**。
>
> **生成日期**：2026-11-21　|　**工程路径**：`C:\Users\cj\Documents\GitHub\GarbagePlanet\xinqiu`　|　**Unity**：2022.3.62f3c1（2022.3 LTS 中国版）+ URP 14.0.12
>
> **口径约定**：本文件只写**在当前工程里能查到的事实**。凡是需要用户决定、代码里还没定论的内容，一律标注「**待补充**」或「**待拍板**」，不做臆测。设计初衷以 `Assets/垃圾星球_策划案_v1.2.md` 为准；实际数值以 `Assets/Data/*.asset` 配置为准（策划案是设计稿，代码可能已按迭代调整）。

---

## 0. 30 秒速览（先看结论）

| 项 | 结论 |
|---|---|
| 是什么 | 比赛参赛项目，命题「**同行**」；一人一狗在一颗小星球上捡垃圾、做环保、修复生态。**不做变现、免费原型**。 |
| 怎么玩 | 第一人称在球面行走 → 准星瞄准垃圾左键拾取/拆解 → 狗跟着并自动叼回 → 按 M 开中转站加工制造 → 生态三阶段变好 → 走完 10 步任务链结算。 |
| 主平台 | PC（Windows），键鼠，1920×1080；手机端是移植批次，本阶段未做。 |
| 唯一正式场景 | `Assets/Scenes/WhiteboxPrototype.unity`（Build Settings 第 0 位）。另一个 `Title.unity` 是标题场景。 |
| 代码语言/命名空间 | C#；玩法在 `TrashPlanet.Whitebox`，球面控制复用老代码 `PlanetCleaner`。 |
| 数值入口 | `Assets/Data/` 下 5 个 ScriptableObject 配置资产（白膜 / 抽卡 / 工具 / 背包 / 金币）。**数值不写死在脚本里**。 |
| 场景怎么建 | **不手写场景 / 预制体 YAML**；全部由 `Assets/Scripts/Editor/` 里的 Builder 用 Editor API 生成，菜单在 `Tools/垃圾星球白膜/*`。 |
| 已有的"角色" | 只有两个：**玩家**（第一人称，只有一双手 + 胶囊体）、**伙伴狗**（固定外观，不随阶段变化）。**没有可收集的"人物角色"系统**——见第 7 章。 |
| 验证状态 | 编辑器编译通过、41 项校验全 PASS、Play 冒烟 0 错误；但完整任务链、帧率/内存/手感**未实测**。 |
| 设计门禁 | `docs/design-freeze.json`：当前 `version 19 / frozen`（2026-11-20）。策划案正文与早期文档里写的 `version 4` 是历史状态，以实际文件为准。 |

---

## 1. 项目整体定位

- **一句话**：你是一名星球环保管理员，带着一条狗，站在一眼能看完的小星球上，把废弃物捡回中转站、加工成环保产品；地表、水、天空和光一点点变好，**只有狗从头到尾是同一个样子**。
- **主题落点**：比赛命题「同行」由这条狗承载——靠它的**位置与行为**表达陪伴，不靠对话、好感度、外观变化或常驻 UI。设计上有一句核心台词：**"星球一直在变，它一直没变"**。
- **体验目标**：看得见的修复进度 + 有人同行。不是战斗、不是生存压力，没有血条、没有小地图、没有常驻任务列表。
- **品类**：3D 低模 / 轻度探索 / 环境修复，第一人称，单个完整球形星球。

### 1.1 设计冻结与可追溯机制（重要，别绕过）

项目有一套"策划案 → 工程"的可追溯约定，接手时务必遵守：

- `docs/design-freeze.json` 是**机器可读的设计冻结清单**：`status: frozen`，里面 `items` 是已确认要做的需求（带编号如 `MECH-014`、`ART-012`），`deferred` 是待拍板，`deprecated` 是作废。当前是 `version 19`。
- 脚本文件头部用 `// @trace 编号` 标记"这条代码实现了哪条需求"。`Whitebox_Spec_Notes.txt` 是工程内的对照记录。
- **改动原则**：新增/变更需求 → 先改冻结清单并递增 `version` → 再改代码 → 保留 `@trace` 对应。别把没确认的东西写进 `items`，也别让代码里出现对不上编号的标记。
- 注意：策划案 v1.2 正文把 `ART-010`（狗三阶段外观）标为作废，工程确认遵守的是替代它的 `ART-012`（固定外观）。

---

## 2. 核心玩法循环

### 2.1 三层循环

| 层级 | 时间尺度 | 做什么 |
|---|---|---|
| 微循环 | 秒级 | 准星对准垃圾 → 白色描边亮起 → 左键 → 垃圾被拾取/耐久条变化 → 弹出 `+1`。 |
| 单局循环 | 分钟级 | 探索 → 拾取/拆解 → 回中转站（M）→ 存入 → 加工 → 制造 → 修复生态 → 继续探索。 |
| 长线循环 | 跨多局 | 工具升级（拾取距离 2/3/4 → 配置现为 4/6/8 m）、专精解锁（Q 轮换）、生态阶段 I→II→III、抽卡收集。 |

### 2.2 一局是怎么跑起来的（启动顺序，改代码必看）

`WhiteboxGame.Start()` 按**固定顺序**初始化，**顺序不能乱**（`Boot → Gameplay/Station/Environment/UI → Data → Core`）：

```
读存档 → 按配置重设星球尺寸 → 工具 ToolSystem → 背包 ToolBagSystem → 金币交易 CoinTradeSystem
→ 生态 EcoStage → 星球净化 PurificationSystem → 场景装饰 SceneryDecorSystem → HUD → 垃圾刷新 TrashSpawner
→ 准星交互 InteractionController → 狗 CompanionDog → 中转站 StationSystem
→ 任务链 QuestChain → 抽卡 GachaSystem → 应用 UI 状态
```

> 依赖要点：工具必须在交互/中转站/生态/抽卡**之前**可用；背包在工具之后（读 ToolConfig）；金币交易在背包之后（拿背包接口）；**星球净化必须在垃圾刷新之前**（初始撒点要靠 `IsPurified` 排除已净化土地）。

### 2.3 十步任务链（`Scripts/Station/QuestChain.cs`）

走完即结算，结算展示 6 项数据（垃圾清理数、材料加工数、产品制造数、土地/水源进度、总时长）：

1. 初次探索（移动≥3m 或捡到垃圾）→ 2. 拾取 5 个 → 3. 拆解 1 次大型垃圾 → 4. 靠近中转站按 M → 5. 造 1 块再生建材板修复中转站 → 6. 四类垃圾各加工一次 → 7. 造 1 个生态肥料包 → 8. 土地修复到目标 → 9. 水源修复到目标 → 10. 净化区覆盖整颗星球（结算）。

> 第 8/9 步按 MVP 裁剪建议做成「达到 `mvpProgressTarget`（配置默认 0.30）」而非 100%，属于 `[待拍板 C7]`。
> **第 10 步已改为"全星球净化"**：判定走 `game.Purification.FullyCovered`（净化半径到达对跖点 `πR`，即土地+水源进度都到 1.0）。旧的"双进度≥0.30 即结算"已被替换；旧场景无净化系统时自动降级回旧条件，任务链不会卡死。

---

## 3. 主要机制与系统

### 3.1 运行时系统一览（脚本 → 职责）

| 系统脚本 | 所在目录 | 一句话职责 |
|---|---|---|
| `WhiteboxGame` | Boot | 总装配与入口；固定顺序初始化所有系统，管按键仲裁、暂停、存档、重开。 |
| `WhiteboxState` | Core | 运行时状态 + 全部可复算公式 + 存档读写入口（背包、材料、进度、任务、抽卡、工具、金币、位置视角等全在这）。 |
| `WhiteboxConfig` | Data | **白膜唯一数值入口**：星球半径、刷新、拾取、加工、色板、狗参数……`OnValidate` 做钳制。 |
| `SphericalSurface` / `PlanetLayout` | Core | 球面数学工具：把物体贴到球壳上、切平面移动、法线朝向计算。 |
| `SaveSlotSystem` | Core | 5 槽 JSON 存档（`trash_planet_save_slot_N.json`），原子写入，兼容旧单文件档；`ActiveSlot=0` 时自动存档不落盘。 |
| `PlayerController` / `PlayerCameraRig` / `FirstPersonHands` | `_Game/Scripts/Player`（命名空间 `PlanetCleaner`） | 复用的老代码：球面切平面移动、第一人称相机俯仰、相机下的双手。 |
| `TrashItem` / `TrashSpawner` | Gameplay | 垃圾实体（类别/耐久/高亮）+ 对象池刷新（全局总量、并发上限、随机散落、冒出动画）。撒点时经 `IsPurified` 排除已净化土地；全星球净化后停止刷新。 |
| `InteractionController` | Gameplay | 相机中心射线 → 唯一目标 → 描边高亮 → 左键拾取/拆解；准星命中时同步耐久条。 |
| `CompanionDog` | Gameplay | **伙伴狗**：球面跟随 + 冷却到点自动叼回玩家准星没指着的垃圾；外观全程固定。 |
| `StationSystem` | Station | 中转站 2D 经营：存入 → 加工 → 制造 → 修复 → 升级；支持批量（数量弹窗）。 |
| `QuestChain` | Station | 十步任务链判定与结算触发。 |
| `PurificationSystem` | Environment | **星球净化区**：以中转站为中心，初始 5m、随进度沿球面扩到 `πR`；`IsPurified` 供撒点排除，`FullyCovered` 供结算；净化区扩张时回收区内旧垃圾；边界点纯视觉、无碰撞。进度与 EcoStage 同源。 |
| `EcoStage` | Environment | 三阶段环境表现：地表/水/天空/雾/主光角度平滑插值 + 阶段白光；**只碰地表和水两个 Renderer，绝不碰狗**。净化进度上升时它同步改善地表/天空/雾（与净化同源）。 |
| `SceneryDecorSystem` | Environment | 场景装饰（树/广告牌/水塘/土堆）随生态阶段变化的视觉联动，只读阶段、不写玩法数值；净化推进时植被同步复绿。 |
| `GachaSystem` / `GachaConfig` | Loot | 老虎机抽奖：拾取有概率发现"隐藏款"得券 → 走近场景老虎机、准星对准按 F → 弹出小型三转轮面板 → 权重+软/硬保底抽奖 → 奖励入库。 |
| `SlotMachine` / `GachaPanelView` | Loot / UI | `SlotMachine` 挂在场景老虎机实体上（交互半径 4m、准星高亮、F 入口）；`GachaPanelView` 是弹出的三转轮面板（滚动→依次停止→结算），关闭走 `Hide()` 彻底复位。 |
| `ToolSystem` / `ToolConfig` | Gameplay / Data | 工具单一服务：查询持有、加成钳制、专精解锁/轮换(Q)、统一发放入口 `Grant`。 |
| `ToolBagSystem` / `CoinTradeSystem` | Gameplay | 独立的工具/技能书背包（I 键）与金币交易（B 键），两条链路互不混用。 |
| UI 一族 | UI | `WhiteboxHud`（IMGUI 代理/回退）+ `WhiteboxCanvasHud`（uGUI 内容驱动）+ 各面板 View（转盘/背包/交易/存档/结算等）。 |

### 3.2 实际按键（`WhiteboxGame.Update()`，注意互斥优先级）

| 键 | 功能 | 键 | 功能 |
|---|---|---|---|
| WASD / Shift | 球面移动 / 加速 | 鼠标 / 左键 | 第一人称视角 / 拾取·拆解 |
| I | 工具背包（独立面板） | B | 金币交易（独立面板） |
| F（对准老虎机时） | 弹出老虎机抽奖面板 | M | 中转站（开/关） |
| ESC | 逐层关闭面板，最后暂停 | Q | 轮换已解锁专精 |
| F1 | 调试信息开关 | F5 / F9 | 重开一局 / 局内存档面板 |

> 抽奖面板**没有独立快捷键**：不存在"按 R  anywhere 打开转盘"。必须走近场景里的老虎机实体（`env_slotmachine_whitebox`，在中转站旁）、准星对准、按 F 才弹出；关闭后交互状态与转轮状态都会复位。
> **同帧只取一个面板键**，优先级：`背包(I) > 交易(B) > 中转站(M) > 暂停(ESC) > 专精(Q)`；老虎机走独立的准星交互链路（也接入同一套互斥 `OpenGachaFromMachine`）。开一个面板必关其他，杜绝面板叠加。打开任意面板会解锁光标、禁用玩家移动（`IsUiBlocking`）。

### 3.3 关键机制口径（代码实际值，多数为可配置默认值）

- **拾取/拆解**：拾取距离按工具等级 4/6/8 m（配置 `pickRange`），描边 `highlightRange=6 m`；大型垃圾耐久 5/8/10，每次点击伤害 1/1/2。
- **垃圾刷新**：地表并发上限 `mapTrashCap=1000`，全局总量 `totalTrashBudget=2000`（累计生成满则不再刷），开局铺设 `initialTrashCount=500`，刷新间隔三档均 10 s（`spawnInterval`）；`whiteboxSpawnTimeScale` 是演示加速倍率，**正式验证应调回 1**。已净化土地不再撒点（见下条）。
- **星球净化 `[待拍板]`**：初始半径 `purifyBaseRadius=5 m`（球面弧长），随进度 `(土地+水源)/2` 线性扩到 `π·R`（R=8 → πR≈25.1 m，覆盖整球）；`purifyShowBoundary=true` 显示边界点。进度=1（土地与水源都满）→ `FullyCovered` → 任务链第10步完成、地表停止刷新。进度同源驱动 `EcoStage`/`SceneryDecorSystem`，故地表、天空、雾、植被会随净化持续改善。
- **背包**：垃圾背包两级（Lv.1 合计 50 / Lv.2 合计 100）；工具背包是另一套"携带格子"。
- **生态阶段**：按 `(土地+水源)/2` 划分，阈值 0.30 / 0.70。
- **存档**：`Application.persistentDataPath` 下 5 槽 JSON，`version` 字段 + 读失败回退 + 旧档归一（`NormalizeLoaded`）。新增字段时保持 `version=1` 并在 `Normalize*` 里补默认值，避免升版本。

> ⚠️ 这些数值很多带 `[待拍板 C1~C13]` 标记（见策划案第 16 章），是可配置默认值而**非最终定稿**。帧率、内存、单局时长、球体半径仍是 `[待实测]`。

---

## 4. 关键场景 / 脚本 / 资源的作用

### 4.1 场景

| 场景 | 作用 |
|---|---|
| `Scenes/WhiteboxPrototype.unity` | **唯一正式关卡**，Build Settings 第 0 位。启动与重开都加载它。 |
| `Scenes/Title.unity` | 标题场景（由 `TitleBuilder` 接线按钮）。注：策划案提到"移除开始界面后唯一入口是本关"，标题场景当前是否仍在使用 → **待补充**。 |

**正式场景里的层级**（Hierarchy）：

```
WorldRoot
├─ env_planet_surface   球体（带 SphereCollider，供 PlayerController 自动量测半径；配置 R=8）
├─ env_west_water       污染水域
├─ chr_player_whitebox  玩家：CharacterController + 胶囊体 Body + Facing + PlayerController
├─ chr_dog_whitebox     伙伴狗（预制体实例）：CompanionDog
├─ env_station_whitebox 中转站（唯一建筑，暖色光）
├─ env_landmark_* ×4    四方向地标（枯树/广告牌/水塘等）
├─ env_decor_*          装饰（树/板/水塘/土堆，生态联动）
├─ env_trash_root       运行时生成的垃圾容器
├─ env_sun / env_fill_light / env_rim_light   主光 + 两盏补光
Main Camera
├─ PlayerCameraRig      第一人称相机
└─ Hands                第一双手（HandLeft / HandRight + FirstPersonHands）
sys_whitebox            所有系统组件挂这一根物体（WhiteboxGame 及各 System）
└─ fx_purify_boundary_* 净化区边界点（运行时生成、随进度扩张；无碰撞、无 TrashItem）
UI_Whitebox             Canvas(1920×1080) + 各面板 + 准星 + 浮字 + 耐久条
EventSystem             StandaloneInputModule（旧 Input Manager）
```

### 4.2 目录结构与分层

```
Assets/
├─ Scripts/
│  ├─ Boot/        WhiteboxGame（总入口）
│  ├─ Core/        WhiteboxState, SphericalSurface, PlanetLayout, SaveSlotSystem
│  ├─ Data/        WhiteboxConfig, ToolConfig, ToolBagConfig, CoinTradeConfig（均为 ScriptableObject 类）
│  ├─ Gameplay/    TrashItem, TrashSpawner, InteractionController, CompanionDog, ToolSystem, ToolBagSystem, CoinTradeSystem
│  ├─ Station/     StationSystem, QuestChain
│  ├─ Environment/ EcoStage, SceneryDecor, SceneryDecorSystem, PurificationSystem
│  ├─ Loot/        GachaConfig, GachaSystem
│  ├─ UI/          WhiteboxHud, WhiteboxCanvasHud, WhiteboxUiActions 及各面板 View
│  └─ Editor/      各 Builder（生成场景/UI/配置）、Validator、PlaySmoke、各种 Fix（仅编辑器编译）
├─ _Game/          复用的老代码：球面控制、相机 Rig、双手、白膜材质
├─ Data/           5 个配置资产 .asset
├─ Art/            模型(FBX)、材质(.mat)、UI 切图/图标、字体、HDR 天空盒等
├─ Prefabs/Whitebox/  10 个白膜预制体（7 垃圾 + 狗 + 中转站 + 地标）
├─ Scenes/         WhiteboxPrototype.unity, Title.unity
├─ 垃圾星球_策划案_v1.2.md   设计依据（阶段一交付稿）
└─ Whitebox_Spec_Notes.txt   规格对照（供 @trace 扫描）
docs/              冻结清单、项目规格、验证报告、UI 说明等
Tools/             check_project.py 等离线脚本（零第三方依赖）
```

> **为什么没有 asmdef**：脚本量 <30，且 Boot 层要复用预定义程序集里的 `PlanetCleaner.*`。加 asmdef 反而无法引用预定义程序集，故只用目录分层，`Editor/` 由 Unity 自动归编辑器程序集。这是明示偏差，脚本量上来后再拆。

### 4.3 配置资产（`Assets/Data/`）

| 资产 | 对应类 | 作用 |
|---|---|---|
| `WhiteboxConfig.asset` | `WhiteboxConfig` | 全局数值：星球、刷新、拾取拆解、工具专精、加工制造、狗、色板、光照、动效。 |
| `GachaConfig.asset` | `GachaConfig` | 隐藏款概率(0.08)、每次得券(1)、稀有度权重(70/25/5)、硬保底(20)、软保底、奖励池（默认 15 条）。 |
| `ToolConfig.asset` | `ToolConfig` | 10 件工具 + 4 本技能书的定义；购买配置当前全部 `enabled=false`（金币购买链路上线情况见 3.1 与待补充）。 |
| `ToolBagConfig.asset` | `ToolBagConfig` | 工具背包格子配置。 |
| `CoinTradeConfig.asset` | `CoinTradeConfig` | 金币交易配置（售价、限额、入口等）。 |

> 每个配置类都有 `CreateDefault()` 代码兜底：**场景没接引用时也能跑**，数值与资产一致。改数值优先改 `.asset`，不要改脚本常量。

### 4.4 预制体与材质

- `Prefabs/Whitebox/`：`prop_trash_{plastic,metal,organic,electronic}_whitebox`（4 类普通垃圾）、`prop_trash_large_{tire,metal_device,appliance}_whitebox`（3 类大型垃圾）、`chr_dog_whitebox`、`env_station_whitebox`、`env_landmark_whitebox`。
- 垃圾靠着色形状区分，**本体材质不参与阶段插值**；可交互时显示一个放大 1.0667 倍的白色 `Outline`（Unlit、`Cull=Front`，只渲染背面）——这是"去蓝环改白描边"后的现状。
- `Art/Materials/M_*_Whitebox.mat`：地面/水/垃圾/狗/中转站/锈/地标/玩家/双手/描边等 10 个材质。
- `Art/` 下还有大量第三方/AI 生成的模型与 UI 切图（太阳能行星包、垃圾桶/瓶/电池 FBX、成套 UI 图标）。**.font 注意**：目录里有 `字魂宋刻楷体 W5`、`字小魂萌趣苏打饼` 等标注「商用需授权」的字体，正式发布前需确认授权；当前中文 UI 靠运行时 OS 字体回退（`AutoFixCjkFont`）。

### 4.5 编辑器菜单入口（`Tools/垃圾星球白膜/*`）

| 菜单 | 作用 |
|---|---|
| ① 生成场景与预制体 | 重建白膜场景、配置、10 个预制体、10 个材质（**会重置场景内容**）。 |
| ② 打开白膜场景 | 打开 `WhiteboxPrototype.unity`。 |
| ③ 校验白膜工程 | 41 项校验，结果写 `docs/whitebox-validate.json`。 |
| ④ 生成 / 补齐 Hierarchy UI | 幂等，只补空引用，不覆盖你调过的布局。 |
| ⑤ 重排中转站 UI（美术版）/ 重跑 Play 冒烟 | 两个菜单同名序号，分属不同脚本；注意区分。 |
| ⑥ 应用 UI 贴图导入设置 / 刷新垃圾描边 | 前者在 `UiArtImportSettings`，后者在 `WhiteboxBuilder`，均序号 ⑥。 |
| ⑦ 增强立体感光照 / 应用播放器设置 | 分属 `WhiteboxBuilder` 与 `WhiteboxPlayerSettingsFix`。 |
| ⑧ 按配置重设星球半径 | 当前场景重设。 |
| ⑨ 生成老虎机配置、实体与面板 / ⑩ 重建老虎机面板与实体 / ⑫ 生成工具配置 | 抽卡（老虎机）与工具相关。⑨ 幂等：旧圆形转盘自动替换为场景老虎机实体 + 三转轮面板；脚本重编译后也会自动补齐一次。 |
| ⑬ 生成背包与交易界面 / ⑭ 接线标题场景 / ⑮ 生成多存档界面 / ⑯ 生成场景装饰 | 后续迭代新增的系统。 |
| ⑰ 生成星球净化系统并接线 | 幂等：在 `sys_whitebox` 挂 `PurificationSystem`，收集所有 `env_station_*` 作为净化中心并 Wire；不新建场景实体。脚本重编译后自动补齐一次。 |

> 序号有重复（多个脚本用了 ⑥⑦），以菜单文字为准。所有 Builder 都是 **Editor API 生成、不手写 YAML**，且尽量幂等。

---

## 5. 已有角色的组成与创建/接入流程

### 5.1 工程里到底有哪些"角色"

**只有两个**，且都不是"可收集的人物角色"：

| 角色 | 物体名 | 组成 | 脚本 |
|---|---|---|---|
| 玩家 | `chr_player_whitebox` + `Main Camera/Hands` | CharacterController + 胶囊体 Body + 朝向块 Facing + 一双手（HandLeft/HandRight）。**没有第三人称可见身体**。 | `PlanetCleaner.PlayerController`、`PlayerCameraRig`、`FirstPersonHands` |
| 伙伴狗 | `chr_dog_whitebox` | 1 组几何体（身体/头/4 腿/尾巴）+ 1 套固定材质 `#D8C3A0`，体高 ≤0.6。 | `TrashPlanet.Whitebox.CompanionDog` |

**狗的核心红线（ART-012 / v1.2 D6）**：外观全程恒定。`CompanionDog.Init()` 一次性把固定材质写进各 Renderer；之后 `EcoStage` 只改地表和水，**任何阶段切换都不碰狗的材质/Renderer**，没有 `UpdateLook()`、没有阶段色数组。改它的颜色只需改 `WhiteboxConfig.dogColorHex`（默认 `#D8C3A0`，对应待拍板 C13）。

### 5.2 一个角色是怎么"创建 + 接入"的（以狗为例）

这套流程是理解"新增人物该怎么做"的关键模板，全程在 Editor 脚本里完成：

1. **建预制体**（`WhiteboxBuilder.CreateDogPrefab`）：程序化拼几何体 → `AddComponent<CompanionDog>()` → 绑定 `FixedColorRenderers` / `FixedMaterial` → 存为 `Assets/Prefabs/Whitebox/chr_dog_whitebox.prefab`。
2. **放进场景**（`BuildSceneContents`）：`PrefabUtility.InstantiatePrefab` → 命名 → 挂到 `WorldRoot` → 用 `SphericalSurface.Place` 贴到球面 → 拿到 `CompanionDog` 引用。
3. **注入引用**（`WhiteboxGame.Wire(...)`）：把 dog 传给总入口的 `dog` 字段（序列化引用）。
4. **运行时初始化**（`WhiteboxGame.Start()` → `dog.Init(this)`）：狗拿到 game 引用，写死固定色，之后每帧 `Follow()` + `Fetch()`。
5. **单独系统**（如转盘/背包）走 `WireXxx()` 单独注入 + 运行时 `Init(this)`，不改 `Wire()` 主签名——这是为了兼容旧场景。

> 玩家角色同理：Builder 里建 `chr_player_whitebox` 与相机/双手 → `rig.Target/Player`、`hands.Player/CameraRig` 接线 → 总入口通过 `Wire` 拿到 `player`/`viewCamera`。**双手是单独红线**：`Main Camera/Hands` 下的 `HandLeft`/`HandRight` 与 `FirstPersonHands` 被校验器专门检查，常规改动不要动它们。

### 5.3 狗的具体行为（`CompanionDog.cs`）

- **跟随**：与玩家共用球面约束，保持在身后约 `dogFollowDistance=1.8 m`，用 Slerp 平滑贴球面、朝向玩家 forward。
- **叼回**：冷却到点（按阶段 `dogFetchCooldown=4/3/2 s`）→ 在半径 `dogFetchRadius=1.5/2.5/3.5 m` 内找最近垃圾 → **阶段一只叼塑料和有机，阶段二起四类** → 避开玩家准星当前目标 → 背包容量检查 → 入背包 → 回池 → HUD 显示"狗 +1"。
- **不做**：不叼大型垃圾、不代替中转站、不产生常驻 UI、不参与战斗、不变外观。

---

## 6. 命名和规范

### 6.1 命名规范（策划案 §9.4，工程实际也在用）

| 类别 | 格式 | 例子 |
|---|---|---|
| 角色 / 道具 | `chr_<名称>_<动作>_<帧序>` / `prop_<类别>_<名称>_<变体>` | `chr_dog_whitebox`、`prop_trash_plastic_whitebox` |
| 场景 | `env_<区域>_<类型>_<序号>` | `env_landmark_whitebox`、`env_decor_tree_2` |
| UI | `ui_<界面>_<元素>_<状态>`（工程里面板多用 `Panel_*`、`Btn_*`、`Txt_*`） | `Panel_Gacha`、`Btn_Draw` |
| 特效 / 音频 | `fx_<效果>_<序号>` / `sfx_<类别>_<名称>` | — |

### 6.2 工程红线（改动前务必遵守）

1. **不手写场景/预制体 YAML**：用 `Scripts/Editor/` 的 Builder 生成；改结构先改 Builder 再重跑，或加"幂等补齐/定点修复"脚本。
2. **数值不写死**：全部走 `WhiteboxConfig` 等配置资产；`OnValidate` 做上下限钳制；`[待拍板]` 项给建议默认值。
3. **可追溯**：实现需求就在文件头写 `// @trace 编号`；变更需求先升冻结清单版本。
4. **存档兼容**：新增状态字段保持 `version=1`，在 `NormalizeLoaded` / `NormalizeTools` / `NormalizeInventory` 里补默认值，不升版本不搬家。
5. **垃圾不改色、地平线不遮挡**：可交互高亮用独立 Outline，不改垃圾固有色；任何物体高度不过视线水平线，无远景假山/雾墙。
6. **狗固定外观**：`EcoStage` 不得接触狗的 Renderer；不留任何阶段色/姿态插值。
7. **输入仲裁集中**：面板开合、互斥优先级统一在 `WhiteboxGame.Update()`，别在各系统里各自读按键。
8. **幂等与保护**：Builder 只补空引用不覆盖用户布局；常规改动不动玩家双手、不动 UI 里用户调过的格式。
9. **验证才算完成**：改完要跑 L1 离线自检（`Tools/check_project.py`）、L2 编辑器校验（③）、L2.5 Play 冒烟（⑤），没有证据不标"已完成"。

---

## 7. 后续新增人物时需要修改或注意的地方（重点）

> **先说结论，避免走弯路**：当前工程里**没有"人物/角色"系统**——没有角色配置表、没有角色预制体族、没有角色外观/动画/属性框架。抽卡奖励的类别 `GachaKind` 只有 `Trinket / Weapon / Skill / SkillBook` 四类，**没有"人物"**。所以"新增人物"必须先明确你到底要加什么，否则无处落脚。以下按两种可能落点给出清单。

### 7.1 必须先确认的问题（**待补充 / 需用户拍板**）

1. 要加的"人物"是**场景里的实体角色**（像狗一样跟着玩家、会在球面上活动），还是**抽卡/图鉴里的可收集条目**（只是一个有名字、稀有度、描述的数据条目）？
2. 如果是实体角色：它的**行为**是什么（跟随？采集？对话？战斗？）——注意策划案明确"不做 NPC 与对话、不参与战斗"。
3. 外观：是否也要求"固定外观"？配色/体高有没有红线？是否复用 `ART-012` 的约束？
4. 是否要进抽卡池、是否有 rarity/权重/保底、是否与工具/技能book共用 `Grant` 发放入口？
5. 是否需要进存档（新增字段要遵守 6.2 第 4 条的兼容规则）、是否需要新的 UI 面板？
6. 主题一致性：命题是"同行"，新增人物不能破坏"狗是唯一常量、环境是唯一变量"的表达 → 需确认新角色在主题中的位置。

### 7.2 落点 A：新增"场景实体角色"（类比伙伴狗）

需要改动/新增的地方（按依赖顺序）：

| 位置 | 要做什么 |
|---|---|
| `Assets/Data/WhiteboxConfig.cs` + `.asset` | 新增该角色的半径/冷却/高度/颜色等可配置参数，**标 `[待补充]` 默认值**，并在 `OnValidate` 里钳制。 |
| `Assets/Scripts/Gameplay/` | 新增 `CompanionXxx.cs`（参照 `CompanionDog`：序列化 Renderer/Material、`Init(WhiteboxGame)`、`Update` 里行为），头加 `// @trace`。 |
| `Assets/Scripts/Editor/WhiteboxBuilder.cs` | 新增 `CreateXxxPrefab`：程序化建模 → 挂脚本 → 存 `Prefabs/Whitebox/chr_xxx_whitebox.prefab`；在 `BuildSceneContents` 里实例化、贴球面、拿引用。 |
| `Assets/Scripts/Boot/WhiteboxGame.cs` | 加 `[SerializeField]` 字段 + 属性 + `Wire` 参数（或新增 `WireXxx` 单独注入以兼容旧场景）+ 在 `Start()` 固定顺序里 `Init(this)`；若参与 `IsUiBlocking`/事件，一并接入。 |
| `Assets/Scripts/Environment/EcoStage.cs` | **不要动**——保持新角色也不接受阶段材质插值（除非用户明确要求变化，需先改冻结清单）。 |
| `docs/design-freeze.json` + `Whitebox_Spec_Notes.txt` | 新增需求编号（如 `MECH-017`）先入 `items` 并递增 `version`，再补 `@trace` 对照。 |
| 校验脚本 | `Scripts/Editor/WhiteboxValidator.cs`（41 项校验）增加对新角色/预制体/引用的检查。 |

### 7.3 落点 B：新增"抽卡人物奖励"（数据条目）

| 位置 | 要做什么 |
|---|---|
| `Scripts/Loot/GachaConfig.cs` | 若要真正的"人物"类别，需在 `GachaKind` 枚举新增值（如 `Character`），并改 `GachaDrawResult.KindName` 的显示；若只是换皮条目，直接复用现有 kind 即可。 |
| `Assets/Data/GachaConfig.asset` | 在 `rewards` 数组加条目：`id`（唯一）、`displayName`、`rarity`、`kind`、`weight`、`description`、`colorHex`。**id 必须唯一**，它是入库/去重/返券的键。 |
| `Scripts/Editor/GachaBuilder.cs` | `EnsureConfig()` 负责生成/补齐配置资产；改默认池时要同步 `GachaConfig.CreateDefault()`，保证资产缺失兜底一致。 |
| `Scripts/Loot/GachaSystem.cs` | 一般不用改：稀有度权重、软/硬保底、`ActivePool()` 过滤、`RollReward()` 加权都按配置走。若新人物要"修复后才出池"之类门槛，按 `epicRequiresRepair` / `activeInPhaseA` 的现有模式处理。 |
| `Scripts/Gameplay/ToolSystem.cs` + `Scripts/Data/ToolConfig.cs` | 若该人物要提供加成/技能书效果，需在 `ToolConfig` 加 `ToolDef`（`rewardId` 对上）并在 `ToolSystem` 加对应加成读取；否则只进图鉴，可标 `codexOnly=true`。 |
| 存档 | `WhiteboxState.ownedRewardIds/Counts` 是按 id 的平行数组，新条目自动入库，**无需新增字段**。 |

### 7.4 无论哪种落点都别踩的坑

- 别改 `WhiteboxGame.Start()` 的初始化顺序，新系统要排在它依赖的系统之后。
- 别手写 `.unity`/`.prefab`/`.asset` 文本；用 Builder + `Wire*` + `Init`，并兼容旧场景（引用为空时运行时补建/兜底）。
- 别让 `EcoStage` 去改新角色的材质；狗的固定外观是冻结红线。
- 别在存档升 `version`，新增字段走 `Normalize*` 补默认值。
- 别忘 `@trace` 与冻结清单；别忘把新预制体/引用加进 `WhiteboxValidator`。
- 别用受版权/IP 保护的形象，也别模仿在世艺术家风格（策划案 §9.1 禁忌⑤⑥）。

---

## 8. 待补充 / 待拍板清单

| 项 | 状态 | 说明 |
|---|---|---|
| "新增人物"的具体定义 | **待补充** | 见 7.1，需用户先确认是实体角色还是抽卡条目，以及行为/外观/主题定位。 |
| C1~C7、C9~C13 | 待拍板 | 耐久、转化比例、升级消耗、球体半径 R、描边口径、暂停计时等；工程里均为可配置默认值。 |
| C11 / C13 | 待拍板 | 狗与工具 Lv.3 成长线拆分口径；狗固定形象色号。 |
| 帧率 / 内存 / 包体 / 单局时长 / 球体半径 / 反馈延迟 | 待实测 | 未实测前不写"已平衡"。 |
| 完整任务链、三阶段截图、狗固有色取样 | 未跑 | 自动冒烟只覆盖启动与若干帧。 |
| 27 件正式美术资产、音频、手机端 | 未实现 | 当前是白膜占位；音频静音降级已实现。 |
| 标题场景 `Title.unity` 是否仍在使用 | 待补充 | 策划案提到"移除开始界面"，实际文件仍在。 |
| 金币购买链路 | 待补充 | 代码里 `ToolPurchase.enabled=false`、接口预留，但 `CoinTradeSystem` 已上线"出售得金币 + 购买"；以实际代码与用户最新决定为准。 |
| 商用字体授权 | 待确认 | `字魂*` 字体标注「商用需授权」，发布前需处理。 |

---

## 本轮产出 / 未完成 / 建议下一步

**本轮产出**
- 在 Unity 工程根目录生成 `PROJECT_GUIDE.md`：覆盖定位、核心循环、系统职责、场景/脚本/资源配置、角色创建接入流程、命名规范、新增人物落点与待补充清单。
- 全部内容基于实际目录、脚本、场景、预制体、配置资产和已有文档核对，未确认处已标注。

**未完成**
- 未对项目文件做任何修改（本次只读分析 + 新增本文档）。
- "新增人物"因定义不明，仅给出两种落点与待确认问题，未落到具体编号或代码。

**建议下一步**
1. 先回答 7.1 的 6 个问题（尤其是"实体角色 vs 抽卡条目"），我再给出精确到文件/行号的实施方案。
2. 若要新增实体角色：按 7.2 表格逐项落地，先改冻结清单升版本，再改 Builder 与 `WhiteboxGame`。
3. 若要新增抽卡人物：按 7.3 在配置里加条目，改完跑 ⑨ 并复验三/四项报告。
