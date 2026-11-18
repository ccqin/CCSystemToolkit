# 系统管理工具集

基于 **Avalonia 12 + .NET 10** 的 Windows 系统管理工具合集：卡片式主页 + 一个工具一个模块（ToolModule）。当前内置 **9 个工具 + 统一日志**：路由管理、权限医生、清理右键菜单、COM 扩展管理、开机启动项（含服务/计划任务）、PLC IP 扫描、流量监控、串口 MCP、文件传输（FTP/SFTP/SCP）。

> **开发者/AI 助手请先读 [AGENTS.md](AGENTS.md)**：架构约定、新增工具标准流程、构建验证纪律与历史踩坑清单。

## 功能总览

| 工具 | 功能 |
|------|------|
| 🌐 **路由管理** | **总览**：统计卡片 + 自动诊断（多条默认路由、缺失 on-link 直连路由可一键修复、跨网卡网段冲突、APIPA、代理/VPN TUN 接管提示）。**路由表**：全部 IPv4/IPv6 路由，按网卡筛选、按目标/网关/接口搜索，添加（可选持久 `-p`）/修改/删除（核心系统路由受保护），每步显示等效 `route add` / `netsh` 命令。**网卡**：状态、IP、网关、接口跃点，IP 缺直连路由红字告警。**备份**：JSON 备份（路由+网卡快照）、差异对比、增量合并/完全恢复、安全快照、按网卡名重映射接口索引、导入导出。**帮助**：9 章中文文档。 |
| 🔐 **权限医生** | 诊断/修复文件夹 ACL 权限（安装程序“错误 5：拒绝访问”等问题）：**诊断报告** = 写入实测（真实创建/删除子目录）+ 当前用户有效权限 + 所有者/继承状态 + 问题清单（孤儿 SID、断继承、缺权限）；**权限明细表** 列出全部 ACL 条目（身份/权限/应用于/继承来源，标注孤儿 SID 与匹配当前用户的条目）；**一键修复**（授予修改权限 / 恢复继承 / 取得所有权）或复制等效 `icacls`/`takeown` 命令到管理员终端。 |
| 🧹 **清理右键菜单** | 按「右键场景」管理经典右键菜单（Win11 在「显示更多选项」中），三个 Tab 页：**场景视图**（左选场景：任意文件/文件夹/空白处/驱动器/后缀动态发现，右侧模拟真实菜单逐项开关，显示本场景条目数）；**来源视图**（按软件聚合成可展开组，一键禁用/启用全部注入点）；**全量列表**（搜索过滤、列宽可调）。扫描覆盖 `*`/`Directory`/`AllFilesystemObjects` 等通用根 + `SystemFileAssociations` + `.后缀→ProgID` 间接路径（exefile 等）。自动检测**软件卸载残留**（命令指向不存在的文件，一键清理）、识别 Microsoft/系统项并保护。禁用走 HKCU（完全可逆）、删除前自动 .reg 备份可还原；共享详情面板实时读取注册表值与文件版本信息、生成等效 reg 命令（可复制）；搜索三视图联动；扫描等待遮罩；shellex 禁用后可一键重启资源管理器。 |
| 🧩 **COM 扩展管理** | 管理 Shell COM 扩展：右键菜单的**动态项**（如“发送到手机”，注册表搜不到文字）、**图标叠加**（系统上限 15 个，超出的排队浪费）、缩略图、预览窗格、属性页。描述三级回退（**Approved 官方清单 → CLSID 组件名 → 键名**）；**疑似菜单文字分析**：以纯数据方式映射 DLL（不执行代码）遍历字符串资源，启发式筛出该扩展实际显示的菜单文字（如夸克的“发送到手机/PDF转Word…”），点击行展开二级详情查看；DLL 残留检测、按 CLSID 聚合注册点、类型筛选与搜索；黑名单禁用可逆 + 一键重启资源管理器；扫描等待遮罩。 |
| 🚀 **开机启动项** | 两个 Tab 页。**🚀 启动项**：覆盖 **全部 10 类启动位置** —— 注册表 Run/RunOnce（HKCU/HKLM/32位）、组策略 Run、启动文件夹（用户/全局）、Winlogon（Userinit/Shell 追加监控位）、Active Setup、HKU\.DEFAULT·Run、**UWP 应用 StartupTask**（AppModel 状态读写，与系统设置同一存储）；禁用走系统 **StartupApproved**（与任务管理器互通、可逆）；Winlogon/ActiveSetup 只读防误改；残留检测（无引号含空格路径正确解析）；删除自动备份可还原；支持新增 HKCU·Run。**⚙ 服务**：全部用户态服务（Start 类型/账户/svchost 宿主 DLL/来源/残留），筛选 + 一键改启动类型（自动/手动/禁用，需管理员）。**📋 计划任务**：基于 [dahall/TaskScheduler](https://github.com/dahall/TaskScheduler)（COM 通道，加固系统锁死 TaskCache 注册表/Tasks 目录时依然可用），全部任务按触发器类型标注（登录=自启动置顶/开机/定时/事件/空闲/会话），上次/下次运行时间、来源、残留检测；筛选（登录触发/仅第三方/全部）+ 启用禁用开关 + 删除（系统任务需管理员）。 |
| 📡 **PLC IP 扫描** | ARP 网段扫描发现设备（WinForms 版 PlcIpScanner 迁移）：网卡选择（★自动=全部在线网卡源地址池，多网卡同网段不漏设备）、网段解析（`192.168.1` / `192.168.1.0/24` / `192.168.1.10-60`，多段换行/分号）、SendARP 64 并发探测、自动清理失效 ARP 缓存、TCP 端口探测与识别（**102=西门子 S7 / 502=Modbus / 44818=EtherNet-IP / 21+23=★疑似 SC 设备**，金色标注）、**每行「🔍 探端口」按钮可改端口后补测**、进度条+剩余时间、隐藏本机结果、双击行复制 IP。 |
| 📶 **流量监控** | **历史落库（SQLite）**：监控运行期间每 10 秒把各进程流量增量写入 %LOCALAPPDATA%/SystemToolkit/toolkit.db（保留 14 天），「📊 历史分析」页按时间范围（15 分钟/1 小时/6 小时/今天/3 天）汇总各进程上下行——事后分析“这段时间谁耗的流量”，**双击/右键任意进程行打开流量曲线弹窗**（自绘折线图：绿下行/橙上行、悬停读值、峰值与合计统计）；采样间隔 5 秒。**双击/右键曲线**：下行蓝实线/上行橙虚线（色盲安全），悬停读值。**「🛡 代理分析」页**：对接 Clash/FlClash 内核 external-controller 连接表——回答“FlClashCore 的流量究竟是谁走的”：按进程聚合（谁在走代理）+ 活跃连接明细（进程/目标/规则/出口节点/上下行），支持 5 秒自动刷新。统一日志同样落库（保留 30 天）。实时查看**哪个进程在下载/上传**（内核 ETW TCP+UDP 事件按进程归因，任务管理器同源机制）：按下载速率排序的进程表（速率/会话累计/TCP 连接数）、总速率与最活跃进程统计卡；**右键进程**：查看详细信息（路径/公司/版本/命令行 + 该进程全部 TCP 连接表：本地→远程/状态）、打开文件位置、网上搜索、复制进程名、结束进程树。需要管理员。 |
| 🔌 **串口 MCP** | 内置 **MCP 服务器**（streamable HTTP，仅监听本机回环，需接入令牌），让 **AI 客户端通过标准 MCP 协议直接操作串口**：7 个工具（枚举串口含设备描述 / 打开（默认 115200 8N1，DTR/RTS 拉有效与 Xshell 一致，`line_history` 行历史容量 0=全部缓存）/ 关闭 / 收发（支持 `\r \n \t \xNN` 转义与 HEX，`newline` 自动补回车，发送后等待应答）/ **读数据（实时读缓冲 + `lines` 最近 N 行 + `from_line`/`to_line` 行号范围，行历史带行号可配置容量）** / 清空接收缓冲 / 状态）。卡片内**实时监控收发**：`AI→串口`、`串口→返回`、手动发送、**`AI调用`**（每次 MCP 调用的参数摘要）逐行着色；**串口返回按行缓冲显示**（收到换行符才整行显示，超长截断悬停看全文，空闲 150ms 兜底显示半行）；**方向筛选标签**（全部/串口返回/AI→串口/AI调用/手动/系统/错误，多选过滤）、HEX 显示切换、自动滚动、**多选右键复制**（左键单击清空多选）、累计统计；手动发送测试（文本/HEX，**附加回车开关**），串口设置含「历史」输入框（行历史容量，0=全部缓存）。服务启停/端口/接入授权（令牌）在顶栏「🔌 MCP 服务」弹窗统一管理：按 AI 客户端生成可启停/吊销的令牌，一键复制含令牌的接入 URL，统计各客户端调用次数与最近使用。 |
| 📋 **操作日志** | 以工具卡片形式进入的完整日志页：按级别（信息/成功/错误）与来源（各工具）筛选，实时统计“共 N 条 · ✔ · ✘”，一键清空；与状态栏“日志”Flyout 共用同一份数据。 |
| 📤 **文件传输** | **统一站点管理（FTP / SFTP / SCP 协议在添加/编辑站点时选择；SFTP/SCP 走 SSH 密码或私钥认证，FTP 被动模式可选）+ 手动传输（协议跟站点走）+ 远程浏览 + 传输记录 三页**，内置**传输 MCP 服务器**（默认端口 8643，令牌授权同串口 MCP）：AI 可调 5 个工具——`list_transfer_sites`（站点清单，不回显密码）/ `transfer_file`（按站点上传下载，SFTP 自动逐级建远程目录）/ `transfer_file_direct`（免站点直接传连接参数）/ `list_remote_dir`（列远程目录，SCP 不支持）/ `get_transfer_status`。站点编辑弹窗内**就地「测试连接」**（用当前填写内容验证连通性与认证，结果彩色显示——填错当场改；结果同时进传输记录）；**「远程浏览」页**：选站点列目录、双击进入子目录、上级/刷新、选中文件一键下载、把当前目录**「填为上传目标」**自动回填对应协议表单。**传输记录页**实时显示每次传输（协议/站点/文件/大小/耗时/速度，AI 调用参数摘要中密码自动脱敏）。站点持久化 `%LOCALAPPDATA%\SystemToolkit\transfer-sites.json`（密码明文，注意保管）。 |
| 🧩 更多工具开发中 | 新工具以 ToolModule 模块接入（见下文）。 |
| 🎨 **主页与主题** | 卡片背景自定义：⚙ 菜单（12 色色板/取色器/本地图片），默认绿底白字，配置持久化；状态栏**明暗主题循环切换**（跟随系统/浅色/深色，DynamicResource 即时刷新）；全局控件统一圆角、卡片阴影与悬停上浮动效。 |

## 快速开始

```bash
# 构建（需要 .NET 10 SDK）
dotnet build SystemToolkit.sln -c Release

# 运行工具集
src/Toolkit.App/bin/Release/net10.0/Toolkit.App.exe

# 命令行工具：导出网卡 / 路由表 / 诊断（留档对比用）
dotnet run --project tools/RouteDump
```

### 自动发布（GitHub Actions）

推送到 `master` 自动触发 [Build & Release](.github/workflows/release.yml)：构建自包含单文件 exe（**免装 .NET 运行时，解压即用**）→ 打 tag（`v年.月.日.当日序号`）→ 创建 GitHub Release 并附带 zip。也可在 Actions 页手动触发。

### 权限说明

- **查看**路由表、网卡、诊断、创建备份：普通权限即可。
- **修改**路由（增删改、恢复备份、一键修复）：需要管理员。主页/工具内点击“以管理员重启”，UAC 确认后**整个工具集**以管理员身份重启（重启后回到主页）。
- 任何被拒绝的写操作都会显示等效命令，可复制到管理员命令行手动执行。

## 项目结构

```
SystemToolkit.sln
├─ src/
│  ├─ Toolkit.Shell/               # 工具集抽象层
│  │  ├─ IToolHost.cs              #   宿主能力：IsElevated / StatusText / Log / ElevateRestart / ShowMcpSettings
│  │  ├─ ToolModule.cs             #   工具模块基类：Id/Title/Glyph/Description/CreateView
│  │  ├─ ToolLog.cs                #   统一日志条目与级别
│  │  └─ Mcp/                      #   MCP 基础设施（工具暴露 MCP 服务用）
│  │     ├─ IMcpServiceDefinition.cs    # 服务定义接口（Id/端口/启停/状态变化事件）
│  │     ├─ McpServiceRegistry.cs       # 静态注册表（工具注册，设置弹窗统一管理）
│  │     ├─ McpHttpServer.cs            # MCP HTTP 服务器通用基类（HttpListener+JSON-RPC 骨架，
│  │     │                              #   子类只提供工具清单与执行；新 MCP 服务继承即用）
│  │     └─ McpAccessStore.cs           # 接入授权（客户端令牌启停/吊销/调用统计）+ 端口记忆，
│  │                                    # JSON 持久化 %LOCALAPPDATA%\SystemToolkit\mcp-access.json
│  ├─ Toolkit.App/                 # 宿主应用（卡片式主页 + 工具切换 + 全局提权）
│  │  ├─ App.axaml(.cs)            #   主题与全局样式（卡片/色板/设置按钮样式）；工具注册清单
│  │  ├─ MainWindow.axaml(.cs)     #   顶部导航（返回主页 + 🔌 MCP 服务设置按钮）+ 底部全局状态栏
│  │  ├─ ViewModels/MainViewModel.cs   # IToolHost 实现、卡片导航、提权重启、日志归集
│  │  │                            #   ToolCardViewModel：卡片状态 + ⚙ 背景设置命令
│  │  ├─ ViewModels/LogsViewModel.cs   # 操作日志页 VM：DataGridCollectionView 筛选 + 统计
│  │  ├─ ViewModels/McpSettingsViewModel.cs  # 「MCP 服务」设置弹窗 VM（服务启停/端口 + 接入授权）
│  │  ├─ Services/CardAppearanceStore.cs  # 卡片外观模型 + JSON 持久化 + 图片复制
│  │  ├─ Tools/LogsToolModule.cs   #   操作日志工具模块（复用宿主日志集合）
│  │  └─ Views/                    #   HomeView（卡片墙）、LogsView（日志页）、
│  │                               #   LogListView（状态栏 Flyout 复用）、CardColorWindow（取色器）、
│  │                               #   McpSettingsWindow（MCP 服务设置弹窗：服务/接入授权两 Tab）
│  └─ Tools/NetRouteList/
│     ├─ NetRouteList.Core/        # 路由核心库（无 UI 依赖）
│     │  ├─ Native/IpHelperNative.cs   # P/Invoke：GetIpForwardTable2 / Create/DeleteIpForwardEntry2
│     │  ├─ Models/                #   路由/网卡/备份/诊断模型
│     │  ├─ Services/              #   RouteService、AdapterService、BackupService、
│     │  │                         #   DiagnosticsService、ElevationHelper、RouteCommandText
│     │  └─ Docs/HelpDocs.cs       #   帮助文档与字段说明
│     └─ NetRouteList.Ui/          # 路由管理工具 UI（Avalonia MVVM，CommunityToolkit.Mvvm）
│        ├─ NetRouteListToolModule.cs  # ToolModule 实现：组装服务与视图
│        ├─ Views/NetRouteListView.axaml(.cs)  # 工具根视图（原主窗口改 UserControl）
│        ├─ ViewModels/            #   工具根 VM + 5 个页面 VM + 对话框 VM
│        └─ Views/                 #   页面视图 + 对话框
│  └─ Tools/PermDoctor/            # 权限医生（文件夹 ACL 诊断/修复）
│     ├─ PermDoctor.Core/          # ACL 核心库（无 UI 依赖；.NET 6+ 内置 ACL API）
│     │  ├─ Models/DiagnosisModels.cs   # ACL 条目/写入实测/诊断报告模型
│     │  └─ Services/              #   AclService（读取+有效权限+写入实测）、
│     │                             #   DiagnoseService（问题清单+等效命令）、FixService（一键修复）
│     └─ PermDoctor.Ui/            # 诊断页 UI（路径输入/报告卡/ACL 表/修复按钮）
│  └─ Tools/MenuClean/             # 清理右键菜单（三 Tab：场景/来源/全量列表）
│     ├─ MenuClean.Core/           # 菜单扫描核心（无 UI 依赖）
│     │  ├─ Models/MenuModels.cs   #   条目模型（场景/类型/状态/残留/来源）
│     │  └─ Services/              #   ContextMenuScanner（通用根+SystemFileAssociations+
│     │                             #   .ext→ProgID 间接路径，命令目标提取+残留检测）、
│     │                             #   MenuCleaner（LegacyDisable/Blocked/备份删除）、
│     │                             #   MenuBackupService（reg export/import）、
│     │                             #   EntryDetailService（详情实时读取）
│     └─ MenuClean.Ui/             # 三视图 UI + 共享详情面板 + 等待遮罩
│  └─ Tools/ShellExt/              # COM 扩展管理
│     ├─ ShellExt.Core/            # Shell 扩展扫描核心
│     │  ├─ Models/ShellExtModels.cs    # 扩展模型（CLSID 聚合/类型/疑似文字）
│     │  └─ Services/              #   ShellExtScanner（Approved/图标叠加/预览/文件类型聚合）、
│     │                             #   SuspectedTextScanner（DLL 字符串资源启发式分析，纯数据映射）、
│     │                             #   ShellExtCleaner（Blocked 黑名单禁用）
│     └─ ShellExt.Ui/              # 列表 UI + 行详情二级展示 + 等待遮罩
│  └─ Tools/PlcScanner/            # PLC IP 扫描（ARP 网段扫描，WinForms 版迁移）
│     ├─ PlcScanner.Core/          # 扫描核心（无 UI 依赖）
│     │  └─ Services/              #   ScanCore（SendARP/网段解析/端口探测/邻居缓存清理，原样迁移）、
│     │                             #   ScanOrchestrator（网卡枚举/扫描编排/结果回调）
│     └─ PlcScanner.Ui/            # 扫描页 UI（网卡/网段/进度/结果表）
│  └─ Tools/StartupMgr/            # 开机启动项管理（三 Tab：启动项/服务/计划任务）
│     ├─ StartupMgr.Core/          # 启动项核心
│     │  ├─ Models/                    #   启动项/服务/计划任务模型
│     │  └─ Services/                  # StartupScanner（10 类位置 + StartupApproved 禁用状态）、
│     │                                 #   StartupCleaner（禁用/删除备份还原）、
│     │                                 #   ServiceScanner（服务枚举 + Start 类型修改）、
│     │                                 #   TaskScanner（计划任务，基于 dahall/TaskScheduler COM）
│     └─ StartupMgr.Ui/            # 三 Tab 列表 UI + 等待遮罩
│  └─ Tools/NetTraffic/            # 流量监控（内核 ETW 按进程归因）
│     ├─ NetTraffic.Core/          # 流量核心
│     │  ├─ Models/TrafficModels.cs     #   进程流量行模型（INPC 实时刷新）
│     │  └─ Services/                  # EtwTrafficMonitor（ETW TCP/UDP 事件按 PID 聚合）、
│     │                                 #   ConnectionCounter（TCP 连接数）、
│     │                                 #   ProcessDetailService（进程详情/WMI 命令行/连接表）
│     └─ NetTraffic.Ui/            # 实时列表 + 统计卡 + 右键菜单 + 详情对话框
│  └─ Tools/SerialMcpTool/         # 串口 MCP（AI 客户端经 MCP 协议收发串口 + 卡片实时监控）
│     ├─ SerialMcpTool.Core/       # 串口核心（无 UI 依赖）
│     │  ├─ SerialTraffic.cs           #   收发方向/条目模型 + 文本转义/HEX 解析工具
│     │  ├─ SerialPortService.cs       #   串口封装：枚举(WMI 描述)/开关/收发/等待应答；
│     │  │                             #   读取走「事件 + 主动轮询」双路径（不依赖 DataReceived 触发）
│     │  ├─ McpServer.cs               #   streamable HTTP MCP 服务器（HttpListener + JSON-RPC 2.0 手实现，
│     │  │                             #   仅本机回环 + Origin 校验 + 令牌校验；6 个串口工具）
│     │  └─ SerialMcpRuntime.cs        #   进程级单例（跨视图存活）+ IMcpServiceDefinition 实现
│     └─ SerialMcpTool.Ui/         # 串口设置 + MCP 状态条 + 收发监控列表 + 手动发送
│  └─ Tools/FileTransferTool/      # 文件传输 MCP（FTP/SFTP/SCP 三协议分页 + 传输记录）
│     ├─ FileTransferTool.Core/    # 传输核心（无 UI 依赖）
│     │  ├─ TransferModels.cs          #   协议/方向/站点/记录模型 + 站点持久化 transfer-sites.json
│     │  ├─ TransferServices.cs        #   三协议实现：FTP（FluentFTP）/ SFTP、SCP（SSH.NET，密码或私钥）
│     │  └─ TransferMcpRuntime.cs      #   进程级单例 + McpHttpServer 子类（5 个传输工具）
│     └─ FileTransferTool.Ui/      # FTP/SFTP/SCP 三 Tab（站点表+手动传输）+ 传输记录页 + 站点编辑弹窗
└─ tools/RouteDump/                # 命令行快照/自测工具（只依赖 Core）
```

## 如何新增一个工具

> 完整流程与纪律见 **[AGENTS.md](AGENTS.md)**，以下为速览。

1. 在 `src/Tools/` 下新建 `XxxTool.Core` + `XxxTool.Ui` 类库（参考任一现有工具的 csproj），Ui 引用 `Toolkit.Shell` 与自己的 Core；
2. 实现一个 `ToolModule` 子类：`Id / Title / Glyph / Description / CreateView()`（视图的 DataContext 由模块自行配对），需要提权或报状态走 `Host`；
3. 在 `Toolkit.App/App.axaml.cs` 的 `InitializeTools(...)` 清单里注册；
4. 主页自动出现新卡片。全局样式（`mono` / `card` / `sectionTitle` 等）在 `Toolkit.App/App.axaml`，工具视图直接复用；
5. **接入统一日志**：工具内任何 `AddLog`/日志方法里调 `Host.Log(本工具名, message)` 即可归集到「操作日志」卡片与状态栏日志 Flyout（消息以 `✔`/`✘` 开头会自动着绿/红）。参考 `NetRouteListViewModel.AddLog`。

## 架构与迁移说明

- **宿主与工具解耦**：`Toolkit.App → NetRouteList.Ui → {NetRouteList.Core, Toolkit.Shell}`，工具不引用宿主，只依赖 Shell 抽象，避免循环引用。
- **UIA/自动化调试支持**：关键控件均设置了 `AutomationProperties.AutomationId/Name`，可用 UIA 工具（如 laoqinskills 的 uia）按 id 精确定位与 Invoke，无需依赖坐标：

  | 控件 | AutomationId | 名称 |
  |------|--------------|------|
  | 工具卡片 | `card-{工具Id}`（card-netroutelist / card-logs / card-placeholder） | 工具名（如“路由管理”） |
  | 卡片 ⚙ 设置按钮 | `cardsettings-{工具Id}` | 设置卡片背景 |
  | 色板色块 | `swatch-{色值}`（如 swatch-4FC3F7） | 色块·浅蓝 等 |
  | Flyout 功能按钮 | `btn-custom-color` / `btn-pick-image` / `btn-reset-appearance` | 按钮文本 |
  | 取色对话框 | `btn-color-ok` / `btn-color-cancel` | 确定/取消 |
  | 状态栏 | `btn-elevate` / `btn-logs-flyout` | 按钮文本 |
  | 日志页 | `combo-level` / `combo-source` / `btn-clear-logs` / `list-logs` | 级别筛选 等 |
  | 日志列表项 | `log-{HHmmss}` | 日志消息文本 |
  | 权限医生 | `input-path` / `btn-browse` / `btn-diagnose` / `grid-acl` / `btn-fix-grant` / `btn-fix-reset` / `btn-fix-owner` / `tb-commands` / `btn-copy-commands` / `tb-status` | 对应中文功能名 |
  | 清理右键菜单 | `tab-scene` / `tab-source` / `tab-list` / `btn-scan` / `tb-search` / `list-scenes` / `switch-entry` / `grid-entries` / `expander-source` / `btn-clean-residual` / `btn-backups` / `btn-restart-explorer` / `panel-detail` / `btn-close-detail` / `tb-detail-commands` / `panel-busy` / `tb-status` | 对应中文功能名 |
  | COM 扩展管理 | `combo-kind` / `tb-search` / `btn-scan` / `grid-exts` / `switch-ext` / `row-details`（行详情）/ `btn-restart-explorer` / `panel-busy` / `tb-status` | 对应中文功能名 |
  | 开机启动项 | `tab-startup` / `tab-services` / `btn-scan` / `tb-search` / `grid-startups` / `switch-item` / `btn-delete` / `input-new-name` / `btn-add` / `combo-service-filter` / `grid-services` / `btn-svc-auto` / `btn-svc-manual` / `btn-svc-disable` / `panel-busy` / `tb-status` | 对应中文功能名 |
  | PLC IP 扫描 | `combo-nic` / `btn-refresh-nic` / `chk-virtual` / `input-ranges` / `btn-fill-ranges` / `chk-hidelocal` / `chk-ports` / `input-ports` / `btn-start` / `btn-stop` / `btn-clear` / `btn-probe`（行内探测）/ `grid-devices` / `tb-status` / `tb-eta` / `tb-found` / `tb-progress` | 对应中文功能名 |
  | 流量监控 | `btn-toggle` / `btn-reset` / `btn-elevate` / `tabs-traffic` / `tab-realtime` / `grid-traffic` / `tb-status` / 历史页 `tab-history` / `combo-history-range` / `btn-loadhistory` / `btn-copyhistory` / `grid-history`（双击行开曲线）/ `tb-historystats` / 曲线弹窗 `chart-traffic` / `tb-chart-stats` / 右键菜单 `menu-chart` / 代理页 `tab-proxy` / `input-proxy-uri` / `input-proxy-secret` / `btn-proxy-load` / `chk-proxy-auto` / `tb-proxy-stats` / `grid-proxy-proc` / `grid-proxy-conns` `menu-detail` / `menu-openlocation` / `menu-websearch` / `menu-copyname` / `menu-kill` / 详情窗 `grid-detail-conns` | 对应中文功能名 |
  | 自绘标题栏 | `btn-mcpsettings` / 窗口控制 `btn-win-min` / `btn-win-max` / `btn-win-close` | MCP 服务设置 / 最小化 / 最大化或还原 / 关闭 |
  | 串口 MCP | `combo-serport` / `btn-refreshports` / `combo-baud` / `combo-databits` / `combo-parity` / `combo-stopbits` / `btn-openport` / `btn-closeport` / `tb-portstatus` / `tb-mcpstate` / `btn-mcpstart` / `btn-mcpstop` / `btn-copyurl` / `chk-hexmode` / `chk-autoscroll` / `btn-clearlog` / `btn-copyall` / 筛选标签 `chk-filter-all` / `chk-filter-fromport` / `chk-filter-ai` / `chk-filter-aicall` / `chk-filter-manual` / `chk-filter-info` / `chk-filter-error` / `list-serialtraffic`（多选）/ 右键菜单 `menu-copy-selected` / `menu-copy-all` / `input-manualdata` / `btn-manualsend` / `btn-send` / `chk-manualhex` / `chk-appendnl` / `input-linehistory` / `tb-stats` | 对应中文功能名（监控行 Name=显示内容） |
  | 文件传输 | `tabs-transfer` / `tab-sites` / `tab-browse` / `tab-records` / 站点表 `grid-sites` / `btn-addsite` / `btn-editsite` / `btn-delsite` / `btn-refreshsites` / 浏览页 `tab-browse` / `combo-browse-site` / `input-browse-path` / `btn-browse-go` / `btn-browse-up` / `btn-browse-refresh` / `grid-browse` / `input-browse-local` / `btn-browse-download` / `btn-browse-open` / `btn-browse-use` / 手动传输 `combo-site` / `combo-dir` / `input-local` / `input-remote` / `btn-transfer`/ 记录页 `list-records` / `btn-clearrec` / `btn-copyrec` / 状态条 `tb-mcpstate` / `btn-mcpstart` / `btn-mcpstop` / `btn-mcpsettings` / `tb-stats` / 站点弹窗 `input-site-name` / `btn-site-test`（就地测试连接） / `combo-site-proto` / `input-site-host` / `input-site-port` / `input-site-user` / `input-site-pass` / `input-site-key` / `btn-site-browsekey`（文件选择私钥）/ `chk-site-passive`（端口默认值跟随协议：FTP 21 / SFTP·SCP 22，切换自动更新） / `btn-site-ok` / `btn-site-cancel` | 对应中文功能名 |
  | MCP 设置弹窗 | `tabs-mcpsettings` / `tab-mcp-services` / `tab-mcp-access` / `input-svcport` / `btn-svcstart` / `btn-svcstop` / `btn-copysvcurl` / `btn-copyaccessurl` / `input-newaccess` / `btn-addaccess` / `list-accessitems` / `btn-copytoken` / `switch-access` / `btn-delaccess` / `tb-mcpmsg` | 对应中文功能名 |
  | 状态栏（主题） | `btn-theme` | 主题切换 |

  `GearIcon` 自定义控件示例了自定义 `AutomationPeer`（`GetNameCore() => "齿轮"`）。新增工具时建议同样为卡片内可交互控件设置 AutomationId。
- **统一日志**：`IToolHost.Log(source, message)` 是唯一上报通道 → 宿主 `MainViewModel.Logs`（`ObservableCollection<ToolLogEntry>`，上限 500，最新在前）→ 状态栏“日志”Flyout 与「操作日志」工具页两个视图共用；级别由消息前缀（✔/✘）解析。工具内部各自的日志（如路由管理的操作日志 Flyout）不受影响。日志页筛选用 `DataGridCollectionView`（Avalonia.Controls.DataGrid 包）。
- **操作日志是内置工具**：`LogsToolModule` 位于 Toolkit.App 内（日志数据本来就在宿主），`LogsViewModel` 直接包装 `MainViewModel.Logs`，不走 IToolHost 抽象。
- **卡片背景自定义**：每张卡片（含占位卡）的 ⚙ Flyout 命令 → `MainViewModel.SetCardAppearance(key, …)` → 更新内存字典 + `CardAppearanceStore` 持久化 + 即时刷新卡片。**背景直接绑定按钮本体**（`Background="{Binding CardBackground}"`，默认绿色非空 → 点击命中可靠），图片用 `ImageBrush(Stretch=UniformToFill)` + 半透明黑遮罩 + 白色文字（`Classes.customBg` 样式切换）。占位卡用 `Classes.disabled`（视觉禁用但保持可交互，⚙ 仍可点），不用 `IsEnabled`。
- **卡片背景持久化位置**：`%LOCALAPPDATA%\SystemToolkit\card-appearance.json`；所选图片复制到 `SystemToolkit\cardbg\`（避免原图移动后失效）。
- **主题切换**：状态栏按钮循环 `Application.RequestedThemeVariant`（跟随系统→浅色→深色），全部 `DynamicResource` 即时刷新；注意 Avalonia 12 的 `BoxShadowTransition` 已移除（悬停动效只用 `TransformOperationsTransition`），Transform 字符串须写 `translate(0px,-3px)`（带括号与单位）。
- **菜单/扩展工具的禁用机制**：shell 命令项禁用 = HKCU 同路径写 `LegacyDisable`（HKCU 对 HKCR 的合并优先级高于 HKLM，普通权限可写、完全可逆）；shellex/COM 扩展禁用 = `HKCU\...\Shell Extensions\Blocked` 黑名单加 CLSID。扩展类禁用需资源管理器新进程生效 → 工具内提供一键重启。COM 扩展的菜单文字由 DLL 运行时生成（注册表搜不到），用 `LoadLibraryEx(LOAD_LIBRARY_AS_DATAFILE)` 纯数据映射 DLL 遍历字符串资源做启发式“疑似菜单文字”分析（不执行任何 DLL 代码）。
- **提权是进程级操作**：由宿主 `MainViewModel.ElevateRestart()` 统一处理（`ElevationHelper.RestartElevated` 用 `Environment.ProcessPath` 重启当前 exe），工具内按钮仅转发到 `IToolHost.ElevateRestart()`。
- **MCP 服务架构（聚合网关）**：**一条接入 URL（默认 `http://127.0.0.1:8642/mcp?token=…`）暴露全部启用的工具**——由 Shell 层的 `McpGatewayRuntime` 聚合网关统一对外（单端口，HTTP 为手实现 streamable HTTP：`HttpListener` + JSON-RPC 2.0，仅监听本机回环 + 浏览器 Origin 校验，通知 202、GET/DELETE 405）。任何工具想暴露 MCP 能力：实现 `IMcpServiceDefinition`（**能力**：工具清单 `BuildTools`/执行 `ExecuteTool`/启用开关 `SetEnabled`）并注册到 `McpServiceRegistry`（静态构造注册即可）——工具自动进网关，**AI 客户端配置永远不用改**；顶栏「🔌 MCP 服务」弹窗 = 网关启停/端口 + 各能力开关（关掉即从 tools/list 移除，调用返回明确提示）+ 接入授权。接入授权全局统一：`McpAccessStore` 按"接入项=一个 AI 客户端"生成 32 位随机令牌，停用/删除后立即失效（每次 HTTP 请求校验），同一令牌可访问网关上全部启用的能力。各能力运行时（如 `SerialMcpRuntime`/`TransferMcpRuntime`）为进程级单例，切换工具视图不中断。
- **串口读取注意**：`SerialPort.DataReceived` 事件在某些驱动（如 com0com 虚拟串口）下存在触发丢失——串口服务的等待/读取路径均为「事件 + 主动轮询驱动缓冲」双路径（`ReadDriverIntoPending`：只读 `BytesToRead` 可用字节，不请求大缓冲凑满），实测应答在等待窗口内即时收到。串口打开时 `DtrEnable/RtsEnable` 拉有效（与 Xshell/串口助手默认一致）——嵌入式调试串口（SylixOS 实测）使能/复位脚接在 DTR/RTS 上时，信号无效设备完全不收发。手动发送支持「附加回车」开关（默认开，shell/固件通常收到换行才执行）与「复制全部记录」（整段会话留档/分析）。
- **MCP 接入数据持久化**：`%LOCALAPPDATA%\SystemToolkit\mcp-access.json`（接入项令牌/统计 + 各服务记忆端口）；首次使用自动创建"默认接入"项。
- **对话框归属**：工具视图通过 `TopLevel.GetToplevel(this)` 取宿主主窗口作为 `ShowDialog` 的 Owner；`Dialogs.Owner` 静态单例在单窗口宿主下依然成立；宿主自己的对话框（取色器）走 `AppDialogs.Owner`。
- **路由读取**：`GetIpForwardTable2`（P/Invoke），不受系统显示语言影响。注意 `MIB_IPFORWARD_ROW2` 实际 ABI（104 字节）与 MSDN 语法页字段顺序不一致：`Origin` 在实际内存布局中位于结构**末尾**。
- **跃点语义**：`route print` 显示的跃点 = 接口跃点 + 路由跃点，界面显示总跃点并拆解说明。
- **备份目录**：`%LOCALAPPDATA%\NetRouteList\backups\*.json`（沿用原独立版目录，旧备份继续可用）。

## 已验证

- 解决方案 Release 构建 0 错误；警告与迁移前一致（Core Windows API 的 CA1416 平台标注，无新增类别）。
- UI 实测：主页卡片渲染 → 进入路由管理（总览数据、备份列表加载旧备份）→ 普通权限红字提示 → “以管理员重启” UAC 流程 → 重启后回到主页且权限变绿。
- 权限医生实测：诊断 `E:\Programs\Trae CN`（安装“错误 5”现场）命中孤儿 SID/缺写权限/写入被拒全部问题；修复按钮普通权限下正确引导提权；实测授予修改权限后安装成功。
- 清理右键菜单实测：本机扫描 925 项（第三方 202 / 残留 59 / 已禁用 65），ProgID 间接路径补全后覆盖右键 .exe 的全部注入项；残留一键清理、禁用/启用、.reg 备份还原、搜索三视图联动、共享详情（注册表值/文件信息/等效命令）均验证。
- COM 扩展管理实测：本机 106 个扩展（图标叠加 35，超系统 15 上限）；搜“手机”经疑似菜单文字命中夸克扩展并展开显示其全部菜单文字（发送到手机/PDF转Word 等）；黑名单禁用写入注册表实测生效，重启资源管理器后菜单项消失。
- 开机启动项实测：本机三页扫描 —— 启动项 36（含 Active Setup 4 与 UWP StartupTask 10；已禁用状态与任务管理器互通）· 服务 359（自动 120）· 计划任务 202（登录触发 36 · 第三方 8；TaskCache/Tasks 目录锁死由 COM 通道绕过）；10 类启动位置全覆盖。
- 操作日志卡片实测：点击进入 → 统计/级别筛选/来源筛选/清空均正常，进入工具本身产生日志并实时归集。
- 卡片背景实测：⚙ Flyout 色板即时变色、取色器自定义色、本地图片背景均正常；重启后配置完整恢复；主题循环切换（跟随系统/浅色/深色）即时生效。
- PLC IP 扫描实测：192.168.1.0/24 全段 254 地址 → 发现 19 台设备（IP/MAC 全部正确，含 00-30-1B 工控设备网段）；端口识别（S7/Modbus/EtherNet-IP/SC）与单行「🔍 探端口」补测实测通过（157/158/159 探出 23）。
- 流量监控实测：管理员下内核 ETW 会话正常启动，进程实时速率列表正常显示（用户确认）；非管理员下明确拦截并引导提权；右键菜单五项（详情/文件位置/搜索/复制/结束）与详情对话框（含 TCP 连接表）已交付。
- 串口 MCP 实测（com0com 虚拟串口对 COM4↔COM6 模拟设备）：MCP 协议全通——initialize（协议版本回显）/ tools/list（6 工具）/ tools/call 全部正常；无令牌与已删除令牌返回 401，通知返回 202；`open_serial_port` + `send_serial_data("AT\r\n")` → 设备收到 AT 并回 `+OK\r\n`，等待窗口内收到应答（转义文本 + HEX 双格式返回）；HEX 模式发送、无应答超时、串口未开报错（isError）、关闭/重开均验证；卡片监控列表实时显示 `AI→串口`/`串口→返回` 行与收发统计；「MCP 服务」弹窗服务启停/端口记忆/新增删除接入项/复制含令牌 URL 验证通过。
- 文件传输实测（本机 pyftpdlib 临时 FTP 服务器）：MCP 5 工具全通——`transfer_file_direct` 上传（远端落地内容一致）与下载（内容一致）、按站点 `transfer_file` 上传、`list_remote_dir` 列目录、`get_transfer_status`；密码错误返回 isError + 服务端原因（530 Authentication failed）；AI 调用记录参数摘要中**密码自动脱敏 `***`**；界面侧站点新增弹窗（uia 填表保存）→ 站点表立即出现 → MCP `list_transfer_sites` 立即可见；传输记录页实时显示每次传输（协议/站点/大小/耗时/速度）。
- RouteDump 输出与本机 `route print` 一致（结构体 ABI 自检通过）。
