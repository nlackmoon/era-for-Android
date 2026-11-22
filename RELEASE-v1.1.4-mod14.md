# era for Android 1.1.4 · mod14

基于 [ArtifactYu/era-for-Android](https://github.com/ArtifactYu/era-for-Android) **v1.1.1 / commit `fe54fae`** 的非官方修改分支 `eraflow-mod`。

这一版只做一件事：**让几千个脚本的大作打开得快一点**。

上一版把「读文件」和「解码」合并成一遍，TW 已经快了几秒，但打开仍然要等 20 秒上下。
用引擎自带的计时（`time.log`）一拆，时间根本不在解析算法上：

```
Proc:Init:ERB:Start     1861ms
Proc:Init:ERB:End      19065ms   ← 17.2 秒
```

这 17.2 秒里，真正在解析的只有 1.5 秒左右（函数本体 7590 个，0.8 秒；
剩下是字句解析），**其余全是 GC**。原因很具体：

## 1. Mono 的 nursery 太小（主要收益）

TW 的 4100 个 ERB、约 84 万行，解析后常驻对象约 **1.5 GB**。
Android 上的 Mono 默认 nursery 只有几 MB，于是一次加载要跑：

| | 改前 | 改后 |
|---|---|---|
| minor GC（gen0） | **1277 次** | **79 次** |
| major GC（gen2） | **19 次** | **6 次** |
| ERB 阶段耗时 | 17.2 s | **9.0 s** |
| 整个加载（Init） | 25.1 s | **17.9 s** |

（「改前」这一列是本次改动的中间版本，已经包含并行解析；mod13 实测还要再慢 1～2 秒。）

做法是给 Mono 下发 GC 参数（`src/EraFlow.Android/Resources/environment.conf`）：

```
MONO_GC_PARAMS=nursery-size=64m,major=marksweep-conc
```

`nursery-size` 让短命对象在新生代里就消化掉，`marksweep-conc` 让 major GC 并发回收，
减少加载过程中整段卡住的时间。

## 2. 函数本体按调用深度并行解析（顺带）

逐次版是「解析深度 0（事件函数）→ CALL 到的函数标深度 1 → 再解析深度 1 → …」一层层推。
现在**每一层内部**用多线程分担，解析的函数集合与逐次版完全一致（到不了的函数照旧不解析），
所以行为不变：`scaningLine`（报错用的当前行）改成 `[ThreadStatic]`，警告队列加锁。

设备上这只多省了约 0.5 秒——主要瓶颈是 GC，不是 CPU。留了个安全阀：
游戏目录放一个空的 `singleparse.flag` 就退回单线程解析。

## 3. 字句解析完立即释放原文缓存

`Preload` 会把所有 ERB 以 UTF-16 字符串常驻（TW 约 187 MB 原文 → 近 400 MB 字符串），
以前要等整个初始化结束才清。现在**字句一读完就清**，函数本体解析阶段堆小一截，
GC 压力也跟着小一点。`ReloadErb` 本来就会重新 `Preload.Load`，不受影响。

## 验证

- 桌面探针（同一份 TW 数据）改动前后：渲染行、选项列表**逐字节一致**，`error=false`；
  eraALK1.067_14_β 同样一致。
- 模拟器（4 GB / 4 核）实机加载 TW：能正常进标题、选项可点。
- `time.log` 分段计时（TW）：

```
ERB.LoadAll        2848ms   字句解析 4100 个文件
ERB.layer0..10      745ms   函数本体（并行）
ERB.uncalledLoop   4795ms   未调用函数的收尾 + 期间发生的 major GC
ERB.ParseScript    9010ms
```

## 排查用

- 游戏目录放空文件 `bootprofile.flag` → 启动后生成 `time.log`（分段计时）。
- 游戏目录放空文件 `singleparse.flag` → 关闭并行解析（万一某个游戏并行解析出问题）。

## 安装

- 与 mod13 同签名，可直接覆盖安装，存档与游戏库保留。
- 从官方版或其它签名版本迁移，先导出存档再卸载安装。
