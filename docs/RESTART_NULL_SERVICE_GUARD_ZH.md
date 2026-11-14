# 重开空网络服务保护候选（restart-null-service-guard-v1）

状态：2026-11-14 23:46，用户确认桌面房主重开正常，随后确认 Android 客机自动重连正常。日志确认命中空服务保护、续局房间重新发布成功。当前测试组合的重开闭环已通过；其他协议/平台组合未据此视为通过。临时诊断探针已从源码移除，保留常规启动日志与空服务保护，尚未正式发布。

## 实机确认

此次 `restart-20261114T154641158-03acbbe5b5d14d4bb68f24aed270bc8a.jsonl` 中：

- 序号 121：`sync_return`。
- 序号 122：`null_service_loop_stopped`，目标为 `NMultiplayerNetworkProblemIndicator.UpdateLoop`。
- 序号 177：正常 `end`。
- 游戏日志记录 `continue_run_publish: publish succeeded screen=NMultiplayerLoadGameScreen`。

这说明保护确实拦截了此前的空服务访问，而不只是一次未触发问题的成功尝试。用户确认重开不再崩溃，房间恢复也有日志证据；不据此扩大为全平台、所有协议及完整联机流程验收。

## 依据

- v1 日志中，续局页面初始化、进阶 0/10 刷新、`stack.Push` 都返回；v2 又确认成功提示弹窗创建、`_Ready`、添加及焦点回调全部返回。故障发生在同步调用返回后。
- 游戏 `NMultiplayerNetworkProblemIndicator.UpdateLoop` 每隔 2 秒恢复，循环条件先读取 `RunManager.Instance.NetService.IsConnected`，再检查节点有效性。
- mod 的重开清理会把旧 `RunManager.NetService` 置空。这是原有 RitsuLib 防旧会话错绑逻辑，不是此次 0.113.0 适配新增。
- [2026-09-12 实测记录](ALPHA1_RESUME_QA_20260912.md) 已记载同一清理与网络指示器异步任务之间的空引用竞争。

## 修改范围

新增 `LanConnectRestartLifecyclePatches`：在 `UpdateLoop` 的编译器生成异步状态机 `MoveNext` 中，把唯一的 `INetGameService.IsConnected` 读取替换为安全读取。

- 服务为空：返回 false，使旧循环结束。
- 服务非空：读取原有 `IsConnected`，保持其他逻辑不变。
- 保留指令标签和异常处理区；匹配不到恰好一处读取时明确报告补丁失败。
- 保护安装在兼容性补丁组中，不依赖房间协议，也不因 RMP 存在而跳过。
- 不恢复旧会话、不修改 RitsuLib、不改变协议、存档内容、房间模式或重开顺序。
- 排障版曾在命中保护时记录 `null_service_loop_stopped`，实测命中证据已留存；干净版移除了该临时写入和全部 v1/v2 探针，空服务保护本身不变。

## 验证

真实 0.113.0 游戏程序集上的前后对照：

1. 将测试进程的 `RunManager.NetService` 设为空。
2. 原游戏循环返回 faulted Task，异常为 `NullReferenceException`。
3. 安装保护后，同一游戏循环正常完成，且全局网络服务仍为空。
4. 连接正常/断开时的布尔值不变；IL 只替换一处调用，标签、异常处理区和其他指令不变。

结果：4 项定向测试通过；包含它们的核心 Godot 回归 70 项通过；客户端单测串行 1287 项通过、1 项既有跳过。

自动验证使用本机 Godot 4.5.1 / .NET 9.0.22；用户游戏使用 .NET 9.0.7。已验证明确的空引用路径和修复效果，**尚不能仅凭这些测试断言用户的原生 SIGSEGV 已完全消除，也不能断言 Android 的差异原因**。

现有 ENet 断开期间的 `Peer not connected` / 空 peer 日志不在此补丁范围内。

## 清理临时诊断后的构建

临时 writer、Harmony 探针、源码检查点、诊断启动标记和诊断专用测试已删除，原有启动诊断与保护补丁的常规安装日志保留。新测试确认程序集不再含临时 trace 类型，同时仍含保护类。

- Godot 核心回归：66 项通过；移除 4 项临时探针测试，实际功能测试保留。
- 客户端单测串行：1286 项通过、1 项既有跳过；移除 2 项临时 writer 测试，新增 1 项干净程序集检查。
- 协议计划测试：11 项通过；1243 个 mod 类型在游戏依赖下加载通过。
- 干净 DLL SHA-256：`f168a7a161f8ea21504ab2c9e39574a055c13d7ff96ccb4d922f399af97a8596`。
- 干净 ZIP SHA-256：`04e13d2846fe8a789350c88d9a1d0025cd7977db9aa42fc977158e9e2212c5f6`。
- 用户退出游戏后，已于 2026-11-15 00:03 安装干净 DLL 并逐字节校验。此前带诊断的版本备份在游戏目录 `mod-backups/sts2-beta113-clean-20261115-000346/sts2_lan_connect/`；配置、manifest、PCK 和许可文件与备份对比一致，存档未改动，旧诊断证据未删除。

## 已安装的候选（清理前）

已在确认游戏退出后备份并替换本地 DLL，未改动配置、资源和存档，未启动游戏。

- 备份：游戏目录 `mod-backups/sts2-restart-null-guard-v1-20261114-233127/sts2_lan_connect/`。
- DLL SHA-256：`aeb268e1b4fcee70194154f3931604535783803487e9cb5aebedbbdfabc04af4`。
- ZIP SHA-256：`82a23b1fe9c9fbec21dea98ff3135d3221a3a4cce7aae0942118d39b504787ef`。

## 实机复验

保持刚才的游戏版本、协议、MOD 组合不变。仅更新桌面房主即可，Android 不需要为这一处本地延迟循环保护更新。重开一次后确认：是否还崩溃、房间是否恢复、客机能否重新加入。仍有问题时保留普通游戏日志；干净版不再生成重开 trace JSONL，旧诊断文件仅作为历史证据保留。
