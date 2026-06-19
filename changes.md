# CuSaveManager 更新日志

> English changelog: see [changes.en.md](changes.en.md)

## 1.1.5

本 mod 自身固定世界引擎（self）确定化补强：补全协程内全部同步执行点的重置。

- 背景：协程世界生成阶段（`IEnumerator`，如 `WorldGenerateTerrain`/`WorldPlaceEntities`/`WorldGenerateStructures`）的 Harmony Prefix 只在协程对象创建时（首个 `yield` 之前）执行一次，真正抽随机发生在 `yield` 之后的帧里，期间 `UnityEngine.Random` 可能被其它系统打乱 → 仅阶段级重置不可靠，地形/实体可能无法按种子复现。
- 新增对协程内**同步执行**子方法的逐一即时重置：`FastNoiseLite` 构造函数（地形/洞穴噪声，按 `_noiseGenStep` 递增）、`DistributeEntities`（敌人/陷阱/箱子/尸体等分布，按名字 hash+参数+调用序号）、`PlaceLiquids`、`GenerateLifePods`/`GenerateDropCapsules`/`GenerateCollapsedPods`、`ApplyLayerModifiers`（与读档还原补丁共存）。
- 进层/重生成/清理时复位计数器与种子：`ContinueRun`、`RegenerateWorld`、`Clear`，并在 `GenerateWorld`/`WorldGenerateTerrain` 阶段开头复位噪声计数。
- 战利品确定化：`TraderScript.GenerateInventory`、`Openable.OnUse`（前后保存/还原 `Random.state` 避免污染全局流）。
- 存档/读档一致路径（原生存档 + 各还原补丁）不变。

## 1.1.4

仅装 KrokMP 联机 mod（未装 QoL）时，与 KrokMP 同开会出现三个问题，本版修复：

- **地图模式恒为默认**：本 mod 接管 `PreRunScript.StartRun` 后丢掉了 KrokMP 原补丁里的 `WorldGeneration.runSettings = 所选预设` 写入，导致主机开房时荒凉/无芯片/自定义等地图模式被 `WorldGeneration.Awake` 回落到 normal 预设。已在接管逻辑里补回该写入。
- **每次重开都是同一种子（地图与上一次相同）**：默认 `PreferredEngine=qol`，未装 QoL 时旧逻辑回落到本 mod 的「确定化世界」引擎并强制注入固定种子；且 `EnsureFreshSeed` 在已激活时直接返回，跨局复用上一局种子。现默认行为改为：KrokMP 在场即交还其原生世界生成（随机地图）；本 mod 固定世界引擎改为显式 opt-in（面板里手动选「本 mod」）。`EnsureFreshSeed` 每次新开局重新取种子。
- **无法进入下一层（卡在当前层）**：确定化引擎在进层时按存档里旧的 `totalTraveled` 复算种子，重新生成与所在层相同的世界，表现为“走到底端选下一层仍是同一层”。随上一条交还 KrokMP 原生进层后修复。

## 1.1.2

- 主面板侧栏新增「关于」分页：与 SkinSync 同款居中标题 + 链接按钮 + 名字按钮风格，列出版本号 / 仓库 / 最新发布 / 作者 / 依赖。
- 设置 →「其他」区段加「界面语言」三档切换（自动 / 中文 / English）；写入配置文件 `I18n.PreferredLanguage`，重启游戏后保留。语言判定升级为三层兜底（配置 → 游戏 Locale 关键词识别 → PlayerPrefs）。
- 与其他嵌入侧栏的 mod 统一半透明底色（HwAssistive 之前底色明显偏深，本轮对齐到 saveManager 标准），切换侧栏分页时主区底色不再跳变。

## 1.1.1

- 修复读档/回档时游戏在第 1 层重新派发起始物资（应急灯或灯笼+狗粮+水瓶+垃圾袋）的问题；游戏 `WorldGeneration.WorldPlacePlayer` 自身没有读档守卫，每次回档第 1 层都会按 `runSettings["startingsupplies"]` 重发。新增「读档时不重发起始物资」开关（设置 → 其他，默认开启），关闭后回到游戏原版行为。

## 1.1.0

- 主面板按屏幕分辨率整体等比缩放，超大设计尺寸不再超出屏幕；保留边距、可拖动，命中检测随缩放校正。
- 读档 / 回档时从存档恢复游戏难度（RunSettings），修复单机与多人重新读档后自定义难度变默认的问题。
- 修复面板缩放后右上角 X 关闭图标显示异常。

## 1.0.9

- 修复 `ImGuiImeRecovery` 在面板关闭后仍因外部 IMGUI（如 KrokMP 多人连接页）有焦点就清 `FocusControl`，导致 IP/用户名字段无法输入的问题；现仅在面板关闭时 `RequestClear()` 才回收焦点。

## 1.0.8

- 修复在面板文本框（昵称、种子等）用过中文输入法之后，本模组快捷键和快捷栏按键都不响应的问题。关面板后会清掉 IMGUI 残留的键盘焦点。

## 1.0.7

围绕"固定世界 + 回档"这条主线做了一轮系统性整理：

- **固定世界引擎二选一**：面板可选「QoL（优先）」或「本 mod」。选 QoL 时把世界确定化交给 QoL；选本 mod 时由自带引擎接管并暂时禁用 QoL 的世界介入。未安装 QoL 时「QoL」按钮禁用，只能用本 mod 引擎。
- **多人支持**：多人存档按 KrokMP 的 `mp_save` 目录整体打包为槽位；回档由主机两阶段重载（返回主菜单 → 自动重新加载），全员重连进入备份点。
- **统一日志**：所有日志收敛到统一出口，可在设置里开启「在游戏控制台显示模组日志」，按 ` 键打开的控制台即可查看。
- **更新检测**：启动时比对 GitHub 最新版本，有新版在屏幕左上角红字提示；可在设置里关闭。
