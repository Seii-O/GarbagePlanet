把中文字体放这里（.ttf / .otf），重跑一次「Tools/垃圾星球白膜/⑤ 重排中转站 UI（美术版）」即可生效。

推荐：思源黑体 / Noto Sans SC / Source Han Sans（OFL 授权，可商用）。

- 只放一份时，UiFont.Load() 会用它作为全 UI 字体；放多份则按工程内顺序取第一个，建议只留一份。
- 换完请对比 docs/preview-station.png：判断清晰度要把 Game 视图设成 1920x1080（缩放系数 1.0）再看。
- 不放任何字体也不会报错，会自动退回 Unity 内置 LegacyRuntime.ttf（中文走系统字体回退）。
