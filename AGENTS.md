# AGENTS.md — ReaderPro 维护约定（新项目，吸取旧项目教训）

## 更新 / 修复前必读（强制）
每次对 ReaderPro 做更新、修复或功能调整前，**必须**先读取以下文件：

1. `CHANGELOG.md` — 更新日志：了解已做过的改动与当前里程碑。
2. `data/notes.json` — 用户「我的想法」待办，未标记 `done` 的条目即待办，据此确定本轮内容。
3. `data/ai_log.json` — 开发者 × AI 历史对话（上下文备份）。上下文被压缩/丢失时先读它恢复脉络。

完成后：
- 把本次改动追加到 `CHANGELOG.md` 最上方（新版本在前）；
- 把本轮对话（用户需求原文 + AI 改动/验证摘要）追加到 `data/ai_log.json` 最上方；
- 采纳某条想法时同步在 `data/notes.json` 标 `done: true`；
- 涉及技术选型/架构的决策，追加到 `docs/DECISIONS.md`（记录理由与否决项，防止反复回滚）。

## 运行与构建
- 构建：`dotnet build src/ReaderPro/ReaderPro.csproj -c Release`
- 运行：`dotnet run --project src/ReaderPro` 或 Release 产物 exe
- 打包：`dotnet publish src/ReaderPro -c Release -r <win-x64|win-arm64> --self-contained false`（按 PRD 支持 x86/x64/ARM64）
- 测试：`dotnet test tests`

## 架构与目录（按 PRD 三层）
- `src/ReaderPro/Engine/`：能力引擎层 — Parsers 格式解析 / Render 排版渲染 / Pdf PDF 引擎 / Data 数据存储
- `src/ReaderPro/App/`：应用功能层 — Shelf 书架 / Reader 阅读 / Annotate 标注笔记 / Sync 云同步 / Settings 系统设置
- `src/ReaderPro/ViewModels/` + `Views/`：MVVM
- `data/`：本地数据（JSON/SQLite），与程序分离、可迁移

## 硬性约束（PRD + 历史教训）
- 核心阅读功能完全离线可用；联网仅限可选功能（词典/翻译/在线书库/云同步）
- 无广告、无后台数据采集、无静默上传；无闭源黑盒组件，全部自包含可审计
- 大文件（500MB+ PDF/EPUB）秒开、翻页无卡顿；空闲内存 ≤50MB 指标
- **不引入浏览器内核**（历史否决：起点直连/网页端/Electron 均被用户否决）
- 数据格式与 `data/` 布局保持稳定；新功能先读旧约定，不静默重构

## 优先级推进
按 PRD：P0（M1）→ P1（M2）→ P2（M3），每里程碑交付可运行版本。需求有歧义时：记录假设到 DECISIONS.md 并在交付说明列出，不静默省略功能。
