# era for Android 1.1.3 · mod13

基于 [ArtifactYu/era-for-Android](https://github.com/ArtifactYu/era-for-Android) **v1.1.1 / commit `fe54fae`** 的非官方修改分支 `eraflow-mod`。

这一版修的是别人报上来的两个 bug，都出在 **eraTW 画蛇添足版** 上。

## 1. 汉化全部失效：满屏「※未能找到XX翻译，请保存日志报错」

画蛇添足版的汉化不是把译文写死在脚本里，而是启动时把词典
`plugins/tw_csv_chs.xml` 导进 SQLite 表 `CSV_MAP`，之后每次显示名词都查一次
`SELECT v FROM CSV_MAP WHERE k = '...'`。

我们的导入用了 `XmlReader.ReadElementContentAsString()`：它读完一个元素后，
游标已经停在**下一个节点**上，外层再 `while (reader.Read())` 就多跳了一格。
结果 `<k>` 读到了、`<v>` 永远读不到，**整个词典导入 0 行**。

于是每次查询都返回空，脚本走 `未能找到...翻译` 的分支——272 行报错就是这么来的。

改成 `XmlDocument` 一次性读入（同时兼容 `<p k=".." v=".."/>` 的写法）：

| | 修复前 | 修复后 |
|---|---|---|
| `CSV_MAP` 导入行数 | 0 | **7721** |
| `TASTE_MAP` 导入行数 | 0 | **3944** |
| 启动日志里「未能找到」条数 | 272 | **0** |

## 2. 警告行被当成「选项」，铺满整个画面

`警告Lv0:034天子.csv:第3行:同...` 这类是引擎的解析警告，每行都带一个
「跳到那一行」的按钮。选择枝面板把**所有可点的按钮**都收了进来，
于是两百多条警告变成两百多张卡片，把标题画面糊住了。

现在这类按钮会打上 `IsErrorButton` 标记，选择枝面板和「可选按钮数」都跳过它们：

- 标题画面：**275 个（272 条警告 + 3 个真选项）→ 3 个**
- 列表里只剩 `[0] 开始游戏` / `[1] 继续游戏` / `[2] DEMO`

警告本身仍然照常显示在日志里（也会进「全文」面板），只是不再抢选项面板的位置。

## 3. 顺带：「全文」面板不再显示 HTML 标签

用 `HTML_PRINT` 写的行（标题画面就是），以前进全文面板会连
`<div xpos='1600px' ...>`、`<shape type='space' param='841px'>` 一起显示。
现在只保留看得见的文字。

## 4. 选项卡片上的文字也清理了

画蛇添足版的选项是用 `HTML_PRINT` 的 `<shape type='space'>` 排版的，选项面板以前
会把标签一起显示成 `[0]<shape type='space'...`。现在只显示
`[0] START` / `[1] 扮演模式（一部分口上无法显示）`。

## 验证

用桌面版探针直接跑同一份 eraTW 目录（8200 个脚本 / 338 MB）：

```
error=false
CSV_MAP rows=7721   TASTE_MAP rows=3944
未能找到: 0
choices: [0] 开始游戏 / [1] 继续游戏 / [2] DEMO   (警告 0 条)
```

## 安装

- 包名：`app.eraflow.android`
- 版本：`1.1.3-mod13`（versionCode 15）
- 最低 Android 8.0（API 26），arm64-v8a / x86_64
- 大小：56 528 331 字节（53.9 MiB）
- SHA-256：`03d2fbc1cc2400c70b6fc3a3e17bbeefec2b76c78d782b0ec752adca7ea521ff`

**签名说明**：与 mod1～mod12 同一张自签证书，可直接覆盖升级，游戏库和存档保留；
不能覆盖上游官方版。

## 下载

| 文件 | 说明 |
|---|---|
| `era-for-Android-1.1.3-mod13.apk` | 安装包（53.9 MiB，versionCode 15） |
| `INSTALL.txt` | 安装顺序、签名与 SHA-256 |
| `era-for-Android-mod.zip` | 完整源码 |
| `era-for-Android-mod.patch` | 相对上游 `fe54fae` 的完整补丁 |
