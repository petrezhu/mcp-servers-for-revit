# Requirements Document

## Introduction

本需求文档定义 `mcp-servers-for-revit` 中“连接 RevitLookup 以实现低 token 构件查询”的功能需求。

目标是复用 RevitLookup 已有的对象组织方式、继承链成员分组能力以及 descriptor 映射能力，为 MCP 调用方提供一套“先摘要、后展开、再导航”的查询协议，减少调用方通过 `execute` 或一次性大对象序列化来理解复杂 Revit 构件所产生的 token 消耗，同时避免在 `mcp-servers-for-revit` 中重新维护一套独立的构件分类树和复杂值格式化逻辑。

## Requirements

### Requirement 1

**User Story:** 作为 MCP 调用方，我希望系统能够按 RevitLookup 的方式返回当前查询上下文中的根对象列表，以便我先定位要分析的构件，而不是一开始就拿到大体量明细。

#### Acceptance Criteria

1. WHEN 调用方请求根对象列表 THEN 系统 SHALL 优先按当前选择集返回根 `Element` 对象。
2. IF 当前没有选择集 THEN 系统 SHALL 按 RevitLookup 当前行为退化为返回当前视图中的元素作为根对象。
3. WHEN 返回根对象列表 THEN 系统 SHALL 按运行时 `TypeName` 对根对象进行分组。
4. WHEN 返回每个根对象条目 THEN 系统 SHALL 至少提供 `objectHandle`、`elementId`、`title`、`typeName`、`category` 等最小识别信息。
5. IF 运行时可获取 `UniqueId` THEN 系统 SHALL 在根对象条目中返回 `uniqueId`。
6. WHEN 根对象列表超过单次预算 THEN 系统 SHALL 优先裁剪每组返回数量，并显式标记结果已截断。

### Requirement 2

**User Story:** 作为 MCP 调用方，我希望系统能按对象继承层级返回成员目录，以便我快速知道“这个对象有哪些能力区块”，而不是平铺全部成员和值。

#### Acceptance Criteria

1. WHEN 调用方请求某个对象的成员目录 THEN 系统 SHALL 返回按 `DeclaringTypeName` 分组的成员目录。
2. WHEN 返回成员分组 THEN 系统 SHALL 为每组返回 `declaringTypeName`、`depth`、`memberCount` 和预览成员列表。
3. WHEN 成员目录生成完成 THEN 系统 SHALL 复用 RevitLookup/LookupEngine 的继承链分解结果，而不是重新手写一套成员分层逻辑。
4. IF 某个对象的成员数量过多 THEN 系统 SHALL 仅返回每组前 `N` 个预览成员，并标记该组仍有未展开成员。
5. WHEN 调用方仅请求成员目录 THEN 系统 SHALL 默认不返回成员值。

### Requirement 3

**User Story:** 作为 MCP 调用方，我希望只展开我点名的少量成员，以便把 token 消耗控制在问题所需范围内。

#### Acceptance Criteria

1. WHEN 调用方指定对象及成员列表 THEN 系统 SHALL 仅展开被显式请求的成员。
2. WHEN 展开简单标量成员 THEN 系统 SHALL 返回紧凑的标量值表示。
3. WHEN 展开复杂成员值 THEN 系统 SHALL 返回摘要信息，而不是默认返回整棵递归对象图。
4. IF 成员值可继续导航 THEN 系统 SHALL 返回 `valueHandle` 或等价的导航句柄。
5. WHEN 展开成员失败 THEN 系统 SHALL 仅在对应成员结果中返回错误信息，而不是让整次请求整体失败。

### Requirement 4

**User Story:** 作为 MCP 调用方，我希望复杂值能像 RevitLookup 一样继续打开下一层对象页，以便逐层深入而不是一次性获取全部几何、参数和引用关系。

#### Acceptance Criteria

1. WHEN 调用方使用某个可导航值句柄请求下一层对象 THEN 系统 SHALL 返回与对象成员目录相同形状的结果。
2. WHEN 打开下一层对象 THEN 系统 SHALL 保持“按声明类型分组成员”的结构一致性。
3. IF 某个复杂值来源于 descriptor 映射 THEN 系统 SHALL 优先导航 descriptor 生成的可浏览对象。
4. WHEN 调用方未明确继续导航 THEN 系统 SHALL 不主动深度展开复杂值。

### Requirement 5

**User Story:** 作为维护者，我希望系统尽量复用 RevitLookup 的 descriptor 映射能力，以便减少我们自己对 Geometry、BoundingBox、Parameter、Reference 等复杂类型的适配工作。

#### Acceptance Criteria

1. WHEN 查询层遇到存在 descriptor 的值类型 THEN 系统 SHALL 优先使用 `DescriptorsMap` 对值进行映射和摘要化。
2. WHEN 处理 `Geometry`、`BoundingBox`、`GetMaterialIds` 等已被 RevitLookup 重写的成员 THEN 系统 SHALL 复用 descriptor 输出，而不是重新实现独立的格式化分支。
3. IF descriptor 不可用或解析失败 THEN 系统 SHALL 允许回退到反射或最小摘要输出。
4. WHEN 发生回退 THEN 系统 SHALL 在响应元数据中标记使用了 fallback 路径。

### Requirement 6

**User Story:** 作为 MCP 调用方，我希望每次查询都能带 token 预算提示，以便服务端主动裁剪结果而不是把超大响应交给模型消费。

#### Acceptance Criteria

1. WHEN 调用方提供 `tokenBudgetHint` 或等价预算参数 THEN 系统 SHALL 按预算优先返回目录和摘要，而不是大体量明细。
2. WHEN 服务端需要裁剪结果 THEN 系统 SHALL 按“减少组内元素数、减少成员预览数、减少非关键字段、仅返回计数和样本”的顺序裁剪。
3. WHEN 结果被裁剪 THEN 系统 SHALL 在响应中显式返回 `truncated` 或等价标记。
4. WHEN 结果被裁剪 THEN 系统 SHALL 提供建议的下一步动作，例如继续展开某个组或缩小查询范围。

### Requirement 7

**User Story:** 作为维护者，我希望查询协议有稳定的句柄和缓存机制，以便调用方多跳浏览时不用重复传输同一批上下文。

#### Acceptance Criteria

1. WHEN 系统向调用方返回对象或复杂值 THEN 系统 SHALL 返回会话内可解析的短句柄。
2. WHEN 调用方重复请求同一对象的成员目录或同一值的摘要 THEN 系统 SHALL 允许命中会话级缓存。
3. IF 文档切换、选择集变化或模型修改导致上下文失效 THEN 系统 SHALL 使相关句柄和缓存失效。
4. WHEN 句柄失效 THEN 系统 SHALL 返回明确的失效错误，并提示调用方重新获取根对象列表。

### Requirement 8

**User Story:** 作为维护者，我希望新查询协议与现有 `execute` 能清晰分工，以便 inspection 查询走低成本协议，复杂自定义分析仍可保留通用执行能力。

#### Acceptance Criteria

1. WHEN 调用方进行常规构件检查、定位、参数浏览或关系浏览 THEN 系统 SHALL 优先支持通过新查询协议完成，而不是要求调用方先写动态 C#。
2. WHEN 调用方需要超出规范范围的自定义分析或模型修改 THEN 系统 SHALL 允许继续使用 `execute`。
3. WHEN 新查询协议可完成需求 THEN 系统 SHALL 不要求调用方依赖 `execute` 才能获取基础 inspection 信息。

### Requirement 9

**User Story:** 作为维护者，我希望系统在异常、日志和测试方面遵循当前仓库约束，以便该功能能够稳定集成到现有 Revit 插件与 MCP 桥接链路中。

#### Acceptance Criteria

1. WHEN 查询处理过程中发生异常 THEN 系统 SHALL 记录符合项目日志规范的中文日志，并包含模块名、方法名和关键上下文。
2. IF 某个子成员或子值解析失败 THEN 系统 SHALL 避免通过空 catch 吞掉异常。
3. WHEN 新增命令或查询适配器 THEN 系统 SHALL 为核心路径补充自动化测试。
4. WHEN 命令执行涉及对 Revit 上下文的读取或句柄解析 THEN 系统 SHALL 不引入违反当前桥接线程模型的访问方式。
