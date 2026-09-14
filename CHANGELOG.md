# CHANGELOG

## v0.2.12（2026-09-12）书架布局：多列 + 右侧简介面板
- 书架卡片改 WrapPanel 多列排列；设置里新增「书架列数」（1/2/3 列），自动持久化。
- **1-2 列时**：书架右侧显示简介面板，单击选中书即显示书名/作者/内容简介（无简介提示去智能整理补全）；
  **3 列**：紧凑多列，不显示简介面板。
- 交互改为：**单击选中（右侧看简介），双击打开阅读**（原单击直接打开）。
- 测试 35/35 绿，便携版重发布。

## v0.2.11（2026-09-12）修复智能整理列表渲染绑定错误
- crash.log 抓到第二条：列表项 Run.Text 绑只读属性 SuggestedTitle/SuggestedAuthor/SuspectReasons，WPF Run.Text 默认双向绑定，只读属性报 XamlParseException——列表渲染即崩，表现为"共 0 条"。
- 修复：三处绑定显式 Mode=OneWay。
- 便携版重发布。

## v0.2.10（2026-09-12）修复智能整理闪退（NullToVis 资源缺失）
- 崩溃日志抓到：OrganizeWindow 列表模板引用 `NullToVis`（NullToVisibilityConverter），但窗口 Resources 只定义了 NullToVisInverse，第一条书渲染即抛 XamlParseException——表现为"共 0 条待确认"、按钮点不了/闪退。
- 修复：Resources 补上 `NullToVis`。
- 另加全局崩溃捕获：任何未处理异常写入 data/crash.log（本次即靠它定位）。
- 测试 35/35 绿，便携版重发布。

## v0.2.9（2026-09-12）智能整理：改为对全部书架书联网补全
- 修复用户反馈：智能整理原来只挑"疑似书名有误"的书，书库正常时直接提示"没有疑似书"，补全简介/封面的功能根本触发不了。
- 改为：书架**所有书**都进待确认列表——疑似书给具体理由（书名过长/序号结尾等），正常书标注"联网补全简介/封面/作者"。
- 点"全部联网比对"后对所有书并发查豆瓣/必应，采纳即补全（含 v0.2.8 新增的真正内容简介）。
- 测试：35/35 全绿；便携版重发布。

## v0.2.8（2026-09-12）元数据增强：豆瓣详情页抓真正内容简介
- 豆瓣搜索结果的 abstract 是出版信息（出版社/ISBN），本版新增：
  - 搜索时保留豆瓣图书详情页 URL；
  - 对前 2 个豆瓣候选并发抓详情页，正则提取 `id="link-report"` 内 `.intro` 块（v:description 已随豆瓣改版移除），清洗 HTML/段落换行后替换为真正的内容简介。
- 实测：抓《三体》详情页拿到 235 字内容简介（"文化大革命如火如荼进行的同时……"）。
- 失败自动回退：详情页抓取失败保留原 abstract，不阻断整理。
- 测试：35/35 全绿；便携版重发布，冒烟存活。

## v0.2.7（2026-09-12）系统服务中心：系统托盘 + 退出确认
- 系统托盘（PRD 3.4.3）：托盘图标常驻，双击唤起主窗口；右键菜单「显示主窗口/退出」。
- 退出确认：点窗口 X 弹对话框——「最小化到托盘（后台运行）/直接退出」+「记住我的选择（不再询问）」勾选；记住后按记忆直接执行，不再询问。
- 行为持久化：AskOnClose/CloseAction 写入 data/settings.json。
- 技术：csproj 启用 UseWindowsForms（NotifyIcon），修复 WPF/WinForms 类型歧义（Color/Brush/Binding/Application/MessageBox/ListBox/Cursors 别名）。
- 测试：35/35 全绿；便携版重发布，冒烟存活。

## v0.2.6（2026-09-12）系统服务中心：统一分类设置面板 + 设置持久化
- 新增 `Engine/Data/AppSettings.cs`：AppSettings（字号/主题/语速/音量/语音）+ SettingsStore（data/settings.json 读写，损坏回默认不阻断启动）。
- `MainViewModel`：启动时应用已存设置；字号/主题/语速/音量/语音变化自动写回（SaveSettings）；新增 Volume/SelectedVoice/ThemeName/DataDir/AppVersion 属性；OpenSettings() 打开设置窗口。
- 新增 `Views/SettingsWindow.xaml(.cs)`：统一分类面板——阅读（字号滑杆 12-48、主题下拉 白天/夜间/护眼）、听书（离线语音包下拉、语速、音量）、关于（版本号、数据目录+打开按钮）。
- 工具栏新增「设置」按钮（书架+阅读模式均可见）。
- 测试：35/35 全绿（新增 SettingsStore 3 项：往返/缺文件默认/损坏回退）；便携版重发布，冒烟存活。

## v0.2.5（2026-09-12）内容体验中心：听书↔阅读进度互通 + 分类树遮挡修复
- **新功能（内容体验中心，PRD 3.10 逐句高亮/听到哪读到哪）**：
  - `TtsService` 重构：`SpeakUnit(章节,段落,文本)` 队列，跨章节连续朗读、暂停/恢复/停止、按单元回调进度。
  - `MainViewModel`：
    - `BuildSpeakQueue`：从当前章节/段组装到书尾的朗读队列，章首插章节标题提示（para=-1 哨兵）。
    - 朗读从**当前阅读位置**开始（不再从章节 0）；当前段落高亮（主题色半透明 + 加粗）+ 正文自动滚动跟随（`ScrollToParagraph` 事件）。
    - 用户手动切换章节 → 朗读自动跟随到新章节继续（`_isSpeechFollow` 守卫防递归）。
    - 控制条：朗读/暂停(▶/⏸ 动态按钮)/停止、语速滑杆、朗读状态栏（"正在朗读：第X章 · 第a/b 段"）。
  - 段落模型升级 `ParagraphItem(Text,IsCurrent)`；`BodyText` 适配。
- **Bug 修复（用户截图反馈）**：左侧分类树被书库列表遮挡——Books ListBox 原 `Grid.ColumnSpan=2` 不透明背景盖住分类树；改为 `Grid.Column=1`，恢复两列布局（左 230 分类树，右书库）。
- 测试：32/32 全绿（新增 SpeakQueue 4 项：跨章队列/章首标题哨兵/空章跳过/越界空队列）；便携版已重发布。

## v0.2.4（2026-09-12）M2 第一步：元数据智能中心「智能整理」流水线
- 新增 `Engine/Metadata/`：
  - `TitleAnalyzer.cs`：文件名清洗（去括号网站/作者标签/网址域名/噪音词）+ 置信度判定（过长/网址/章节残留/乱码→疑似，吸取旧项目《末日乐园》yeudusk 教训）。
  - `TitleSimilarity.cs`：书名归一化 + 编辑距离 + 包含加权（0..1）。
  - `MetadataService.cs`：多源并发查书，8s 超时、单源失败不阻断。**实测源**：豆瓣搜索（`window.__DATA__` IndexOf 截取，含书名/作者/出版信息/封面）、必应搜索（兜底书名，噪音过滤）；知轩藏书 zxcs.me（本机 DNS 不可达）与百度（反爬验证页）实测不可用，未纳入（记录于代码注释）。
- 新增 `ViewModels/OrganizeViewModel.cs` + `Views/OrganizeWindow.xaml(.cs)`：待确认列表（原书名→建议书名/作者/出版信息/封面缩略，行内采纳/忽略、全部采纳、保存并关闭）。
- `BookRecord` 新增 `Description` 字段；采纳后落库书名/作者/简介/封面（封面下载到 data/covers）。
- `MainWindow` 工具栏新增「智能整理」入口（书架模式，按 PRD 附录 A 书架按钮 ≤8 约束），关闭后书架与分类树刷新。
- 测试：28/28 全绿（新增 TitleAnalyzer/TitleSimilarity 12 项）；联网冒烟 2/2（豆瓣+必应真实返回，跑后删除不留在测试集）；便携版冒烟存活（141MB 工作集）。

## v0.2.3（2026-09-12）功能融合规划落地（四大能力中心）
- 用户反馈：功能设计臃肿，散落功能应融合（如智能爬取）。
- 落地（纯文档规划，不涉代码）：
  - `docs/PRD.md` 增补**附录 A：功能融合规划**——融合原则（按用户意图组织、一个意图=一条流水线=一次交互、新功能先归位再排期、数据只存一份）；四大能力中心功能归位矩阵；「智能整理」流水线示例；验收口径（核心功能 3 步内、书架按钮 ≤8、设置按分类）。
  - `docs/DECISIONS.md` 新增 **D-007**（四大能力中心 + 硬约束：禁散落按钮、入口用用户语言）。
  - `README.md` 里程碑按能力中心框架重排：M2 第一步 = 元数据智能中心「智能整理」流水线（吸收旧项目全部爬取/补全/修正功能为一步）；并记录当前进度 v0.2.2。
- **下一步（M2 起点）**：实现「智能整理」流水线（扫描→置信度→修正→补全→一次确认）。

## v0.2.2（2026-09-12）MOBI 解析 + 首行缩进 + Release 打包（测试 16/16）
- 解析引擎：新增 `MobiParser`（手写，无需第三方库）——
  - PalmDB 记录表 + PalmDOC 头（压缩类型 0xCCCC 未压缩 / 0x01 LZ77）；
  - PalmDOC LZ77 解压（flag 位回引，含压缩单测 "ABABAB"）；
  - EXTH 元数据（书名 type=0x03 / 作者 type=0x04，UTF-8）；
  - HTML 正文提取 + `<mbp:pagebreak/>` 分章 + `<h1>` 章标题；
  - `ParserFactory` 注册 .mobi/.prc/.azw。
  - 修复：MOBI 头偏移（headerLength @ +8 非 +4；textEncoding @ +0x10 非 +0x1C）；EXTH 测试构造头字段数（28 非 24）。
  - 暂不支持（M2）：KF8/AZW3 HUFF/CDIC 压缩与复杂版式——构造测试覆盖未压缩路径，真实网文 mobi 多为 PalmDOC 压缩路径，已实现。
- 阅读体验：正文段落首行缩进（两全角字符）；书籍卡片悬停高亮（Accent 边框 + 淡色背景）。
- 打包：Release 三态发布验证——framework-dependent（exe 148KB）/ R2R / **self-contained 单 exe 便携版**（163MB，免装 .NET，`dist\win-x64-portable\ReaderPro.exe`）。
  - 内存实测：便携版空闲工作集 138MB / 私有内存 91MB。PRD 6.1「≤50MB」与 WPF 基线冲突，权衡落档 DECISIONS D-005（功能密度优先，验收以实际体验为准）。
- 测试：16/16 全绿（MOBI 构造 3 例 + 原 13 例）。
- **下一步**：M2 — PDF/DJVU 引擎、设置面板（排版参数 UI）、主题扩展、阅读统计。

## v0.2.1（2026-09-12）M1 桌面层第一版：书架 + 分类 + 导入 + 阅读 + 排版 + 主题 + 听书
- ViewModels：`ObservableObject` 基类、`ThemeManager`（白天/夜间/护眼三主题）、`MainViewModel`（书库/分类/搜索/导入/阅读/排版/主题/听书/封面缓存）。
  - 封面加载：后台线程解码（并发≤2）+ LRU 缓存（上限 400 张）+ Dispatcher 回 UI——吸取旧项目滚动卡顿教训。
  - 导入：`ScanAndImportAsync` 智能扫描（递归、扩展名过滤、按路径去重避免重复导入、单文件失败容错不中断整批）。
  - 阅读：章节列表 + 正文段落（ListBox 虚拟化）、章节切换、进度保存（章节+滚动比例）。
  - 排版：字号 A±（12–48，行高/段距联动）；主题一键切换。
  - 听书：`TtsService`（System.Speech，Windows 离线语音）——开始/暂停/停止 + 语速滑块（-5..5）+ 段落进度回调。
- Views：`MainWindow` 重写——顶部工具栏（搜索/导入/字号/主题/听书/返回）、左分类树（全部/收藏/作者/格式/标签，一书可多标签多节点）、书籍卡片（封面 84×112 + 书名/作者/进度，无封面显示书名首字符占位）、阅读双栏（章节+正文）、底部控制条。
- Converters：NullToVis / NullToVisInverse / InverseBoolToVis。
- 修复：ListBox 无 ScrollChanged（改 Loaded 后 VisualTree 查找内部 ScrollViewer 订阅）；XAML Margin 不能拼接 Binding 与文本（改固定边距）；App.xaml 移除 StartupUri（MainWindow 改依赖注入构造）。
- 验证：主工程 0 警告 0 错误；单测 13/13 全绿；启动冒烟存活（Debug 内存 144MB，Release 待发布优化）；真实 5.8MB《斗罗大陆》GBK txt 端到端解析 142ms、章节>50、无乱码无水印（PRD：100MB≤1s 目标达标）。
- **下一步**：MOBI/AZW3 解析（PRD P0）、阅读首行缩进+分页布局（FlowDocument）、主题应用视觉确认、Release 打包。

## v0.2.0（2026-09-12）M1 后端第一层：解析引擎 + 数据引擎 + 测试全绿（14/14）
- 解析引擎（Engine/Parsers）：
  - `BookDocument`/`BookChapter` 文档模型；`IBookParser` 接口 + `ParserFactory` 扩展名分派（插件扩展预留）。
  - `EncodingDetector`：BOM（UTF-32/UTF-16LE/BE/UTF-8）→ 无 BOM UTF-16 NUL 启发式 → UTF-8 严格 → GB18030 回退。
    - 修复：无 BOM 的 UTF-16 曾被误判 GB18030（`Encoding.Unicode.GetBytes` 不写 BOM；纯 ASCII UTF-16 会被"合法 UTF-8"误吞，故 NUL 启发式必须前置）。
  - `TxtParser`：章节标题正则（第X章/序章/楔子/番外/Chapter N…）、整行水印/广告丢弃（吸取旧项目《末日乐园》yeudusk 教训）、空行占比≥6% 用空行分段否则按行尾闭合标点合并续行、章节标题独立成段。
    - 修复：逐行合并模式下章节标题被拼进正文段导致分章失败。
  - `EpubParser`：container.xml→OPF（title/creator/封面提取到 data/covers）→spine itemref→各 XHTML 章节（XmlReader 解析，含宽松正则回退）。
- 数据引擎（Engine/Data）：`BookRepository` books.json/progress.json 持久化、AddOrUpdate/Remove/进度、ScanFolder 递归扫描（扩展名过滤+跳过隐藏/无权限目录）。
- 测试：xUnit 工程 14 用例全绿（TXT 编码/水印/分章/分段 9 例 + EPUB 2 例 + 书库 2 例 + 新增逐行标题独立成段 1 例）。
  - 修：xUnit2009 警告（Assert.True→Assert.EndsWith）。
- 坑位记录：WPF SDK ImplicitUsings 不含 System.IO/Linq → 新增 `GlobalUsings.cs` 统一补齐；C# 原始字符串字面量 `"""` 语法易炸 → 统一普通字符串拼接。
- **下一步**：M1 — 书架 UI + 阅读渲染 + 排版参数 + 主题系统 + TTS 朗读。

## v0.1.1（2026-09-11）工具链就绪 + 骨架编译通过
- 安装 .NET 8 SDK 8.0.425（winget，本机原无 dotnet）。
- 修复 App.xaml.cs 缺 `using System.IO;`（WPF ImplicitUsings 不含 IO）。
- `dotnet build -c Debug` 0 错误 0 警告；exe 生成 151KB；启动冒烟 5 秒存活正常。
- **下一步**：M1（P0）— TXT/EPUB/MOBI 解析引擎 + 书架 + 阅读渲染 + 排版 + 主题 + TTS。

## v0.1.0（2026-09-11）项目初始化
- 建新项目 `ReaderPro`（吸取旧项目教训：日志机制从第一天建立、技术决策落档、里程碑分级）。
- 技术栈决策：WPF + .NET 8（见 docs/DECISIONS.md D-001）。
- 目录骨架：docs/（PRD、DECISIONS）、data/、src/ReaderPro（Engine/App/ViewModels/Views 三层）、tests/。
- 建立维护约定 AGENTS.md（改前必读三文件、改后必写日志、打包验证链）。
- **待办**：安装 .NET 8 SDK → 验证骨架可编译 → 进入 M1（P0：TXT/EPUB/MOBI 解析 + 书架 + 阅读 + 排版 + 主题 + TTS）。
