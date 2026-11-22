# eraflow-mod 分支说明

本仓库是 [ArtifactYu/era-for-Android](https://github.com/ArtifactYu/era-for-Android) 的**非官方修改分支**。

- 上游项目：`ArtifactYu/era-for-Android`
- 基线版本：`1.1.1`
- 基线 commit：`fe54fae`（`Add Required Notice, disclaimer and first-screen anti-resale notice; release 1.1.1`）
- 本分支：`eraflow-mod`
- 关系：从上游 `main` 拉出的修改分支，**不是**上游官方分支，也不代表原作者。

上游本身是 Emuera / Emuera.EM+EE / andEmuera 的修改版；相关版权与许可见 `LICENSE`、`licenses/` 和 `README-andEmuera.md`。

## 本分支改了什么

### 启动速度（mod14）

几千个脚本的大作（eraTW 画蛇添足版：4100 个 ERB、约 84 万行）打开一次要等 20 秒以上，
时间几乎全在脚本阶段的 GC 上，不是解析算法慢。

- **Mono 的 nursery 太小**：解析 84 万行要新建 ~1.5 GB 对象，默认 nursery 只有几 MB，
  一次加载跑 **1277 次 minor GC、19 次 major GC**。用 `AndroidEnvironment` 下发
  `MONO_GC_PARAMS=nursery-size=64m,major=marksweep-conc`：
  minor GC **1277 → 79**，major **19 → 6**。
- **函数本体按调用深度并行解析**：逐次的「深度 0 → 1 → …」逐层推进改为每层多线程分工，
  解析的函数集合与逐次版完全一致（到不了的函数仍不解析），
  `scaningLine` 改 `[ThreadStatic]`，警告队列加锁。
  设备上只多省 ~0.5 s，属于顺带优化；游戏侧若因此出问题，放 `singleparse.flag` 可退回逐次。
- **字句解析完就释放 Preload 的原文缓存**：187 MB 的 ERB 以 UTF-16 字符串常驻是纯 GC 压力，
  现在在函数本体解析前就清掉（`ReloadErb` 会自己重新 `Preload.Load`）。

同一台模拟器（4 GB / 4 核）、同一份 TW 数据，`time.log` 实测：

| | 改前 | 改后 |
|---|---|---|
| ERB 读入 + 解析 | 17.2 s | **9.0 s** |
| 整个加载（Init） | 25.1 s | **17.9 s** |

> 上表「改前」是本次改动的中间版本（已含并行解析），mod13 实测还要再慢 1～2 s。

### 画蛇添足版兼容修正（mod13）

- **汉化词典导入 0 行**：`SQL_IMPORT_MAP_XML` 原来用
  `XmlReader.ReadElementContentAsString()` 逐元素读，这个方法读完一个元素后
  游标已经移到下一个节点，外层 `while (reader.Read())` 会多跳一格，
  `<v>` 永远读不到 —— `CSV_MAP` 导入结果 0 行，所有名词查询落空，
  于是满屏「※未能找到XX翻译，请保存日志报错」。改为 `XmlDocument` 读入后
  `CSV_MAP` 7721 行、`TASTE_MAP` 3944 行，报错归零。
- **警告行混进选项面板**：引擎给每条解析警告都挂了一个「跳到该行」按钮，
  选择枝面板把所有 `IsButton` 都当成选项，272 条警告变成 272 张卡片盖住画面。
  新增 `ConsoleButtonString.IsErrorButton`，`PrintErrorButton` 里打标，
  选择枝与「可选按钮数」都跳过它；标题画面恢复成 3 个真选项。

### 字号与「看不全」（mod12）

- **字号缩小时行高同比缩小**：以前只改 `FONT SIZE`，`LINE HEIGHT` 原样保留，
  于是「字变小了、一屏还是那么多行」，状态栏照样显示不全、观感还更差。
  现在两者按同一比例缩放（eraTW 的 `18/18` → 字号 10 时行高 10），
  实测同一块屏幕上可视行数 **18 行 → 31 行（+72%）**。
- **字号下限放宽到 8 px**（原 10）。
- **「全文」面板**（顶栏文档图标）：把最近的日志（最多 800 行）交给网页重新排版，
  字号 11～26 px 可调、可上下滚动、横屏单列宽排。
  角色状态栏这类「比屏幕还高」的文字，能在面板里一次读完，不再需要在游戏画面里反复上拖。

### 引擎兼容

- `#DiMS` / `#LocalSize` 指令大小写不敏感（`LogicalLineParser`）。
- `.ERH` 的 `#DIMS` / `#FUNCTIONS` 同样按大小写不敏感处理（`ErhLoader`）。
- 修复 `eraGoblin` 这类使用小写 `#DiMS` 的游戏在加载阶段直接报错的问题。
- **`.als` 别名加载崩溃**：`ConstantData` 里 `VarKeyAreadyDefined` 的提示串需要两个参数
  （名字 + 编号），原来只传了一个，`string.Format` 抛 `FormatException` 被上层吞成
  Lv3「予期しないエラー」，导致该 CSV 的整批别名全部丢弃
  （实测 TW 由此产生 690 处「无法解析的标识符」）。已补齐两个参数。
- **行内解析失败不再整局退出**：启动时打开 `CompatiErrorLine`，与 PC 版行为一致，
  单行写错只记警告，不再中断启动。
- **SQL 插件兼容层**（`ErbSql` + `Creator.Method.Sql.cs`）：
  `SQL_CONNECT`（命名连接，可带连接字符串，相对路径按游戏目录解析）、
  `SQL_P_EXECUTE_NONQUERY/SCALAR_LONG/SCALAR_STRING/READER`（`@0` 参数绑定）、
  `SQL_EXECUTE_*`（1 参用最近连接 / 2 参指定连接）、`SQL_READER_READ/GET_LONG/GET_STRING/ISNULL/CLOSE`、
  `SQL_IMPORT_MAP_XML`（`<map><p><k>/<v>` → `(k,v)` 表）。
  引擎内藏 SQLite，游戏里 `Data Source=plugins/xxx.db` 的库文件直接打开，只读目录会退到作业副本。
- **EE 兼容补丁**：`GETPLATFORM`、`GETANIMETIMER`、`GETSOUNDORBGMINFO`、
  `UNCHECKED_ADD/SUB/MUL/DIV/MOD`、`TEXT_BGC_ON/OFF`。
- **图片资源初始化提速**：`AppContents.CreateFromCsv` 改为只读文件头取尺寸
  （PNG/GIF/BMP/WebP/JPEG），首次 `.Bitmap` 访问才真正解码。
  TW 的图片初始化 13.4 s → 0.19 s；脚本读取改成「判定+解码一遍过」后，ERB 阶段 14.7 s → 10.9 s，总启动 31 s → 14 s。

### 游戏库与导入

- 启动时不再自动全量扫描，只显示 `library.json` 中已有的游戏。
- 目录扫描按 `source` 剪枝，已登记的游戏直接跳过。
- `library/scan-index.json` 目录指纹缓存：目录内容没变就跳过整棵子树。
- 复制导入时顺带统计脚本/图片/体积，避免二次全盘遍历。
- **直读目录**：通过 `MANAGE_EXTERNAL_STORAGE`（Android 11+）或读写权限（Android 10-）把 SAF 目录还原成真实路径，直接登记原目录，**不复制文件**；存档写回原目录。目录不可写或云盘 provider 自动回退到复制模式。
- 直读条目删除时只从库中移除，不删除用户原目录。

### 播放器界面

- 沉浸式全屏，去掉 Activity 标题栏、状态栏、导航栏和圆角窗口。
- **真正 edge-to-edge**：游戏页不再把系统栏/刘海 inset 当成 padding（横屏时会在刘海一侧
  留一整条黑边，看起来像“画面没铺满”）。改为 `layoutInDisplayCutoutMode = always`，
  只在输入法弹出时保留底部边距。
- 2x 离屏渲染（`RenderScale`），文字更清晰。
- 右侧常驻透明数字键盘（可关、可调透明度）。
- **右侧选项面板**：从引擎 `DisplayLineList` 取最新一代 `ConsoleButtonString`，按 `text` / `Inputs` 下发，渲染成可点击的透明块；支持 1/2/3 列、块大小 70%～130%、两行省略、独立纵向滚动。
- **显示选项面板开关**：设置里可关闭右侧选项面板（默认开）。关闭后有选项时不再弹出面板，只保留正文文字与数字键盘；开关在「常驻数字键盘」下方，随「恢复默认显示」一并复位。
- 右上角绿色 `»` 一键跳过：调用 `PressEnter(skipMessage: true)`，一直跳到下一个输入/选项。
- **虚拟手柄**（顶栏手柄图标）：左下角 D-pad + A/B。
  ▲/▼ 翻 1 行、◀/▶ 翻 10 行、● 回到最新；A 确认（有数字就提交，否则等于继续）、B 跳过一句。
  开关按游戏保存，重进自动恢复。
- **转向锁**（顶栏锁图标）：`RequestedOrientation = Locked` / `SensorLandscape`，在 Activity 层锁定，
  不受网页层旋转影响。
- **字号 A− / A+**：10～44 px，写入 `emuera.config` 的 `FONT SIZE`，行高不足时一并抬高；
  按游戏保存，重进游戏生效。
- **「等比」真正生效**：以前引擎始终按屏幕纵横比出图，`object-fit: contain` 看不出差别。
  现在 `EmueraEngine.KeepNativeAspect` 让画布改按 `emuera.config` 的 `WindowY/WindowX` 重绘，
  画面居中留黑边；点击/滑动坐标按实际画面矩形（`contentRect()`）换算，不会点歪。
- **自动跟随最新行**：`scrollstate` 报告没到底、且用户没有主动往上翻时，自动发 `latest`，
  避免新文本落在可视区外还要手动往上拖。
- 设置：屏幕大小 70%～130%（中心缩放，缩小时黑边对称）、铺满/等比、显示图片、按键振动、常驻键盘、显示选项面板、键盘透明度、选项列数、选项块大小。
- 删除点空白/滑动时弹出的“请使用数字键盘”提示。
- 修复 `scrollPos/scrollMax` 未声明导致画面不刷新的 `ReferenceError`。

## 构建

见根目录 `BUILD-ERAFLOW.md` 和 `build-eraflow.ps1`。需要 .NET 10 SDK + Android workload、JDK 21、Android SDK platform 36。

本分支的 APK 使用自建证书签名，**不能覆盖安装上游官方版**；从官方版迁移前先导出存档。仓库不包含任何签名密钥。

## 不包含

- 游戏本体或下载地址。
- 上游作者的 release 私钥。
- 构建产物（`bin/`、`obj/`、APK）。
