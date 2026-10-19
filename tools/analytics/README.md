# Firebase 数据出图工具

从 Firebase Console 导出的 JSON 生成四项指标图表。脚本只读取本地文件，
不会连接 Firebase，不需要管理员密钥，也不会上传或修改数据。

## 安装

在仓库根目录运行。以下为 Windows PowerShell 命令，已使用 Python 3.12 验证：

```powershell
python -m venv .venv-analytics
.\.venv-analytics\Scripts\python.exe -m pip install -r tools/analytics/requirements.txt
```

macOS/Linux 使用 `.venv-analytics/bin/python` 替换上述解释器路径。

## 先查看模拟示例

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --demo --output analytics-output/demo
```

会生成四组 PNG/SVG 和汇总 CSV；每张示例图均标有
**SYNTHETIC DEMO - NOT PLAYER RESULTS**，不能作为真实玩家测试结果提交。

## 使用真实数据

1. 在 Firebase Console 的 Realtime Database > Data 页面，选数据库根节点或 `events` 节点，导出 JSON。
2. 将文件放到仓库根目录的 `analytics-data/`，例如 `analytics-data/firebase-export.json`。
3. 确认测试构建使用的 Build Id，然后运行：

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --input analytics-data/firebase-export.json --build alpha2-all-metrics --output analytics-output/alpha2
```

必须指定准确的 `--build`，避免混合不同版本。默认排除所有 `is_editor=true` 的事件。
如果筛选后没有数据，脚本会明确报错，不会生成全零的假结果。

仅排查 Editor 数据时，可使用：

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --input analytics-data/firebase-export.json --build alpha2-editor-check --include-editor --output analytics-output/editor-check
```

含 Editor 数据的图会标为 **EDITOR DATA INCLUDED - DIAGNOSTIC ONLY**。
原始数据、生成的报告和本地 Python 环境已在 `.gitignore` 中排除。

## 输出内容

| 文件 | 内容 |
| --- | --- |
| `metric1_cut_outcomes.png/.svg` | 四种切割结果次数，以及成功切割的保留体积比例分布 |
| `metric2_progression.png/.svg` | 各房间进入／通关会话数，以及通关率 |
| `metric3_mechanics.png/.svg` | 五种机制的使用比例，以及首次使用时间中位数 |
| `metric4_difficulty.png/.svg` | 平均手动／掉落重开次数、累计通关时间、通关剩余切割次数 |
| `cut_outcomes.csv` | 切割次数、成功率、保留体积比例中位数 |
| `progression.csv` | 房间会话数、游玩次数及通关率 |
| `mechanics.csv` | 机制使用次数、使用比例、首次使用样本数与时间 |
| `difficulty.csv` | 重开、用时、剩余次数及剩余比例 |
| `last_rooms.csv` | 每个会话最后观测到的房间及是否观测到其通关 |
| `events_clean.csv` | 筛选、校验和去重后的事件，可用于复查 |
| `summary.json` | 输入文件 SHA-256、版本、筛选及数据质量统计 |
| `report.md` | 中文统计口径说明和四张图的入口 |

PNG 为 200 DPI，方便放进 GDD；SVG 可以无损缩放。图中文字使用英文，保持与游戏一致。
CSV 使用 UTF-8 BOM，方便 Excel 读取。未定义的统计值在 CSV 中留空，图中显示 N/A 或无样本。
每次运行会覆盖同一输出目录的同名结果，不同构建应使用不同目录。

## 统计口径

- 按 `event_id` 去重。相同 ID 的内容矛盾时排除全部矛盾副本；同一会话的同一序号对应多个不同事件时也排除，数量写入 `summary.json`。
- 数据必须具备合法类型与取值；缺少 `is_editor`、未知事件、非有限数值等记录会被排除。
- 指标 1 可使用旧版 v1 切割事件；指标 2–4 需要 v2 的房间字段。
- 房间按 `scene + room` 区分，不会把不同场景中同名的房间混在一起。
- 指标 2 按不同会话统计；重开不会增加进入会话数。仅在同一房间游玩中观测到先进入、后通关，才计入通关数。
- 指标 3、4 以 `session_id + room_visit_id` 为一次游玩；重开保留游玩身份，通关后重玩产生新的身份。
- 机制使用比例 = 使用过该机制的游玩次数 / 观测到进入的游玩次数。首次使用时间只使用 `first_use=true` 的 `t_visit`；未使用不算 0 秒。
- 重开平均值包含零次重开的游玩。通关时间直接读取 `room_clear.t_visit`，不累加中途快照；切割次数读取通关事件，不读取下一关补满后的值。
- 通关时间和剩余次数只使用已通关游玩；`progression.csv` 的游玩次数减去已通关次数得到观测到的未通关次数。
- 未观测到房间进入的事件不计入指标 2–4；同一个游玩 ID 对应多个房间的异常记录也不计入这些指标。
- 会话数不等于独立玩家人数；退出原因不能从最后一条事件确定。网络丢失也可能造成不完整记录。

## 测试

```powershell
.\.venv-analytics\Scripts\python.exe -m unittest discover -s tools/analytics -p "test_*.py" -v
```

测试覆盖重复上传、数据冲突、过滤、房间分组、重开不重复计数、首次使用、累计用时、旧版数据与模拟数据。
2026-10-18 验证：11/11 项测试通过；完整 JSON 导入出图、默认排除 Editor、无匹配数据报错、
仅含 v1 切割数据的出图均已运行检查，四组模拟图已做显示检查。
真实玩家图表仍需完成浏览器构建验证并收集玩家数据后生成。
