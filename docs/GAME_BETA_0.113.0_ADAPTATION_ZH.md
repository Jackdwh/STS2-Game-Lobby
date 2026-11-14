# 游戏 0.113.0 beta 适配与验证记录

日期：2026-11-14。状态：**本地测试候选，尚未发布。用户已确认兼容协议创建/加入正常，并确认桌面房主重开恢复正常；日志验证命中 [空网络服务保护](RESTART_NULL_SERVICE_GUARD_ZH.md) 且续局房间重新发布成功。临时诊断源码已清理，[历史诊断记录](RESTART_TRACE_V1_ZH.md)保留；用户随后确认本次重开后的 Android 客机自动重连正常，其他测试组合仍待确认。**

## 目标与范围

- 游戏：Linux x86_64 `v0.113.0`，`release_info.json` commit `16ac0fe3`。
- 游戏 `sts2.dll` SHA-256：`7bd4a0fc298a82b3657ef9e638754e68cfc0c6d8beacb86f5404167a0ae4b3a4`。
- 基于客户端 0.6.3；本轮不变更正式版本号、不发布 GitHub/Steam、不修改 lobby-service。
- 保留 `compat_4_5_v1` / `tail_v1` 的现有选择与存档协议绑定，不放宽游戏版本或内容指纹门禁。不同游戏版本仍不能因此互通。

## 适配内容

1. **游戏模式与加入流程**：新版 `InitialGameInfoMessage` 不再携带 `gameMode`，从加入响应或续局存档读取；保留旧字段回退。自定义模式改用独立 `isCustom`，本地 UI 的自定义选择不会作为游戏枚举写入网络；新旧自定义加入界面分别正确路由，支持新版 Blitz/Dread 加入及载入界面。
2. **进阶等级**：加入请求改为分别携带 `maxStandardAscension` / `maxBlitzAscension`，保留旧单字段适配。
3. **兼容协议消息**：手写 join-response 序列化在 `ascension` 前写入新版 `gameMode` / `isCustom`，避免后续位流整体错位。旧版无这两个字段时不写；字段不完整时拒绝不明确的布局。
4. **多人补丁**：解析新旧难度模型命名空间，按 `Players.Count` 而非旧私有字段匹配 IL；支持加入 `isCustom` 参数的新大厅构造函数；断开连接通过兼容入口支持新增的可选 `debugReason` 参数。
5. **宝箱**：扩展新版按遗物数量选择的布局表，保留原游戏的索引、投票、导航初始化。旧投票路径的结果构造兼容新增 `relicIndex`；新版原生 nullable skip 继续走原生逻辑。
6. **存档**：新版优先使用明确的 `IsCustomGame` / `GameMode`，避免空修改器的自定义存档被误认成标准模式，续局发布同样保留自定义属性。

## 已执行的验证

| 检查 | 结果 |
| --- | --- |
| 客户端及 GdUnit 测试工程，引用 0.113.0 游戏 DLL 编译 | 通过；仍有已有 nullable / source-generator 警告 |
| `GameAbiLoadCheck`，新游戏依赖中的 `Assembly.GetTypes()` | 1240 个类型加载通过（不等同于所有方法均执行） |
| `sts2-lan-connect.Tests` 完整单测 | 1285 通过，1 个既有用例跳过，0 失败 |
| `sts2-lan-connect.ProtocolPlanTests` | 11 通过，0 失败 |
| Godot 核心运行时回归 | 62 通过，0 失败（包含新增 4 项按钮路径/缺失按钮用例） |
| 扩大到 LAN 模式选择器、建房弹窗的 Godot 检查 | 68 通过，2 项尺寸断言失败，详见下文 |
| 既有脚本的 PCK 构建及客户端打包契约 | 通过；包清单、法律文件与确定性输出检查通过 |
| `git diff --check` | 通过 |

核心 Godot 回归包括新版接口探测、真实 Harmony 游戏补丁安装、消息布局对照、8 人 compat 消息总线边界、tail 运行时及 Native Bus 检查、存档自定义标志，以及最多 16 个宝箱布局的构建。它们使用真实游戏程序集与 Godot 引擎，但不代替两台设备实际开局、战斗和读档。

原 join-response 反向测试用 4 人判断 3/5 bit 位宽，存在低三位相同的别名问题；本轮改为 8 人，超出 3 bit 范围，同时继续校验所有槽位及后续字段。

### 首次实机反馈与建房菜单修正

21:47:58，本机首次候选在点击大厅“创建房间”后出现 SIGSEGV。归档日志确认加载的是候选 DLL `82c2776d…`，服务端 `/health` 已返回 200，尚未进入发布房间步骤。最后一条具体错误是 `Node not found: "StandardButton"`，调用链为 `HostSubmenuPatches.GetModeAvailability` → 创建弹窗。

新版游戏通过 `%StandardButton` / `%DailyButton` / `%CustomRunButton` 定位嵌套按钮，旧代码错误地假设它们是直接子节点。现统一改为 `GetNodeOrNull` 的唯一名称、旧路径及递归回退；按钮未就绪时禁用相应模式，不报节点查找错误，也不默认解锁。LAN 按钮模板和诊断输出复用同一解析器。

修正版新增旧布局、嵌套布局、唯一名称布局、缺失按钮四项 Godot 回归，62 项核心运行时与 1285 项单测通过，并重新安装。**这验证了已发现的路径错误得到修正，不等同于 SIGSEGV 的全部根因已经证明或崩溃已实机消除。** 他人反馈创意工坊版可建房但客机加入崩溃，属于不同阶段，仍需分别验证。

### 未通过及未覆盖

- `LanConnectLanHostModePickerTests.Picker_has_three_large_stable_mode_choices`：headless 环境测得面板宽度 221，断言要求至少 700。
- `LanConnectCreateProtocolDialogTests.Create_dialog_wraps_its_content_instead_of_filling_the_viewport`：面板与内容高度差 102，断言上限 100。
- 两项布局失败没有通过放宽断言掩盖，也未扩大本轮改动到无关的 UI 尺寸调整；尚需游戏内视觉检查。
- `LanConnectFullMessageGoldenVectorRuntimeTests` 的固定字节样本属于游戏 0.111.0，未将其作为 0.113.0 回归执行，也没有用新版字节覆盖旧样本；重生成入口增加了版本布局保护。新版消息镜像检查与 compat/tail 运行时回归单独执行。
- 没有旧 beta / release 程序集做双版本验证。保留旧 API 分支不代表旧版本运行已获验证；需要完整旧版 `data_sts2_*` 目录和 `release_info.json` 后再确认。
- 未验证 Windows/macOS/Android 实机、RitsuLib 共存、双端联机、首怪、宝箱实际领奖、保存退出续局及断线重连。

## 构建与复现

使用 .NET SDK 9.0.320、Godot 4.5.1 .NET。Linux 构建必须显式指定 `Sts2DataDir`，当前项目的缺省非 Windows 路径仍偏向 macOS，不应把该缺省路径用于 Linux。

```bash
export Sts2DataDir='/mnt/d/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64'
```

```bash
export STS2_TEST_GAME_DATA_DIR="$Sts2DataDir"
```

将 .NET 加入 PATH，并把 `DOTNET_BIN` / `GODOT_BIN` 指向本机实际安装后：

```bash
dotnet test sts2-lan-connect.Tests/sts2_lan_connect.Tests.csproj
```

```bash
dotnet test sts2-lan-connect.ProtocolPlanTests/sts2_lan_connect.ProtocolPlanTests.csproj
```

GdUnit 的现有 `gdunit4.runsettings` 写死了开发者 macOS Godot 路径；本次使用独立临时 runsettings 替换 `GODOT_BIN` 并设置 `--headless`，没有改变全局环境。本机 Arch Linux 下 Harmony 辅助库出现 `_Unwind_RaiseException` 未解析，通过**仅对测试进程**设置 `LD_PRELOAD=/usr/lib/libgcc_s.so.1` 后正常运行；其他系统不要盲目复制该绝对路径。

运行时过滤器：

```text
FullyQualifiedName~LanConnectBeta113RuntimeTests|FullyQualifiedName~LanConnectCompatBoundaryRuntimeTests|FullyQualifiedName~LanConnectTailMessageRuntimeTests|FullyQualifiedName~LanConnectProtocolPatchDispatcherTests|FullyQualifiedName~NativeBus
```

打包使用 `scripts/build-sts2-lan-connect.sh`、`scripts/package-sts2-lan-connect.sh --skip-build --output-dir <仓库外独立目录>`；未手工修改 `releases/`。

## 测试包身份

内部 manifest / DLL 版本号仍为 **0.6.3**，不是新的正式 0.6.3 发布。请不要仅凭版本字符串判断是否为本次候选；同房双方应使用同一测试包并完整重启游戏。

- 当前建房菜单修正版 ZIP SHA-256：`034438447eaef0a9e336714b948e65d14129cccea7ec56334b3c4fd876ca3232`
- 当前 mod DLL SHA-256：`bedbd6f2552cd167be83af66677608c67b22c3dc713e52d76d24a48e9afe0b37`
- 首次候选（已被替代，存在建房菜单路径错误）：ZIP `c86a49b0d3e1e3bca14e69a786b931feeedf76379f892f66c4f6c096948eda9c`，DLL `82c2776dc6d4b79fc41520c818ebba4e1c6c8676c452d3df6c80e8465b7d3c09`。

包内既有 README/用户指南仍是 0.6.3 正式版文档，**本记录才是 0.113.0 候选的验证边界**。测试前备份现有 mod、游戏存档与联机配置，避免创意工坊和手工 mods 目录重复加载。

建议实机验收顺序：两端同为 0.113.0 → 两种协议各自创建/加入标准及自定义房间 → 开局和首怪 → 宝箱投票/跳过/领奖（特别是超过 4 人） → 保存退出后按原协议续局 → 断线重连。每日挑战及新版模式再单独检查；有失败时保留双方启动和联机日志。
