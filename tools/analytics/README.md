# Firebase 数据出图工具（schema v3）

从 Firebase Console 导出的 JSON 生成四项指标的图表和表格。脚本只读取本地文件，
不会连接 Firebase，不需要管理员密钥，也不会上传或修改数据。

支持两种数据：

- **v3（Beta 起）**：`/v3/<build>/<player>/<session>/<事件>`。
- **v2（Alpha）**：`/events/<push key>`。读入时自动改成 v3 的名字（`Tutorial → tutorial`、`NewLevel → level01`、
  `GlassParadox → level02`，段名改为玩家看到的标题），日期从 push key 还原，
  `level_start`、`checkpoint_reached`/`level_complete`、`t_level` 由脚本推算。Alpha 没有玩家 ID，按 session 计。

字段和事件的定义见 [v3 方案](../../docs/analytics/v3-schema-proposal.md)。

## 安装

在仓库根目录运行。以下为 Windows PowerShell 命令：

```powershell
python -m venv .venv-analytics
.\.venv-analytics\Scripts\python.exe -m pip install -r tools/analytics/requirements.txt
```

macOS/Linux 使用 `.venv-analytics/bin/python` 替换上述解释器路径。

## 先看模拟示例

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --demo --output analytics-output/demo
```

每张示例图都标有 **SYNTHETIC DEMO - NOT PLAYER RESULTS**，不能作为玩家测试结果提交。

## 使用真实数据

1. 在 Firebase Console 的 Realtime Database > Data 页面，选中数据库根节点、`v3` 节点或某个版本节点，导出 JSON。
2. 放到 `analytics-data/`，用新文件名（例如 `analytics-data/firebase-2026-11-20.json`）。原始导出只读，不要覆盖。
3. 用准确的版本号运行：

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --input analytics-data/firebase-2026-11-20.json --build beta1-all-metrics --output analytics-output/beta1
```

和 Alpha 做关卡层面的对比（另出 `comparison_levels.png/csv` 和段映射表）：

```powershell
.\.venv-analytics\Scripts\python.exe tools/analytics/plot_analytics.py --input analytics-data/firebase-2026-11-20.json --build beta1-all-metrics --baseline-input analytics-data/firebase-2026-11-01.json --baseline-build alpha2-all-metrics --output analytics-output/beta1-vs-alpha2
```

- `--build` 必填，避免混入其他版本；冒烟测试版本（如 `beta1-smoke`）不要拿来出正式图。
- 默认排除 `is_editor=true`；只在排查时加 `--include-editor`，图上会标 **EDITOR DATA INCLUDED - DIAGNOSTIC ONLY**。
- 筛选后没有数据会直接报错，不会生成全零的假图。
- 每次运行会覆盖同一输出目录，不同版本请用不同目录。

## 输出内容

| 文件 | 内容 |
| --- | --- |
| `metric1_cut_outcomes.png/.svg` | 切割结果：按关卡汇总，Tutorial 再按段细分 |
| `metric2_progression.png/.svg` | 每关从头开始/通关的玩家数；Tutorial 漏斗（停在哪个检查点） |
| `metric3_mechanics.png/.svg` | 机制使用比例：整关（按游玩）和每段（按段游玩）；框出的格子是该段要教的机制 |
| `metric4_difficulty.png/.svg` | 卡关信号：每段到达出口的比例、重开（R/掉落）、耗时和剩余 cut；整关用时与重开 |
| `level_summary.csv` | 每关：开始/通关玩家数、通关率、从中间段开始的游玩数、用时中位数、重开 |
| `funnel.csv` | 多段关卡的漏斗：每个检查点的玩家数、游玩数、停在这里的人数 |
| `segment_difficulty.csv` | 每段：段游玩数、到达出口比例、重开、耗时、剩余 cut、切割失败；`hardest` 标出最难的一段 |
| `cut_outcomes.csv` | 每关、每段的四种切割结果、成功率、保留体积中位数 |
| `mechanics.csv` | 每关、每段每种机制：使用比例、次数、首次使用时间、`taught`（是否为该段要教的机制） |
| `players.csv` | 每位玩家每关一行：游玩次数、最远到达、是否通关、最好用时、重开次数、首次出现日期 |
| `comparison_levels.csv`、`segment_mapping.csv` | 仅在加 `--baseline-*` 时生成 |
| `events_clean.csv` | 清洗后的全部事件（含 `date_utc` 和来源 schema），可用 Excel 打开复查 |
| `summary.json` | 输入文件 SHA-256、版本、过滤和数据质量统计 |
| `report.md` | 中文报告：关键结果（最难的一段、流失最多的位置）、四张图、统计口径 |

PNG 为 200 DPI，方便放进 GDD；SVG 可无损缩放。图中文字使用英文，与游戏一致；横轴不用日期。
CSV 使用 UTF-8 BOM，方便 Excel 读取。未定义的值留空，图中显示 N/A。

## 统计口径

- **一次游玩（run）**：一个 `level_start` 到 `level_complete` 或离开该关；重开不会开始新的游玩。
- **通关率**只看从第 1 段开始的游玩；从菜单直接选中间段开始的计入 `runs_started_mid_level`，不进漏斗。
- **漏斗**按玩家取最好的一次游玩：比如第一次停在检查点 1、第二次通关，算通关。
- **段游玩（visit）**：一段从开始到出口；重开保留同一个 visit，所以重开不会增加 visit 数。
- 时间包含切割暂停、帮助面板和离开窗口的时间；只对到达出口/通关的游玩统计用时。
- 首次使用时间：整关用 `t_level`，分段用 `t_segment`；从未使用不记为 0 秒。
- 「该段要教的机制」来自 LevelCatalog 的机制标签，写在脚本的 `TAUGHT` 中，课程内容改了要同步改。
- 按 `event_id` 去重；同一 ID 内容矛盾、或同一 session 同一序号出现不同事件时全部排除，数量写进 `summary.json`。
- 玩家 = 浏览器里的匿名 ID，不等于真实人数；没有出口事件不能证明放弃，也可能是关页面时数据丢失。

## 测试

```powershell
.\.venv-analytics\Scripts\python.exe -m unittest discover -s tools/analytics -p "test_*.py" -v
```

覆盖：v3 嵌套导出解析、与数据库规则一致的字段校验、重试去重、冲突排除、push key 日期、
Alpha 转换（推算 level_start、检查点/通关、t_level）、漏斗按最好一次、段内重开计数、
「要教的机制」标记、最难一段、命令行出图与对比。2026-11-06：14/14 通过（Python 3.11、matplotlib 3.11.2）。
