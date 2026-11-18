# eraflow-mod 分支说明

本仓库是 [ArtifactYu/era-for-Android](https://github.com/ArtifactYu/era-for-Android) 的**非官方修改分支**。

- 上游项目：`ArtifactYu/era-for-Android`
- 基线版本：`1.1.1`
- 基线 commit：`fe54fae`（`Add Required Notice, disclaimer and first-screen anti-resale notice; release 1.1.1`）
- 本分支：`eraflow-mod`
- 关系：从上游 `main` 拉出的修改分支，**不是**上游官方分支，也不代表原作者。

上游本身是 Emuera / Emuera.EM+EE / andEmuera 的修改版；相关版权与许可见 `LICENSE`、`licenses/` 和 `README-andEmuera.md`。

## 本分支改了什么

### 引擎兼容

- `#DiMS` / `#LocalSize` 指令大小写不敏感（`LogicalLineParser`）。
- `.ERH` 的 `#DIMS` / `#FUNCTIONS` 同样按大小写不敏感处理（`ErhLoader`）。
- 修复 `eraGoblin` 这类使用小写 `#DiMS` 的游戏在加载阶段直接报错的问题。

### 游戏库与导入

- 启动时不再自动全量扫描，只显示 `library.json` 中已有的游戏。
- 目录扫描按 `source` 剪枝，已登记的游戏直接跳过。
- `library/scan-index.json` 目录指纹缓存：目录内容没变就跳过整棵子树。
- 复制导入时顺带统计脚本/图片/体积，避免二次全盘遍历。
- **直读目录**：通过 `MANAGE_EXTERNAL_STORAGE`（Android 11+）或读写权限（Android 10-）把 SAF 目录还原成真实路径，直接登记原目录，**不复制文件**；存档写回原目录。目录不可写或云盘 provider 自动回退到复制模式。
- 直读条目删除时只从库中移除，不删除用户原目录。

### 播放器界面

- 沉浸式全屏，去掉 Activity 标题栏、状态栏、导航栏和圆角窗口。
- 2x 离屏渲染（`RenderScale`），文字更清晰。
- 右侧常驻透明数字键盘（可关、可调透明度）。
- **右侧选项面板**：从引擎 `DisplayLineList` 取最新一代 `ConsoleButtonString`，按 `text` / `Inputs` 下发，渲染成可点击的透明块；支持 1/2/3 列、块大小 70%～130%、两行省略、独立纵向滚动。
- 右上角绿色 `»` 一键跳过：调用 `PressEnter(skipMessage: true)`，一直跳到下一个输入/选项。
- 设置：屏幕大小 70%～130%（中心缩放，缩小时黑边对称）、铺满/等比、显示图片、按键振动、常驻键盘、键盘透明度、选项列数、选项块大小。
- 删除点空白/滑动时弹出的“请使用数字键盘”提示。
- 修复 `scrollPos/scrollMax` 未声明导致画面不刷新的 `ReferenceError`。

## 构建

见根目录 `BUILD-ERAFLOW.md` 和 `build-eraflow.ps1`。需要 .NET 10 SDK + Android workload、JDK 21、Android SDK platform 36。

本分支的 APK 使用自建证书签名，**不能覆盖安装上游官方版**；从官方版迁移前先导出存档。仓库不包含任何签名密钥。

## 不包含

- 游戏本体或下载地址。
- 上游作者的 release 私钥。
- 构建产物（`bin/`、`obj/`、APK）。
