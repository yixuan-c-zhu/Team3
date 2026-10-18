# Alpha 数据分析：四项指标说明

代码基线：`feature/alpha_new_level2`，提交 `4725a89`。
当前开发分支：`feature/analytics-firebase`。Unity 版本：**6000.3.22f1**。

四项指标均按 **Alpha** 阶段实现，已完成数据采集、房间流程接入、Firebase 上传和本地验证。
WebGL／浏览器验收、真实玩家测试和图表制作仍需单独完成。Editor 验证数据不作为玩家测试结果。

## Firebase 配置与开关

项目 ID：`team3-game-37d24`。
数据库地址：`https://team3-game-37d24-default-rtdb.firebaseio.com/`。

1. 创建 Firebase 项目，并以锁定模式创建 Realtime Database。
   Google Analytics 可选；当前采集器使用 Realtime Database REST API。
2. 在 **Realtime Database > Rules** 中发布完整的
   [firebase-database.rules.json](firebase-database.rules.json)。
   只修改仓库里的文件不会自动发布规则。项目所有者已于 2026-10-18 确认发布第 2 版规则。
3. 在 Unity 中打开 **Tools > Analytics > Firebase Settings**。
   配置资源中已填入公开的数据库地址。每个玩家测试版本应设置不同的 **Build Id**；
   当前默认值为 `alpha2-all-metrics`。
4. **Collection Enabled** 是整个采集功能的总开关。关闭后不再记录新事件。
   需要采集玩家测试数据时，应保持开启。
5. **Upload In Editor** 默认关闭。此时在 Editor 中点击 Play，
   如果 **Log Events In Editor** 开启，Console 仍会打印本地 JSON，但不会上传。
   上传模式和 Build Id 在每次 Play 开始时确定，修改后需要重新开始 Play。
6. 打包后的游戏（包括 WebGL）在采集开启且地址有效时会上传数据，
   并标记 `is_editor=false`。**Upload In Editor** 只控制 Editor，
   不会关闭打包后游戏的上传功能。

无需安装 Firebase Unity SDK，也无需服务账号或管理员密钥。
配置中仅包含公开的项目标识。规则允许在 `/events` 下匿名创建符合格式要求的事件，
即**只能新增**；客户端读取、修改、删除以及向其他路径写入都会被拒绝。
规则仍兼容旧版 `schema_version=1` 的切割事件；第 2 版要求提供新增的房间字段。

规则能检查数据格式，但不能证明数据一定来自真实玩家：知道地址的人可以提交符合格式的事件。
当前方案用于范围有限的课堂玩家测试，采集结束后，项目所有者可以恢复锁定规则。
Firebase Console 的访问权限仍由项目账号权限控制。

## 事件与计数方式

| 指标 | 事件名 | 触发条件与 value |
| --- | --- | --- |
| 1：切割结果 | `cut_success` | 玩家确认保留预览中的一块；value = 保留体积 / 切割前总体积 |
| 1 | `cut_miss` | 提交的切割平面未能将物体分成两块；value 为 -1 |
| 1 | `cut_too_small` | 切出的任意一块低于最小尺寸要求；value 为 -1 |
| 1 | `cut_cancel` | 玩家主动提前取消，或取消尚未确认的预览；value 为 -1 |
| 2：关卡进度 | `room_enter` | 实际进入房间，包括重开后进入；value 为 -1 |
| 2 | `room_clear` | 当前这次房间游玩首次通关；value 为剩余切割次数 |
| 3：机制使用 | `use_cut` | 按 C 成功进入切割模式；value 为 1 |
| 3 | `use_view_switch` | 玩家按 Tab 切换视角模式；value 为 1 |
| 3 | `use_color_shift` | 玩家按 Shift 改变颜色；value 为 1 |
| 3 | `use_switch_piece` | 按 F 成功与剩余块交换；value 为 1 |
| 3 | `use_camera` | 玩家使用 Q/E 旋转视角；value 为 1 |
| 4：房间难度 | `restart` | 通关前重开；value 为 1，reason 为 `manual` 或 `fall` |

第 4 项还会使用 `room_clear` 事件中的 `t_visit`、`cuts_left` 和 `cuts_budget`。
四项指标共涉及 **12 种事件**，使用同一套上传流程。

一次有效拖动算一次切割尝试，该尝试的 ID 会成为 `event_id`。
长度不超过 12 像素的拖动，以及无法形成有效切割平面的操作，不计入尝试。
进入切割模式后，在提交有效切割线前主动退出，记一次提前取消。
已经记录为切空或过小的尝试，在退出切割模式时不会再记一次取消；
之后提交新的有效切割线，才算新的尝试。
仅显示预览不算切割成功。预览未确认时离开场景或关闭游戏，也不视为主动取消。

机制使用事件统计实际生效的操作：没有切割次数时按 C、没有剩余块时按 F，都不计入。
程序自动改变相机状态，例如 AimZone 调用 SwitchView，不计为玩家发现或使用机制。
在 2D 模式下，每次按下 Q/E 记一次；在 3D 模式下，持续按住同一方向只记一次，
直到松开或改变方向，不会每帧重复计数。鼠标旋转与缩放不属于计划中的 Q/E 指标。

按 R 和掉落会记录不同的重开原因。通关界面的 **Retry** 按钮会开始一次**新的房间游玩**，
不会计为通关前的重开。重复触发出口不会重复记录通关。
房间切换时，先记录当前房间的剩余切割次数，再补满下一房间的次数。

## 数据字段与时间口径

第 2 版每条事件包含以下 19 个字段：

| 字段 | 含义 |
| --- | --- |
| `schema_version` | 数据格式版本，当前为 2 |
| `event_id` | 随机生成的 32 位十六进制事件 ID；上传重试时保持不变 |
| `session_id` | 每次启动游戏、加载页面或开始 Editor Play 时生成的随机会话 ID |
| `sequence` | 同一会话内的事件顺序，从 1 开始递增 |
| `build` | 玩家测试版本标签 |
| `scene` | Unity 中的原始场景名称 |
| `room` | 稳定的房间 ID，对应关系见下表 |
| `event` | 事件类型 |
| `value` | 事件对应的数值，含义见事件表 |
| `t_room` | 本轮尝试开始后的真实经过时间，单位为秒 |
| `is_editor` | Editor Play 产生的数据为 true |
| `room_visit_id` | 一次房间游玩的 ID，通关前重开时保持不变 |
| `room_index` | 房间在场景中的索引，从 0 开始 |
| `room_attempt` | 当前尝试次数，从 1 开始，每次重开递增 |
| `t_visit` | 本次房间游玩的累计尝试时间，包括之前失败的尝试 |
| `cuts_left` | 事件发生时剩余的切割次数 |
| `cuts_budget` | 当前房间允许的切割次数 |
| `reason` | 重开原因或操作按键，例如 `key_c`、`key_tab`；不适用时为空字符串 |
| `first_use` | 仅当本次房间游玩中首次使用该机制时为 true |

切换场景或重开不会改变 `session_id`。会话 ID 不代表独立玩家身份；
采集器不记录姓名、邮箱或账号 ID。
从菜单重新进入房间，或在通关后再次游玩，会生成新的 `room_visit_id`。
尚未通关时重开，会保留房间游玩 ID 和各机制是否已使用的状态。

时间包含切割模式暂停、帮助面板停留，以及离开游戏窗口的时间，
因此不能当成持续操作键鼠的时间。`t_room` 在重开后归零；
`t_visit` 累计各轮尝试的用时，但不包含中间重新加载场景的时间。
这些时间字段是累计值，**不要把多条事件的时间直接相加**。
事件进入上传队列后，即使切换场景，其原始场景、房间和时间信息也不会改变。

## 教程接入与 checkpoint 来源

合并后的 Tutorial 场景及房间管理器来自 **Connie Zhu** 的
`origin/feature/merged-tutorials` 分支，提交为 `eeef2ea`。
这里只引入所需的教程文件和兼容的调用逻辑，保留了原有 Building Bridge 关卡及瞄准相机功能。

**没有使用 Shijie 的 checkpoint 复活实现。**
该实现来自 `origin/checkpoint` 分支，提交为 `180f160`，
作者为 EdwardBuyy / shijiewu@usc.edu；其中的 `Assets/Scripts/Checkpoint.cs` 未被引入。
当前名称相近的 `CheckpointManager.cs` 是 Connie 用于管理四个教程房间的控制器。

| 场景 | 房间 ID | 索引 | 教程内容（括号内为游戏中的英文标题） |
| --- | --- | --- | --- |
| Tutorial | Level1 | 0 | 切割以通过障碍（Cut to fit） |
| Tutorial | Level2 | 1 | 留下一块协助通关（Leave a little behind） |
| Tutorial | Level3 | 2 | 从另一个角度切割（A different angle） |
| Tutorial | ColorShift | 3 | 改变颜色（Shift your color） |
| 其他游戏场景 | 场景原名 | 0 | 每个场景按一个房间统计 |

菜单中的教程按钮会进入 Tutorial 对应的房间，原有通关进度 ID 保持不变。
`CheckpointManager.Start` 在场景初始化后记录首次进入；
`Reach` 在补满切割次数前记录当前房间通关，然后记录下一房间进入。
重开会重新加载当前房间。跳过的房间不会被补写不存在的进入或通关事件。

独立关卡通过 `LevelManager.Start`、`CompleteLevel` 和 `RestartLevel` 接入，
不依赖 checkpoint 组件。
Tutorial 和 NewLevel 已加入 Build Settings，支持重新加载场景。
**NewLevel 暂未加入 LevelCatalog 和关卡选择菜单**，目前可以在 Editor 中直接打开。
正式发布时的菜单接入仍属于团队的关卡／菜单任务。

Unity MCP 保留在本地并保持可用。本地 MCP 源码和启动脚本已排除在 Git 跟踪范围外。
提交 analytics 代码时，不应包含仅适用于本机的嵌入式 MCP 包锁文件修改。

## 上传机制

跨场景保留的 `[Analytics]` 对象将事件 JSON 上传到
`/events.json?print=silent`。
队列最多保存 256 条事件，同一时间只发送一个请求；
请求超时为 10 秒，每条事件最多发送 4 次。
网络错误、HTTP 408/429 和 5xx 会在逐渐延长并带有随机偏移的等待后重试；
其他错误不重试。
收到 HTTP 401/403 后，本次会话停止上传，需要修正规则后重新开始 Play。
上传失败不会阻塞游戏。

队列满时丢弃新事件，已有事件保留原顺序。可通过以下属性排查问题：
`RecordedCount`（已记录）、`UploadedCount`（已上传）、
`DroppedCount`（已丢弃）、`PendingCount`（待上传）、
`LastEventJson`（最近事件）和 `LastUploadError`（最近上传错误）。
仅在本地预览的事件不会被保存下来供以后补传。

如果 Firebase 已接收事件，但成功响应在途中丢失，POST 重试可能生成重复记录。
**导出后必须按 event_id 去重。**
上传采用尽力送达机制：关闭或重新加载游戏会丢失内存中的待传队列；
关闭 WebGL 标签页也不能保证所有数据上传完毕。

修改脚本前先停止 Play，编译后重新开始一次会话。
当前不支持在 Editor 热重载脚本后继续原来的 analytics 会话。

## 如何把数据转换成四类图表

在 Firebase Console 的 **Data** 页面导出 JSON，将 `events` 下的记录展开成表格行。
筛选目标 `build`，排除 `is_editor=true`，按 `event_id` 去重。
需要房间游玩字段的指标应使用第 2 版数据。
房间分组必须同时使用 **scene + room**。

1. **切割结果：**按房间统计四种切割结果，画堆叠数量图或比例图，并标明总尝试次数。
   成功率 = 成功次数 / 四种结果的总次数。
   保留体积比例分布只使用 `cut_success` 事件的 `value`。
2. **关卡进度：**按房间分别统计进入和通关的不同 `session_id` 数量，
   通关率 = 通关会话数 / 进入会话数。重开产生的进入事件不能重复增加进入人数。
   按 `sequence` 排序后，最后一条 `room_enter` 表示最后观测到的房间。
   有进入而没有通关记录，并不能证明玩家因为什么原因退出，也可能是上传丢失。
   菜单允许直接选关，因此进入后面房间的会话不一定进入过前面的房间。
3. **机制发现与使用：**以进入房间的各次游玩为基础，计算使用过每种机制的比例及使用次数。
   首次使用时间取 `first_use=true` 事件中的 `t_visit`。
   从未使用某机制的游玩记录，不能当作“0 秒发现”。
   报告中应明确并统一分母：是房间游玩次数，还是会话数。
4. **房间难度：**按房间游玩 ID 和原因统计重开次数，区分手动重开与掉落；
   计算平均值时也要包含零次重开的游玩记录。
   对已通关的游玩，绘制 `room_clear` 中的 `t_visit` 和 `cuts_left`；
   如果 `cuts_budget` 大于 0，也可以计算剩余次数占比。
   未通关的游玩应单独报告，其最后事件的时间只是实际停留时长的下限。

会话数不等于玩家人数，玩家人数应以实际招募记录为准。
正式图表需要真实玩家测试数据。
本次实现尚未生成自动制图／报告流程或玩家测试结果文档。

## 验证结果

测试入口：Unity **Test Runner > EditMode > Team3.Analytics.Tests**。

验证日期：2026-10-18。Unity 版本：6000.3.22f1。

- 编译和 Console 检查未发现错误。
- **44/44 项 EditMode 测试通过**，覆盖切割计数、事件上下文、
  会话与房间游玩身份、重复事件抑制、跨重开的首次使用状态、累计时间、
  剩余切割次数、JSON、队列容量、重试及配置。
- Tutorial 场景加载后没有缺失脚本。通过运行中的控制器检查了切割、
  块交换、颜色变化、视角模式切换和持续旋转。
  自动视角变化，以及没有剩余块时按 F，均未产生使用事件。
- 四个教程房间均产生正确的进入和通关事件。
  重复触发出口或完成方法没有增加通关事件；剩余切割次数在补满前记录。
- 在教程第 2 个房间手动重开，保持了当前房间及游玩 ID。
  将运行中的玩家移到 `deathY` 以下触发了掉落重开。
  两次重开后，尝试次数变为 3，累计用时得以保留。
- 通过菜单使用的关卡目录加载路径直接进入教程第 3 关，
  正确记录为 Level3、索引 2，没有为前两关生成虚假的进入事件。
- NewLevel 可以正常重开。通关界面的 Retry 创建新的房间游玩，
  没有额外计入通关前的重开次数。
- 发布第 2 版规则后，**覆盖全部 12 种事件的 17 条 Editor 测试记录均收到 Firebase 成功响应**：
  `RecordedCount=17`、`UploadedCount=17`、`PendingCount=0`、
  `DroppedCount=0`、`LastUploadError=null`。
  测试版本：`alpha2-editor-check`。
  会话 ID：`9e0980a8f8974b5da7963474811165b3`。
  最后一条事件 ID：`c527a9ded9b8400aba384319f0ebe0f1`。
  检查通过 Unity MCP 调用了实际游戏方法并执行几何切割；
  这些是自动化连接验证数据，不是真实玩家测试数据。
- 验证后已关闭 Editor 上传和临时后台运行。
  教程进度检查后已恢复保存的通关标记。
  Unity 已退出 Play Mode，停在 NewLevel 场景。

此前第 1 版连接检查使用版本 `alpha1-editor-check`，
事件 ID 为 `81d6e48da2b24e49a20b09bf95f5ada8`。
分析玩家数据时，应排除这两个 Editor 检查版本。

WebGL／浏览器运行表现、课堂多人同时访问、真实玩家数据和最终图表仍需单独验收，
不能仅凭 Editor 上传成功认定这些工作已经完成。

参考资料：

- [团队数据分析计划](https://docs.google.com/document/d/1xparAlD7UtPupv3t98-bP9pMT2dUjoulsEBNMR-y-eI/edit)
- [Firebase REST 写入文档](https://firebase.google.com/docs/database/rest/save-data)
- [Firebase 规则条件文档](https://firebase.google.com/docs/database/security/rules-conditions)
