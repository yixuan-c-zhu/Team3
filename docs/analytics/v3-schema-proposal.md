# Analytics v3 方案

状态：**已实施，待 Unity 内验证和规则发布**（2026-11-06）。Firebase 数据和线上规则均未改动。
基线：`beta-chen-progress`（提交 `6ce345b`），改动直接提交在 `beta-chen-progress`。

## 1. 目标

Beta 是本学期唯一一次大规模数据收集。v3 只解决三件事：

1. **看得懂**：在 Firebase Console 里按「版本 → 玩家 → 游玩 → 事件」逐层点开，不借助 AI 也能读。
2. **可追溯**：每条事件带服务器时间；每个版本自动落在独立目录，不会再与旧数据混在一起。
3. **按检查点记录 Tutorial**：直接回答「玩家能否通过 Tutorial、卡在哪一段」。

明确**不做**（PM 2026-11-06 决定）：build_commit、进入方式（entry）、离开事件、玩家坐标、无 cut 时按 C、失焦时间等；LEVEL 01、LEVEL 02 不加检查点，按整关分析。

## 1.1 关卡与段的命名（PM 2026-11-06 确认）

数据里的名字与主菜单一致，不再使用场景名或 LevelCatalog 的存档 ID，避免 Tutorial 段 ID "Level1/Level2" 与菜单 "LEVEL 01/02" 混淆。

| 菜单 | Unity 场景 | `level` | `level_title` | 段（`segment` · `segment_name`） |
|---|---|---|---|---|
| Tutorial | Tutorial | `tutorial` | Tutorial | 1 · Cut to fit；2 · Leave a little behind；3 · First bridge；4 · Building Bridge |
| LEVEL 01 | NewLevel | `level01` | New Level | 1 · New Level |
| LEVEL 02 | GlassParadox | `level02` | Glass Paradox | 1 · Glass Paradox |

- `segment_name` 取 LevelCatalog 中玩家看到的 Lesson 标题。
- LevelCatalog 的 `Id`（Level1、Level2、FirstBridge、BuildingBridge）是通关存档的 key，**保持不变**，只是不再写进数据。
- 不在 LevelCatalog 中的场景（DoubleJump、PushingButton 等，菜单无法进入）回退为小写场景名，分析时排除。
- 原始场景名另存于 `scene` 字段，便于排查。

## 2. Firebase 结构

```
/events/...                                  Alpha 存档，原样保留，v3 不再写入
/v3/<build>/<player_key>/<session_key>/<event_key>
```

| 层级 | 格式 | 示例 | 说明 |
|---|---|---|---|
| build | `^[A-Za-z0-9_-]{1,64}$` | `beta1-all-metrics` | 设置资源里的 Build Id |
| player_key | `p_` + 8 位十六进制 | `p_7f3a9c21` | 匿名玩家 ID 的前 8 位 |
| session_key | `yyyyMMdd-HHmm-` + 8 位十六进制 | `20261110-1432-a1b2c3d4` | 玩家本地时间（仅用于排序和查找）+ session_id 前 8 位 |
| event_key | 4 位序号 + `_` + 事件名 | `0005_checkpoint_reached` | 序号即 `sequence`，Console 中按顺序排列 |

### 匿名玩家 ID

- 第一次运行时生成 32 位随机十六进制，存入 `PlayerPrefs`（WebGL 下保存在浏览器本地）。
- 不含姓名、邮箱、账号等任何个人信息，只用于把同一个人的多次游玩归在一起，**不用于 DAU/MAU**。
- 局限：换电脑、换浏览器、无痕窗口、清缓存会变成新玩家；多人共用同一浏览器会被算作一人。
- Editor 中使用单独的键，避免开发者的测试与玩家 ID 混用（Editor 数据另有 `is_editor=true`）。

### 上传方式

- 由「POST 生成随机 key」改为「PUT 到固定 key」。
- 规则允许：节点不存在时写入；或节点已存在且 `event_id` 相同（同一条事件的重试）。因此网络重试不会产生重复记录，分析时也不必再按 event_id 去重（脚本仍保留去重作为保险）。
- `server_time` 写入 `{".sv": "timestamp"}`，由 Firebase 服务器填入毫秒时间戳；规则校验 `== now`。
- 由于 `JsonUtility` 不能输出该对象，在序列化后的 JSON 中插入此字段。

## 3. 事件字段（schema_version = 3）

| 字段 | 类型 | 说明 | v2 对应 |
|---|---|---|---|
| `schema_version` | 3 | | 2 |
| `event` | string | 事件名，见第 4 节 | event |
| `event_id` | 32 hex | | 同 |
| `player_id` | 32 hex | 匿名玩家 ID | **新增** |
| `session_id` | 32 hex | 每次打开页面一个 | 同 |
| `sequence` | int ≥1 | session 内顺序 | 同 |
| `build` | string | | 同 |
| `server_time` | ms | 服务器写入时间 | **新增** |
| `is_editor` | bool | | 同 |
| `level` | string | `tutorial` / `level01` / `level02`（见 1.1） | scene（重命名） |
| `level_title` | string | 菜单上的关卡名 | **新增** |
| `scene` | string | Unity 场景名，仅用于排查 | 同 |
| `segment` | int ≥1 | 第几段；单段关卡固定为 1 | room_index + 1 |
| `segment_count` | int ≥1 | 本关共几段（Tutorial = 4） | **新增** |
| `segment_name` | string | 玩家看到的段标题（见 1.1）；查不到时为 `segment_<n>` | room（改为标题） |
| `visit_id` | 32 hex | 一次段游玩，重开时不变 | room_visit_id |
| `attempt` | int ≥1 | 本段第几次尝试 | room_attempt |
| `t_attempt` | s | 本次尝试开始后的时间 | t_room |
| `t_segment` | s | 本段累计时间（含重开） | t_visit |
| `t_level` | s | 本关累计时间（从 level_start 起，含重开） | **新增** |
| `level_restarts` | int ≥0 | 本关到目前为止的重开次数 | **新增** |
| `cuts_left` / `cuts_budget` | int | | 同 |
| `kept_ratio` | 0–1 或 -1 | 仅 cut_success 有效 | value（cut_success） |
| `reason` | string | 按键或重开原因 | 同 |
| `first_use` | bool | 本段这次游玩中首次使用该机制 | 同 |

去掉 v2 的通用 `value` 字段，改用含义明确的 `kept_ratio`、`cuts_left`。
时间仍包含暂停、帮助面板和窗口失焦时间（与 v2 相同）。

## 4. 事件（14 种）

| 事件 | 何时记录 | 替代 v2 |
|---|---|---|
| `level_start` | 进入一个关卡场景（从菜单、下一关、通关后 Retry）；**重开重新加载不记** | 新增 |
| `segment_start` | 开始一段：关卡开始、过检查点后、重开后 | room_enter |
| `checkpoint_reached` | 到达第 n 段出口且不是最后一段（Tutorial 的 1/2/3）；`segment` = n | room_clear（非最后一段） |
| `level_complete` | 到达最后一段出口（Tutorial 第 4 段、单段关卡的出口） | room_clear（最后一段） |
| `restart` | 通关前重开，`reason` = `manual` / `fall` | 不变 |
| `cut_success` / `cut_miss` / `cut_too_small` / `cut_cancel` | 与 v2 相同 | 不变 |
| `use_cut` / `use_view_switch` / `use_color_shift` / `use_switch_piece` / `use_camera` | 与 v2 相同 | 不变 |

一次完整的 Tutorial：`level_start` → (`segment_start` → … → `checkpoint_reached`) ×3 → `segment_start` → … → `level_complete`。
从菜单直接选第 3 段开始：`level_start` 的 `segment` = 3，数据里可以直接看出来。

## 5. 代码改动范围（确认后实施）

| 文件 | 改动 | 影响的事件 |
|---|---|---|
| `Analytics/AnalyticsSession.cs` | 新字段；level 状态（t_level、level_restarts、level_start 判定）；事件改名 | 全部事件的字段 |
| `Analytics/CutAnalytics.cs` | PUT 到 v3 路径；插入 server_time；玩家 ID | 全部（上传方式） |
| `UI/LevelCatalog.cs` | Chapter 增加分析用关卡 ID（tutorial / level01 / level02）；不改 Lesson.Id | — |
| `Analytics/FirebaseAnalyticsSettings.cs` | 生成 v3 路径；build 字符校验 | — |
| `CheckpointManager.cs` | 从 LevelCatalog 取 level、level_title、段标题后传入；段号 +1、segment_count；区分 checkpoint_reached / level_complete | segment_start、checkpoint_reached、level_complete |
| `LevelManager.cs` | 单段关卡（level01、level02）从 LevelCatalog 取命名；level_complete；restart 计入 level_restarts | level_complete、restart |
| `Resources/Team3AnalyticsSettings.asset` | buildId → `beta1-all-metrics` | — |
| `Tests/Editor/Analytics/*` | 更新并补充测试 | — |

`cut_*`、`use_*`、`restart` 的触发条件不变。

## 6. 规则与发布顺序

1. 在 `firebase-database.rules.json` 中**新增** `v3` 分支（逐字段校验，`$other` 拒绝），`events` 分支保持不变。
2. **PM 在 Console 发布规则**，并用 Rules playground 模拟一次写入。
3. 用 `beta1-smoke` 打包冒烟测试（会写入 Firebase，需 PM 批准），确认 Console 显示正常。
4. 改回 `beta1-all-metrics`，正式发布 Beta。

顺序不能反：规则不允许未登记的字段，新版游戏先上线会被全部拒收。

## 7. 分析输出（出图脚本）

- 读取 v3 嵌套结构；v2（Alpha）通过字段映射转换，日期从 push key 还原。
- v2 关卡映射：Tutorial → `tutorial`，NewLevel → `level01`，GlassParadox → `level02`。
- **Tutorial 通关漏斗**（按玩家和按 session 两种口径）：开始 → 检查点 1 → 2 → 3 → 通关。
- **每段卡关表**：重开（manual / fall）、t_segment 中位数、到达检查点时剩余 cut、切割失败次数，标出最难的一段。
- **关卡层面汇总**（三个关卡）：开始、通关、t_level、level_restarts。
- **每段机制使用**：本段要教的机制是否被用到。
- **每位玩家一行**：最远到达的检查点、是否通关、总耗时、重开次数。
- **Alpha vs Beta**：关卡层面直接对比（注明 Tutorial 内容已改）；按段对比使用下表。

### Alpha → Beta 段映射（待确认）

| Alpha 段 | Beta 段 | 状态 |
|---|---|---|
| 1 Cut to fit（ID Level1） | 1 Cut to fit | 标题相同，内容已改 |
| 2 Leave a little behind（ID Level2） | 2 Leave a little behind | 标题相同，内容已改 |
| 3 A different angle（ID Level3） | — | Alpha 独有，单独列出 |
| 4 Shift your color（ID ColorShift） | — | Alpha 独有，单独列出 |
| — | 3 First bridge | Beta 新增，单独列出 |
| 5 Building Bridge | 4 Building Bridge | **待确认内容是否相同** |

LEVEL 01、LEVEL 02 均为单段，关卡层面直接对比。

## 8. 决定记录

| 日期 | 决定 |
|---|---|
| 2026-11-06 | 不加 build_commit、entry 等附加字段 |
| 2026-11-06 | 采用「版本 → 玩家 → 游玩」分层（方案 B） |
| 2026-11-06 | 关卡命名 tutorial / level01 / level02，段名用玩家看到的标题 |
| 2026-11-06 | LEVEL 01、02 不加检查点 |
| 2026-11-06 | PM 确认方案，开始实施；改动直接提交在 `beta-chen-progress`，不另开分支 |

## 9. 待确认

1. BuildingBridge 在 Alpha 与 Beta 中内容是否相同（决定能否按段对比）。
