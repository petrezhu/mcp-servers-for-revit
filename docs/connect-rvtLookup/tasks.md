# Implementation Plan

- [ ] 1. 建立 connect-rvtLookup 查询协议的基础模型与命令骨架
  - 在 `commandset` 中新增查询命令入口、请求模型、响应模型和公共错误码定义。
  - 为根对象、成员分组、成员展开、导航句柄建立独立的数据模型类型。
  - 为命令层结构和模型序列化补充基础测试。
  - _Requirements: 1.4, 2.1, 3.1, 4.1, 9.3_

- [ ] 1.1 实现会话级 `QueryHandleStore`
  - 编写对象句柄和值句柄的注册、解析、过期和失效逻辑。
  - 处理文档切换、选择变化和模型修改导致的句柄失效。
  - 为有效句柄、无效句柄和失效句柄路径编写测试。
  - _Requirements: 4.1, 7.1, 7.2, 7.3, 7.4_

- [ ] 1.2 实现 `QueryBudgetService`
  - 编写基于 `tokenBudgetHint` 的裁剪策略和截断元数据生成逻辑。
  - 按“组内元素数 -> 成员预览数 -> 非关键字段 -> 计数与样本”的顺序实现裁剪。
  - 为不同预算下的裁剪行为编写单元测试。
  - _Requirements: 1.6, 2.4, 6.1, 6.2, 6.3, 6.4_

- [ ] 2. 实现 RevitLookup 根对象桥接与 `selection_roots` 命令
  - 编写桥接服务，复用 RevitLookup 的 selection/current view 根对象收集语义。
  - 输出按运行时 `TypeName` 分组的紧凑根对象列表。
  - 为有选择集和无选择集两条路径编写集成测试。
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.1_

- [ ] 2.1 实现根对象标题与最小识别信息投影
  - 复用 descriptor 命名规则生成 `title`，并补齐 `elementId`、`uniqueId`、`typeName`、`category` 等字段。
  - 确保根对象结果不会默认带出成员值或参数表。
  - 为标题生成和字段缺失场景编写测试。
  - _Requirements: 1.4, 1.5, 6.1_

- [ ] 3. 实现对象成员目录桥接与 `object_member_groups` 命令
  - 通过 LookupEngine 分解结果获取成员元数据并按 `DeclaringTypeName`、`Depth` 分组。
  - 仅输出成员目录、数量和预览成员列表，不输出成员值。
  - 为成员分组一致性和超量预览裁剪编写测试。
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [ ] 3.1 实现成员目录投影与预览排序策略
  - 编写成员目录投影器，统一处理 `memberCount`、`topMembers`、`hasMoreMembers`。
  - 为大成员集场景增加预算裁剪和预览保序逻辑。
  - 为不同继承链对象类型编写单元测试。
  - _Requirements: 2.2, 2.4, 6.2, 6.3_

- [ ] 4. 实现定向成员展开与 `expand_members` 命令
  - 只对调用方显式请求的成员执行取值。
  - 为标量、枚举、复杂对象和集合输出不同 `valueKind` 结果。
  - 为成员局部失败、整体成功路径编写测试。
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 9.2_

- [ ] 4.1 接入 `DescriptorsMap` 优先的复杂值摘要逻辑
  - 为 `Geometry`、`BoundingBox`、`GetMaterialIds` 等 descriptor 已覆盖成员实现 descriptor-first 摘要输出。
  - 在 descriptor 不可用时实现 fallback 摘要路径，并在结果中标记 `usedFallback`。
  - 为 descriptor 成功和 fallback 两条路径编写测试。
  - _Requirements: 4.3, 5.1, 5.2, 5.3, 5.4_

- [ ] 5. 实现深层导航与 `navigate_object` 命令
  - 支持通过 `valueHandle` 打开复杂值对应的下一层对象页。
  - 复用与 `object_member_groups` 相同的目录输出形状。
  - 为 descriptor 对象导航和不可导航值路径编写测试。
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [ ] 5.1 完成命令间的句柄串联与缓存复用
  - 打通 `selection_roots -> object_member_groups -> expand_members -> navigate_object` 的句柄流转。
  - 对重复目录请求和重复展开请求接入缓存命中逻辑。
  - 为多跳浏览链路编写集成测试。
  - _Requirements: 7.1, 7.2, 8.1, 8.3_

- [ ] 6. 增加 fallback、错误码与日志集成
  - 为无活动文档、句柄无效、RevitLookup 不可用、超时和成员展开失败补充统一错误输出。
  - 接入符合项目规范的中文日志记录，包含模块名、方法名、元素 ID、句柄和异常上下文。
  - 为关键错误路径编写自动化测试。
  - _Requirements: 5.3, 5.4, 7.4, 9.1, 9.2, 9.4_

- [ ] 7. 将新查询协议接入 `command.json` 和服务端工具注册
  - 在 `commandset` 中注册新命令，并同步更新 MCP server 侧工具暴露逻辑。
  - 确保 inspection 场景优先走新命令，而不是依赖 `execute`。
  - 为命令注册和工具可见性编写测试。
  - _Requirements: 8.1, 8.2, 8.3, 9.3_

- [ ] 7.1 为 `execute` 共存路径增加边界保护
  - 保持 `execute` 作为自定义分析和修改场景的后备能力，不在本次改动中破坏现有执行路径。
  - 为新旧路径共存编写回归测试，确保 inspection 场景不再必须依赖 `execute`。
  - _Requirements: 8.1, 8.2, 8.3_
