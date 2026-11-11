---
title: 我用 Avalonia 12 + .NET 10 做了一个 Windows 系统管理工具集：模块化架构 + 8 个硬核工具
tags: [.NET, Avalonia, Windows, WPF, 系统管理]
categories: [.NET]
summary: 从一个路由管理工具长出来的模块化工具集：路由/ACL 权限/右键菜单/COM 扩展/启动项(服务+计划任务)/PLC 扫描/流量监控。分享 ToolModule 插件架构、ETW 按进程流量归因、StartupApproved 禁用机制等一批 Windows 硬核知识点，以及全程用 UIA 自动化调试桌面应用的工作流。
---

## 这个项目是什么

做嵌入式/工控的同学大概都有类似经历：路由表要改、安装软件报"错误 5 拒绝访问"、右键菜单被各路网盘塞成瀑布、开机自启一堆残留、想知道"到底是哪个进程在偷流量"……这些事命令行都能干，但每次都要回忆参数、提权、查资料。

于是我把这些高频操作做成了一个**卡片式工具集**：[CCSystemToolkit](https://github.com/ccqin/CCSystemToolkit)（GitHub 开源， Releases 有自包含单文件 exe，解压即用免装运行时）。

技术栈：**Avalonia 12 + .NET 10 + CommunityToolkit.Mvvm**，纯 Windows 场景（大量 P/Invoke / 注册表 / ETW / COM）。

## 架构：一个工具一个模块（ToolModule）

项目从一个独立的路由管理工具（WPF 迁移来的 Avalonia 版）演化而来，当初做第二工具时就定下了插件式结构：

```
Toolkit.App（宿主）→ 各工具.Ui → { 各工具.Core, Toolkit.Shell }
```

- **Toolkit.Shell** 定义抽象：`ToolModule`（Id/Title/Glyph/Description/CreateView）+ `IToolHost`（提权重启/统一日志/状态栏）
- 宿主是卡片墙 + 内容区，工具**不引用宿主**，只通过 `Host` 访问能力 —— 无循环引用
- 每个工具都是 `Xxx.Core`（无 UI、可单测）+ `Xxx.Ui` 两层

新增一个工具只需：建两个类库 → 实现 ToolModule → 注册三处 → 主页自动出现卡片。从路由管理到流量监控，第八个工具的接入成本依然只有十几分钟。

```
🌐 路由管理  🔐 权限医生  🧹 清理右键菜单  🧩 COM 扩展管理
🚀 开机启动项(含服务/计划任务)  📡 PLC IP 扫描  📶 流量监控  📋 操作日志
```

## 八个工具，八个 Windows 硬核知识点

每个工具背后都有一个值得展开的 Windows 机制，挑重点说。

### 1. 路由管理：MIB_IPFORWARD_ROW2 的 ABI 与 MSDN 不一致

P/Invoke `GetIpForwardTable2` 读写路由表。踩过的坑：`MIB_IPFORWARD_ROW2` 结构体的**实际内存布局（104 字节）与 MSDN 文档的字段顺序不一致**——`Origin` 字段在实际布局中位于结构体末尾。没发现这点前，读出来的路由数据全是乱的。

另外 `route print` 显示的跃点 = 接口跃点 + 路由跃点，界面上要拆解展示，不然用户会疑惑为什么加的路由"跃点不对"。

### 2. 权限医生：ACL 诊断与修复

解决"安装程序报错误 5 拒绝访问"。诊断三板斧：

- **写入实测**：真的去创建/删除一个临时子目录（比静态分析可靠）
- **有效权限计算**：用户 + 所属组 SID 集合匹配 ACL 条目累加，Deny 优先
- **孤儿 SID 检测**：从旧电脑拷来的目录，授权条目全是无法映射的 SID（IdentityNotMappedException），这正是"你明明是管理员却没权限"的常见原因

禁用策略上有个关键认知：**HKCU\Software\Classes 的合并视图优先级高于 HKLM**——往 HKCU 同路径写一个覆盖键，就能在不碰 HKLM（不需要管理员）的情况下改变文件关联和 shell 行为。

### 3. 清理右键菜单：场景视图 + ProgID 间接路径

做的时候发现用户真正的心智模型不是"注册表位置"，而是"**我右键 .exe 文件时看到的那堆东西**"。所以做了模拟真实菜单的"场景视图"。

扫描上的大坑：Windows 文件菜单是三部分合并的——`HKCR\*\shell`（通配）、`HKCR\.exe\shell`（后缀直挂）、还有最容易被漏掉的 **`HKCR\.exe`（默认值 = ProgID）→ `HKCR\exefile\shell`（ProgID 间接路径）**。第三方软件大量注册在 ProgID 下，只扫前两类会漏掉一大半（实测从 167 项涨到 925 项）。

禁用机制：shell 命令项写 `LegacyDisable`（完全可逆），shellex 扩展进 `Shell Extensions\Blocked` 黑名单；删除前自动 `reg export` 备份。

### 4. COM 扩展管理：注册表搜不到的菜单文字

"发送到手机"这种菜单项是 COM 扩展 DLL **运行时动态生成**的，注册表里只有 CLSID——所以任何注册表扫描都搜不到"手机"两个字。

解法：`LoadLibraryEx` 以 `LOAD_LIBRARY_AS_DATAFILE`（纯数据映射，**不执行任何 DLL 代码**）加载扩展 DLL，遍历字符串资源表，启发式筛出"疑似菜单文字"。实测能完整挖出夸克网盘的全部菜单项（发送到手机/PDF转Word/…）。

另一个收获：系统 `Shell Extensions\Approved` 清单里有全部已批准扩展的官方描述，比 DLL 文件名可读得多。

### 5. 开机启动项：10 类位置 + 任务管理器同款禁用

按权威清单（Microsoft 文档/MITRE ATT&CK/Sysinternals Autoruns）对照，覆盖全部 10 类启动位置：Run/RunOnce（HKCU/HKLM/32 位视图）、组策略 Run、启动文件夹、Winlogon（Userinit/Shell 追加监控位）、Active Setup、HKU\.DEFAULT、UWP StartupTask，外加**服务**和**计划任务**两个 Tab。

几个值得记录的机制：

- **禁用 = StartupApproved**：`HKCU\...\Explorer\StartupApproved\Run` 下写 12 字节二进制（首字节奇数=禁用），和任务管理器/设置的禁用是**同一存储，状态互通**
- **计划任务在加固系统上的坑**：本机 TaskCache 注册表和 Tasks 目录 ACL 对普通用户锁死，XML 解析和 schtasks 都会被拒；最终用 [dahall/TaskScheduler](https://github.com/dahall/TaskScheduler)（COM 通道，和 taskschd.msc 同源）绕过，202 个任务全量枚举
- **无引号含空格路径**：`E:\Program Files (x86)\DingDing\x.exe /autorun` 这种命令，取第一个空格前会得到 `E:\Program` → 误判"卸载残留"。必须逐段累积直到文件真实存在

### 6. PLC IP 扫描：SendARP 多源地址探测

从 WinForms 版迁移。核心是 Windows 没有直接列出"网段内所有设备"的 API，用 `SendARP` 逐 IP 探测（64 并发）。关键细节：**SendARP 的源地址参数**——多网卡配在同一网段时，系统自动源地址按接口 metric 选出口，会从错误的网卡发出而漏掉设备；所以要依次用每个同网段本地 IP 做源探测。

配套做了 TCP 端口探测的工控识别：102=西门子 S7、502=Modbus TCP、44818=EtherNet/IP、21+23=疑似 SC 设备。

### 7. 流量监控：ETW 按进程归因（任务管理器同源）

"哪个进程在偷流量"——Windows **没有**每进程流量计数器 API，任务管理器靠的是**内核 ETW 事件**：`Microsoft-Windows-Kernel-Network` 提供者的事件（TCP/UDP × IPv4/IPv6 的 send/recv）自带 PID 和字节数。

C# 用 [TraceEvent](https://github.com/microsoft/perfview)（就是 PerfView 的库）消费：

```csharp
using var session = new TraceEventSession("MyNetTraffic", null);
session.EnableKernelProvider(KernelTraceEventParser.Keywords.NetworkTCPIP);
var kernel = session.Source.Kernel;
kernel.TcpIpSend    += e => Add(e.ProcessID, up: e.size);
kernel.TcpIpRecv    += e => Add(e.ProcessID, down: e.size);
kernel.UdpIpSend    += e => Add(e.ProcessID, up: e.size);
kernel.UdpIpRecv    += e => Add(e.ProcessID, down: e.size);
// + IPv6 变体，共 8 个事件
Task.Run(() => session.Source.Process());
```

按 PID 累计，UI 每秒快照一次（增量/间隔 = 速率）。需要管理员（内核会话）。右键还能看进程详情和它的全部 TCP 连接表——排查"连到哪个远程 IP 下载"非常有用。

顺带一提事件名的坑：查了一圈资料才确认是 `kernel.TcpIpSend`（不是想当然的 `NetworkTcpIpSend`），对照 [PerfView 源码](https://github.com/microsoft/perfview)核对的。

### 8. 全套 UIA 自动化调试

桌面程序自己开发自己测很痛苦，所以我给所有关键控件设置了 `AutomationProperties.AutomationId`，然后用 UIA 工具按 id 定位/Invoke，**不依赖坐标**。自绘的齿轮图标控件还实现了自定义 `AutomationPeer` 提供语义名称。

这套规范写进了项目的 AGENTS.md，全程可以驱动真实 UI 验证功能（连卡片悬停命中失效这种问题都是 UIA 树定位分析的）。实际经验：Avalonia 的 Fluent 按钮模板背景层不参与 hit-test，需要透明兜底层——这种坑没文档，只能调试。

## 工程化细节

- **GitHub Actions 自动发布**：push 到 master → 构建**自包含单文件 exe**（免装 .NET 运行时）→ 自动打 tag（`v年.月.日.当日序号`）→ Release 附 zip
- **AGENTS.md**：给 AI 助手/贡献者的开发指南（架构约定、新增工具流程、构建纪律、已知坑清单）——这个项目从第二个工具开始就是和 AI 结对开发的，把"踩过的坑"沉淀成文档后，AI 重复踩坑率显著下降
- **明暗主题/卡片自定义**：Avalonia 12 的 `RequestedThemeVariant` + 全 DynamicResource；卡片背景直接绑按钮本体（非空画刷保证命中可靠）

## 写在最后

这个项目没有什么宏大目标，就是"把每次折腾系统时学到的 Windows 知识，固化成一个有界面的工具"。但回头看，收获比预期大：

1. **Windows 的很多"黑盒"其实都有官方机制**（ETW/StartupApproved/Approved 清单/COM 通道），缺的是把它们串起来的工具
2. **模块化架构的价值在第三个工具时显现**——接入成本从"新项目"降到"填空"
3. **调试基建（UIA/AGENTS.md）的投入全程都在回本**

仓库：**[github.com/ccqin/CCSystemToolkit](https://github.com/ccqin/CCSystemToolkit)**，Releases 下载 zip 解压即用。如果你也有想塞进来的系统管理场景，欢迎 issue 交流。
