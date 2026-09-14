# ReaderPro · Windows 平台专业级电子书阅读器（新项目）

> 立项：2026-09-11 ｜ 对标：静读天下专业版（Moon+ Reader Pro）｜ 适用平台：Windows 10 1903+ / Windows 11（x86 / x64 / ARM64）
> 依据 PRD：`docs/PRD.md` ｜ 前身项目教训：`D:\Book\book\开发总结.md`

## 一、项目目标

完整对标静读天下专业版全量功能，覆盖三大场景：
1. **电子书休闲阅读**（TXT/EPUB/MOBI 长篇深度阅读）
2. **专业文献学习**（PDF 标注、笔记、翻译、词典）
3. **数字藏书管理**（大规模书籍分类、管理、备份）

结合 PC 端特性增强：键鼠、多屏、大文件秒开、系统深度集成。

## 二、技术栈（决策记录见 docs/DECISIONS.md）

| 层 | 选择 | 理由 |
|---|---|---|
| UI 框架 | **WPF (.NET 8)** | 原生、低内存（空闲 ≤50MB 可达成）、XAML 渲染强（PDF 批注/双页/竖排/动画可做）、开发迭代快；避开 Electron 浏览器内核（历史否决） |
| 语言 | C# 12 | 主开发语言 |
| 运行时 | .NET 8（Win10 1903+ / ARM64 官方支持） | 符合 PRD 适用平台 |
| 解析 | 自研解析引擎 + 成熟库（PdfiumViewer/PDFsharp、SharpCompress 等） | 按 PRD「禁止重复造轮子、全部自包含可审计」 |
| 数据 | 本地 JSON（data/ 与程序分离）+ SQLite（规模化藏书） | 用户数据可迁移 |
| TTS | Windows 系统 TTS（System.Speech）+ 第三方语音包 | PRD 3.10 |
| 云同步 | WebDAV / OneDrive（后置 P2） | PRD 3.8 |

## 三、里程碑（按 PRD 优先级 P0→P1→P2 + 功能融合框架）

> 功能组织遵循**四大能力中心**（见 `docs/PRD.md` 附录 A 与 `docs/DECISIONS.md` D-007）：① 元数据智能中心（智能整理）② 格式解析中心（打开即读）③ 内容体验中心（读与听）④ 系统服务中心（不打扰）。新增功能先归位再排期，禁止散落按钮。

- **M1 (P0 最低可用) ✅**：TXT/EPUB/MOBI 解析（格式解析中心就绪）+ 书架 + 阅读（滚动/翻页）+ 排版定制 + 目录导航 + 主题 + TTS + 进度记忆
- **M2 (P1 完整版)**：**第一步：元数据智能中心「智能整理」流水线**（扫描→置信度→修正→补全→一次确认，吸收旧项目全部爬取/补全功能）；内容体验中心融合（进度↔听书互通、标注整合）；PDF 基础 + 词典翻译 + 阅读统计 + 多视图书架 + 搜索 + 统一设置面板（系统服务中心）
- **M3 (P2 增强)**：PDF 专业（手写/表单/专属主题）+ 云同步（归入系统服务中心）+ 应用锁/Windows Hello + 插件 + 多语言 + 漫画（CBR/CBZ）+ 系统集成（右键/跳转列表/文件关联）

## 三·一、当前进度（v0.2.2）
- M1 完成：解析引擎（TXT/EPUB/MOBI + 编码识别 + 水印清洗，测试 16/16 全绿）、书库（导入/分类/搜索/封面缓存）、阅读（章节/排版/主题/进度）、听书（System.Speech 离线）、Release 便携单 exe 打包。
- 交付物：`dist\win-x64-portable\ReaderPro.exe`（自包含免装 .NET）。

## 四、目录结构

```
ReaderPro/
├─ docs/                # PRD、决策记录、开发总结
├─ data/                # 本地数据（JSON/SQLite，与程序分离、可迁移）
├─ src/ReaderPro/       # WPF 主工程（三层：App / Engine / Views+ViewModels）
│  ├─ Engine/           # 能力引擎层：Parsers 解析 / Render 排版渲染 / Pdf / Data 存储
│  ├─ App/              # 应用功能层：Shelf 书架 / Reader 阅读 / Annotate 标注 / Sync 同步 / Settings
│  ├─ ViewModels/       # MVVM
│  └─ Views/            # XAML 视图
└─ tests/               # 单元测试（解析引擎/存储引擎优先）
```

## 五、开发约定（吸取旧项目教训，强制）

- 每次改动前**必读**：`AGENTS.md` + `CHANGELOG.md`（顶部）+ `data/notes.json`（想法待办）+ `data/ai_log.json`（上下文备份）
- 每次改动后**必写**：CHANGELOG 顶部加条目 + ai_log 追加对话记录
- 技术决策必须落 `docs/DECISIONS.md`，防止反复回滚（旧项目 UI 反复回滚教训）
- 不改 UI 前先对齐意图；不引入浏览器内核/黑盒组件
- 打包验证链：`dotnet build`（Release x64/ARM64）→ 启动冒烟 → 记录验证结果
