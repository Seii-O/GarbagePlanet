垃圾星球 · 音效放置说明（已接入首批素材）
================================

一、现在每个槽位对应什么声音
--------------------------------
Assets/Resources/Audio/SFX/<槽位>/

  UiClick      点击.wav                                   按钮点击
  UiOpen       按键确认音效.mp3                            打开面板
  UiClose      按键确认音效.mp3（音高略降）                关闭面板
  Pickup       拾取东西.wav 切出的 8 段 + 捡东西.mp3
               + 捡起小东西.wav + 捡起小的金属.wav          拾取/拆解（11 个变体随机）
  Deposit      拾取东西.wav 切出的最长一段                 投放进中转站
  Error        错误.wav + 错误（背包满了）.mp3              操作被拒
  GachaWin     捡到隐藏款的东西.mp3                        转盘中奖
  DogFetch     小狗叫.mp3                                  小狗叼回
  DogBark      狗叫2.wav 切出的 6 声叫                     狗叫
  DogSniff     狗闻东西.wav（17 秒，已转单声道）            狗发现垃圾时
  DogWhine     狗撒娇.mp3                                   背包满叼不回来时
  DogPant      狗喘气.mp3                                   备用（未调用）
  Step         走路.mp3 + 走路声枯树叶.mp3                  走路脚步
  Run          跑步.mp3 + 跑步2.mp3                         跑步脚步

  （同一文件夹多个文件 = 随机变体，所以拾取听起来不会机械重复）

Assets/Resources/Audio/BGM/
  bgm_01_sweet.mp3      hiqeui sweet moment   ← 主 BGM（排序取第一首 bgm_*，进游戏自动淡入循环）
  bgm_02_workshop.mp3   悠哉工坊               ← 备用曲，点播：Sfx.PlayMusic("bgm_02_workshop")
  amb_01_wind.wav       开场风声（60s，已转单声道）  ← 点播：Sfx.PlayMusic("amb_01_wind")
  amb_02_gear.mp3       齿轮慢摇（210s）            ← 点播：Sfx.PlayMusic("amb_02_gear")

  约定：bgm_* 是音乐，排序后第一首自动循环；amb_* 是环境音，不会自动播，只能按名字点播。
  想换默认曲：把目标文件改名成排序更靠前（如 bgm_00_xxx.mp3）即可。

二、还没声音的槽位（等你补素材）
--------------------------------
  ProcessStart / ProcessDone    加工开始 / 完成
  CraftStart  / CraftDone       制造开始 / 完成
  Upgrade                       升级工具、专精、背包
  LevelUp                       人物升级
  Coin                          金币进出
  GachaSpin                     转盘启动
  Purify                        净化区扩张

  这些槽位调用时不会报错，只在 Console 提示一次「缺少素材」，玩法照常。

三、怎么加 / 换
--------------------------------
· 换声音：把新音频丢进对应文件夹，删掉旧的，重新编译即可，不用改代码。
· 加变体：同一文件夹再放一个文件就行。
· 加槽位：音效管理.cs 里 SfxId 加名字 + Defs 加一行默认参数；
  菜单「Tools/垃圾星球白膜/㉖ 打开音效素材目录」会自动建同名文件夹。
· 播放一行：Sfx.Play(SfxId.UiClick);   3D 世界音：Sfx.PlayAt(SfxId.Pickup, pos);

四、音量与导入
--------------------------------
· 走标题界面两个滑块：sfx = PlayerPrefs "tp.volume.sfx"，music = "tp.volume.music"，
  SFX 与 BGM 两条独立通道，互不牵连。
· 每个槽位的基础音量 / 音高抖动 / 冷却 / 2D-3D 在 音效管理.cs 顶部 Defs 表。
· 导入自动分两类（脚本 SfxAudioImporter.cs）：
  bgm_/amb_/run_/dog_sniff/dog_pant → Streaming（长音频不吃内存）
  其余 → 单声道 + ADPCM 压缩（短音效零解码延迟）
  改完参数想重跑：菜单「Tools/垃圾星球白膜/㉘ 应用音效导入设置」。

五、没声音怎么查
--------------------------------
1. 菜单「Tools/垃圾星球白膜/㉗ 检查音效接入情况」：列每个槽位有几个文件。
2. Play 后 Console 搜「[音效]」：
   [音效] 就绪：SFX N/23 个槽位有素材，BGM = bgm_01_workshop
   [音效] 待补素材：...
3. 单个音效没响：看是不是被冷却挡住（连点时正常现象），
   或标题界面把音效滑块拉到 0 了。
