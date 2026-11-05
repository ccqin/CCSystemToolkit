# 系统管理工具集

基于 **Avalonia 12 + .NET 10** 的 Windows 系统管理工具合集：卡片式主页 + 一个工具一个模块。首个内置工具为 **路由管理**（原独立项目 NetRouteList 迁移而来）。

## 功能总览

| 工具 | 功能 |
|------|------|
| 🌐 **路由管理** | **总览**：统计卡片 + 自动诊断（多条默认路由、缺失 on-link 直连路由可一键修复、跨网卡网段冲突、APIPA、代理/VPN TUN 接管提示）。**路由表**：全部 IPv4/IPv6 路由，按网卡筛选、按目标/网关/接口搜索，添加（可选持久 `-p`）/修改/删除（核心系统路由受保护），每步显示等效 `route add` / `netsh` 命令。**网卡**：状态、IP、网关、接口跃点，IP 缺直连路由红字告警。**备份**：JSON 备份（路由+网卡快照）、差异对比、增量合并/完全恢复、安全快照、按网卡名重映射接口索引、导入导出。**帮助**：9 章中文文档。 |
| 🔐 **权限医生** | 诊断/修复文件夹 ACL 权限（安装程序“错误 5：拒绝访问”等问题）：**诊断报告** = 写入实测（真实创建/删除子目录）+ 当前用户有效权限 + 所有者/继承状态 + 问题清单（孤儿 SID、断继承、缺权限）；**权限明细表** 列出全部 ACL 条目（身份/权限/应用于/继承来源，标注孤儿 SID 与匹配当前用户的条目）；**一键修复**（授予修改权限 / 恢复继承 / 取得所有权）或复制等效 `icacls`/`takeown` 命令到管理员终端。 |
| 🧹 **清理右键菜单** | 按「右键场景」管理经典右键菜单（Win11 在「显示更多选项」中），三个 Tab 页：**场景视图**（左选场景：任意文件/文件夹/空白处/驱动器/后缀动态发现，右侧模拟真实菜单逐项开关，显示本场景条目数）；**来源视图**（按软件聚合成可展开组，一键禁用/启用全部注入点）；**全量列表**（搜索过滤、列宽可调）。扫描覆盖 `*`/`Directory`/`AllFilesystemObjects` 等通用根 + `SystemFileAssociations` + `.后缀→ProgID` 间接路径（exefile 等）。自动检测**软件卸载残留**（命令指向不存在的文件，一键清理）、识别 Microsoft/系统项并保护。禁用走 HKCU（完全可逆）、删除前自动 .reg 备份可还原；共享详情面板实时读取注册表值与文件版本信息、生成等效 reg 命令（可复制）；搜索三视图联动；扫描等待遮罩；shellex 禁用后可一键重启资源管理器。 |
| 🧩 **COM 扩展管理** | 管理 Shell COM 扩展：右键菜单的**动态项**（如“发送到手机”，注册表搜不到文字）、**图标叠加**（系统上限 15 个，超出的排队浪费）、缩略图、预览窗格、属性页。描述三级回退（**Approved 官方清单 → CLSID 组件名 → 键名**）；**疑似菜单文字分析**：以纯数据方式映射 DLL（不执行代码）遍历字符串资源，启发式筛出该扩展实际显示的菜单文字（如夸克的“发送到手机/PDF转Word…”），点击行展开二级详情查看；DLL 残留检测、按 CLSID 聚合注册点、类型筛选与搜索；黑名单禁用可逆 + 一键重启资源管理器；扫描等待遮罩。 |
| 🚀 **开机启动项** | 管理开机启动项：注册表 **Run/RunOnce**（HKCU / HKLM / 32 位视图）+ **启动文件夹**（用户/全局）。**禁用走系统 StartupApproved 机制**（与任务管理器/设置的禁用状态互通，完全可逆）；卸载残留检测（目标文件不存在标 ⚠）；删除前自动备份（注册表值/启动文件，可一键还原）；支持**新增** HKCU·Run 启动项；搜索过滤 + 扫描等待遮罩。计划任务与服务自启不在范围。 |
| 📋 **操作日志** | 以工具卡片形式进入的完整日志页：按级别（信息/成功/错误）与来源（各工具）筛选，实时统计“共 N 条 · ✔ · ✘”，一键清空；与状态栏“日志”Flyout 共用同一份数据。 |
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

### 权限说明

- **查看**路由表、网卡、诊断、创建备份：普通权限即可。
- **修改**路由（增删改、恢复备份、一键修复）：需要管理员。主页/工具内点击“以管理员重启”，UAC 确认后**整个工具集**以管理员身份重启（重启后回到主页）。
- 任何被拒绝的写操作都会显示等效命令，可复制到管理员命令行手动执行。

## 项目结构

```
SystemToolkit.sln
├─ src/
│  ├─ Toolkit.Shell/               # 工具集抽象层
│  │  ├─ IToolHost.cs              #   宿主能力：IsElevated / StatusText / Log / ElevateRestart
│  │  ├─ ToolModule.cs             #   工具模块基类：Id/Title/Glyph/Description/CreateView
│  │  └─ ToolLog.cs                #   统一日志条目与级别
│  ├─ Toolkit.App/                 # 宿主应用（卡片式主页 + 工具切换 + 全局提权）
│  │  ├─ App.axaml(.cs)            #   主题与全局样式（卡片/色板/设置按钮样式）；工具注册清单
│  │  ├─ MainWindow.axaml(.cs)     #   顶部"返回主页"导航 + 底部全局状态栏（权限/提权/日志 Flyout）
│  │  ├─ ViewModels/MainViewModel.cs   # IToolHost 实现、卡片导航、提权重启、日志归集
│  │  │                            #   ToolCardViewModel：卡片状态 + ⚙ 背景设置命令
│  │  ├─ ViewModels/LogsViewModel.cs   # 操作日志页 VM：DataGridCollectionView 筛选 + 统计
│  │  ├─ Services/CardAppearanceStore.cs  # 卡片外观模型 + JSON 持久化 + 图片复制
│  │  ├─ Tools/LogsToolModule.cs   #   操作日志工具模块（复用宿主日志集合）
│  │  └─ Views/                    #   HomeView（卡片墙）、LogsView（日志页）、
│  │                               #   LogListView（状态栏 Flyout 复用）、CardColorWindow（取色器）
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
│  └─ Tools/StartupMgr/            # 开机启动项管理
│     ├─ StartupMgr.Core/          # 启动项核心
│     │  ├─ Models/StartupModels.cs    # 启动项模型（位置/状态/残留）
│     │  └─ Services/                  # StartupScanner（Run/RunOnce/启动文件夹 +
│     │                                 #   StartupApproved 禁用状态读取）、
│     │                                 # StartupCleaner（禁用/删除备份还原/新增）
│     └─ StartupMgr.Ui/            # 列表 UI（开关+删除+新增+备份）+ 等待遮罩
└─ tools/RouteDump/                # 命令行快照/自测工具（只依赖 Core）
```

## 如何新增一个工具

1. 在 `src/Tools/` 下新建 `XxxTool.Ui` 类库（参考 NetRouteList.Ui 的 csproj），引用 `Toolkit.Shell`；
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
  | 开机启动项 | `btn-scan` / `tb-search` / `grid-startups` / `switch-item` / `btn-delete` / `input-new-name` / `input-new-command` / `btn-add` / `btn-backups` / `panel-busy` / `tb-status` | 对应中文功能名 |
  | 状态栏（主题） | `btn-theme` | 主题切换 |

  `GearIcon` 自定义控件示例了自定义 `AutomationPeer`（`GetNameCore() => "齿轮"`）。新增工具时建议同样为卡片内可交互控件设置 AutomationId。
- **统一日志**：`IToolHost.Log(source, message)` 是唯一上报通道 → 宿主 `MainViewModel.Logs`（`ObservableCollection<ToolLogEntry>`，上限 500，最新在前）→ 状态栏“日志”Flyout 与「操作日志」工具页两个视图共用；级别由消息前缀（✔/✘）解析。工具内部各自的日志（如路由管理的操作日志 Flyout）不受影响。日志页筛选用 `DataGridCollectionView`（Avalonia.Controls.DataGrid 包）。
- **操作日志是内置工具**：`LogsToolModule` 位于 Toolkit.App 内（日志数据本来就在宿主），`LogsViewModel` 直接包装 `MainViewModel.Logs`，不走 IToolHost 抽象。
- **卡片背景自定义**：每张卡片（含占位卡）的 ⚙ Flyout 命令 → `MainViewModel.SetCardAppearance(key, …)` → 更新内存字典 + `CardAppearanceStore` 持久化 + 即时刷新卡片。**背景直接绑定按钮本体**（`Background="{Binding CardBackground}"`，默认绿色非空 → 点击命中可靠），图片用 `ImageBrush(Stretch=UniformToFill)` + 半透明黑遮罩 + 白色文字（`Classes.customBg` 样式切换）。占位卡用 `Classes.disabled`（视觉禁用但保持可交互，⚙ 仍可点），不用 `IsEnabled`。
- **卡片背景持久化位置**：`%LOCALAPPDATA%\SystemToolkit\card-appearance.json`；所选图片复制到 `SystemToolkit\cardbg\`（避免原图移动后失效）。
- **主题切换**：状态栏按钮循环 `Application.RequestedThemeVariant`（跟随系统→浅色→深色），全部 `DynamicResource` 即时刷新；注意 Avalonia 12 的 `BoxShadowTransition` 已移除（悬停动效只用 `TransformOperationsTransition`），Transform 字符串须写 `translate(0px,-3px)`（带括号与单位）。
- **菜单/扩展工具的禁用机制**：shell 命令项禁用 = HKCU 同路径写 `LegacyDisable`（HKCU 对 HKCR 的合并优先级高于 HKLM，普通权限可写、完全可逆）；shellex/COM 扩展禁用 = `HKCU\...\Shell Extensions\Blocked` 黑名单加 CLSID。扩展类禁用需资源管理器新进程生效 → 工具内提供一键重启。COM 扩展的菜单文字由 DLL 运行时生成（注册表搜不到），用 `LoadLibraryEx(LOAD_LIBRARY_AS_DATAFILE)` 纯数据映射 DLL 遍历字符串资源做启发式“疑似菜单文字”分析（不执行任何 DLL 代码）。
- **提权是进程级操作**：由宿主 `MainViewModel.ElevateRestart()` 统一处理（`ElevationHelper.RestartElevated` 用 `Environment.ProcessPath` 重启当前 exe），工具内按钮仅转发到 `IToolHost.ElevateRestart()`。
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
- 开机启动项实测：本机扫描 18 项（注册表 16 / 残留 4 / 已禁用 5 —— 任务管理器禁用过的项状态正确互通）；desktop.ini 自动排除。
- 操作日志卡片实测：点击进入 → 统计/级别筛选/来源筛选/清空均正常，进入工具本身产生日志并实时归集。
- 卡片背景实测：⚙ Flyout 色板即时变色、取色器自定义色、本地图片背景均正常；重启后配置完整恢复；主题循环切换（跟随系统/浅色/深色）即时生效。
- RouteDump 输出与本机 `route print` 一致（结构体 ABI 自检通过）。
