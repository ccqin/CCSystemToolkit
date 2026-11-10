# 系统管理工具集 · 开发指南（AGENTS.md）

面向在本仓库工作的 AI 助手与贡献者：架构约定、代码模式、构建验证流程。读完即可上手新增工具或改现有工具。

## 项目是什么

基于 **Avalonia 12 + .NET 10** 的 Windows 系统管理工具合集：卡片式主页 + **一个工具一个模块（ToolModule）**。全部 UI 中文。仅面向 Windows（大量 P/Invoke 与注册表/ETW/COM）。

## 目录结构（约定）

```
SystemToolkit.sln
├─ src/
│  ├─ Toolkit.Shell/               # 抽象层：IToolHost（Log/ElevateRestart/IsElevated/StatusText）、ToolModule 基类、ToolLog
│  ├─ Toolkit.App/                 # 宿主：主页卡片墙、工具切换、全局提权、统一日志、主题切换、卡片背景自定义
│  └─ Tools/<工具名>/
│     ├─ <Name>.Core/              # 核心库（必须无 UI 依赖；服务/模型/P/Invoke 都在这）
│     └─ <Name>.Ui/                # Avalonia UI（CommunityToolkit.Mvvm；引用 Shell + 自己的 Core）
└─ tools/RouteDump/                # 命令行自测工具
```

**依赖方向（严格）**：`Toolkit.App → 各工具.Ui → {各工具.Core, Toolkit.Shell}`。工具**不引用宿主**，只通过 `ToolModule.Host`（IToolHost）访问提权/日志/状态。禁止循环引用。

## 新增一个工具的标准流程

1. `src/Tools/XxxTool/` 下建 `XxxTool.Core` + `XxxTool.Ui` 两个类库（照抄任意现有工具的 csproj：Avalonia 12.1.4 / DataGrid 12.1.2 / CommunityToolkit.Mvvm 8.4.2；`AvaloniaUseCompiledBindingsByDefault=false`）；
2. Core 放扫描/操作逻辑（静态服务或实例服务），**UI 无关、可单测**；
3. Ui 实现 `XxxToolModule : ToolModule`（`Id/Title/Glyph/Description/CreateView()`；`const string ToolTitle` 同时作统一日志来源名）；
4. 注册三处：`dotnet sln add` 两个 csproj；`Toolkit.App.csproj` 加 ProjectReference；`App.axaml.cs` 的 `InitializeTools` 清单加一行；
5. 主页自动出现卡片。**必须设置 AutomationProperties.AutomationId/Name**（见"UIA 调试"）；
6. 日志：VM 里 `Host.Log(ToolTitle, message)`；消息以 `✔`/`✘`/`△` 开头会被统一日志面板着色（绿/红）。

## 现有工具速览（都在 src/Tools/ 下）

| Id | 工具 | 核心机制要点 |
|----|------|-------------|
| netroutelist | 🌐 路由管理 | P/Invoke GetIpForwardTable2；注意 MIB_IPFORWARD_ROW2 实际 ABI 与 MSDN 字段顺序不一致（Origin 在末尾） |
| permdoctor | 🔐 权限医生 | .NET 内置 ACL API（GetAccessControl 只读 DACL+Owner，读 SACL 需 SeSecurityPrivilege 会抛异常）；禁用写 HKCU 覆盖（合并视图优先于 HKLM） |
| menuclean | 🧹 清理右键菜单 | 场景=注册表位置聚合（*、Directory、ProgID 间接路径等）；禁用=HKCU LegacyDisable/Blocked 黑名单（可逆）；删除前 reg export 备份 |
| shellext | 🧩 COM 扩展管理 | Approved 清单+CLSID 组件名；"疑似菜单文字"=LoadLibraryEx(AS_DATAFILE) 纯数据映射扫字符串资源（LoadString 必须取 ToString(0,len)，全 buffer 会带残留出空行） |
| startupmgr | 🚀 开机启动项 | 10 类位置；禁用=StartupApproved（任务管理器同机制）；计划任务用 dahall/TaskScheduler NuGet（COM 通道——本机 TaskCache/Tasks 目录 ACL 锁死时依然可用）；服务=HKLM Services Start 值 |
| plcscanner | 📡 PLC IP 扫描 | WinForms 项目迁移；ScanCore 原样保留；SendARP 多源地址探测；无引号含空格命令要逐段累积找真实文件（否则误判残留） |
| nettraffic | 📶 流量监控 | 内核 ETW NETWORK_TCPIP（TraceEvent：kernel.TcpIpSend/Recv 等 8 个事件）按 PID 聚合，需管理员；右键详情=ProcessDetailService（WMI 命令行）+GetExtendedTcpTable 连接表 |
| logs | 📋 操作日志 | 内置工具（LogsToolModule 在宿主内），直接包 MainViewModel.Logs |

## 宿主通用机制（做 UI 前先读）

- **提权是进程级**：`IToolHost.ElevateRestart()` → UAC 重启整个 exe。工具内只发请求，不自己拉起管理员进程。提权后 UIA 工具（普通权限）无法操作该窗口，属预期。
- **统一日志**：`MainViewModel.Logs`（上限500，最新在前）；`Log(source, message)` 是唯一通道。
- **全局样式**：`Toolkit.App/App.axaml` 的 `card / sectionTitle / cardLabel / cardValue / mono / busyOverlay / toolcard` 等类，工具视图直接用 Classes 复用，不要自造重复样式。
- **主题**：`RequestedThemeVariant` 循环（跟随系统/浅色/深色）；颜色一律 `DynamicResource`。Avalonia 12 已移除 BoxShadowTransition；Transform 字符串必须写 `translate(0px,-3px)`（带括号+单位），写 `translate 0 -3` 启动即崩。
- **卡片背景**：背景**直接绑定按钮本体** `Background="{Binding CardBackground}"`（默认绿色非空保证命中），另加透明命中兜底层 —— Fluent 模板背景层不参与 hit-test，没有兜底层时悬停/点击只在文字上生效。

## UIA / 自动化调试（强制规范）

所有关键可交互控件必须设置 `AutomationProperties.AutomationId` 与 `Name`（与全局 AGENTS.md 规范一致），命名 `控件前缀-语义名`（btn-/combo-/grid-/list-/tab-/input-/chk-/tb-/menu-）。完整清单在 README。调试用 laoqinskills 的 uia 脚本按 `--id`/`--name` 定位与 Invoke，不依赖坐标。

**注意**：uia 对 Avalonia Flyout 内按钮的 Invoke 可能落到宿主卡片上（历史教训）；验证 Flyout 交互用真实点击。TabItem 切换用 `--mode select`；ComboBox 选项在 `--any-window` 下搜索。

## 构建 / 验证流程（每次改动必须走）

```bash
dotnet build SystemToolkit.sln -c Release   # 必须 0 error（既有 CA1416 平台警告可忽略，不许新增类别）
src/Toolkit.App/bin/Release/net10.0/Toolkit.App.exe   # 运行验证
```

- **应用运行时会锁 bin 输出**：构建报 MSB3021/3027 先 `taskkill //IM Toolkit.App.exe //F` 再重建；**提权实例普通权限杀不掉**（拒绝访问），需要用户手动关闭后才能部署。
- 改 Core 后如果运行时行为诡异（旧样式/旧逻辑），先怀疑**增量构建没产出新 dll**：rm -rf obj bin 后全量重建。
- UI 改动用 uia 验证（见上节）；视觉/布局类改动截图给用户确认。

## 提交纪律

- 每个功能/修复一个 commit，中文提交信息说清"做了什么+实测结果"；
- 用户没说提交就不要提交，更不要 push；
- 远程：github.com/ccqin/CCSystemToolkit（master）。仓库可见性建议保持 Private。

## 已知坑（历史教训，别再踩）

1. Fluent 按钮模板背景不参与命中测试 → 透明兜底层（见宿主机制节）；
2. Avalonia 12：`ColorPicker` 独立包 + 选中属性叫 `Color`；剪贴板 `SetTextAsync` 在 `Avalonia.Input.Platform` 命名空间（扩展方法）；`FileSystemRights.None` 不存在（用 default）；ListBox 无 HorizontalContentAlignment；
3. `System.IO.FileSystem.AccessControl` NuGet 最高 6.0.0（AI 常记错成 8.x）——.NET 6+ ACL API 其实内置，**根本不用加包**；
4. SendARP/注册表命令解析：无引号但含空格的路径（`E:\Program Files (x86)\...`）必须逐段累积验证存在性；
5. Windows 加固系统可能锁死 TaskCache 注册表/Tasks 目录 → 计划任务走 COM（TaskScheduler NuGet）；
6. ETW 内核会话必须管理员；非管理员要给出明确引导（以管理员重启按钮）。
