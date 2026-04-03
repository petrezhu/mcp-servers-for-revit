# Implementation Plan

## Phase 1（已完成）

- [x] 1. 建立 connect-rvtLookup 查询协议的基础模型与命令骨架
  - 在 `commandset` 中新增查询命令入口、请求模型、响应模型和公共错误码定义。
  - 为根对象、成员分组、成员展开、导航句柄建立独立的数据模型类型。
  - 为命令层结构和模型序列化补充基础测试。
  - _Requirements: 1.4, 2.1, 3.1, 4.1, 9.3_

- [x] 1.1 实现会话级 `QueryHandleStore`
  - 编写对象句柄和值句柄的注册、解析、过期和失效逻辑。
  - 处理文档切换、选择变化和模型修改导致的句柄失效。
  - 为有效句柄、无效句柄和失效句柄路径编写测试。
  - _Requirements: 4.1, 7.1, 7.2, 7.3, 7.4_

- [x] 1.2 实现 `QueryBudgetService`
  - 编写基于 `tokenBudgetHint` 的裁剪策略和截断元数据生成逻辑。
  - 按“组内元素数 -> 成员预览数 -> 非关键字段 -> 计数与样本”的顺序实现裁剪。
  - 为不同预算下的裁剪行为编写单元测试。
  - _Requirements: 1.6, 2.4, 6.1, 6.2, 6.3, 6.4_

- [x] 2. 实现 RevitLookup 根对象桥接与 `selection_roots` 命令
  - 编写桥接服务，复用 RevitLookup 的 selection/current view 根对象收集语义。
  - 输出按运行时 `TypeName` 分组的紧凑根对象列表。
  - 为有选择集和无选择集两条路径编写集成测试。
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 8.1_

- [x] 2.1 实现根对象标题与最小识别信息投影
  - 复用 descriptor 命名规则生成 `title`，并补齐 `elementId`、`uniqueId`、`typeName`、`category` 等字段。
  - 确保根对象结果不会默认带出成员值或参数表。
  - _Requirements: 1.4, 1.5, 6.1_

- [x] 3. 实现对象成员目录桥接与 `object_member_groups` 命令
  - 通过 LookupEngine 分解结果获取成员元数据并按 `DeclaringTypeName`、`Depth` 分组。
  - 仅输出成员目录、数量和预览成员列表，不输出成员值。
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 3.1 实现成员目录投影与预览排序策略
  - 统一处理 `memberCount`、`topMembers`、`hasMoreMembers`，并支持预算裁剪。
  - _Requirements: 2.2, 2.4, 6.2, 6.3_

- [x] 4. 实现定向成员展开与 `expand_members` 命令
  - 只对调用方显式请求的成员执行取值。
  - 为标量、枚举、复杂对象和集合输出不同 `valueKind` 结果。
  - 支持成员局部失败、整体成功。
  - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 9.2_

- [x] 4.1 接入 `DescriptorsMap` 优先的复杂值摘要逻辑
  - 为 descriptor 已覆盖成员实现 descriptor-first 摘要输出。
  - descriptor 不可用时回退并标记 `usedFallback`。
  - _Requirements: 4.3, 5.1, 5.2, 5.3, 5.4_

- [x] 5. 实现深层导航与 `navigate_object` 命令
  - 支持通过 `valueHandle` 打开复杂值的下一层对象页。
  - 复用 `object_member_groups` 的目录输出形状。
  - _Requirements: 4.1, 4.2, 4.3, 4.4_

- [x] 5.1 完成命令间句柄串联与缓存复用
  - 打通 `selection_roots -> object_member_groups -> expand_members -> navigate_object`。
  - 接入重复请求缓存命中逻辑。
  - _Requirements: 7.1, 7.2, 8.1, 8.3_

- [x] 6. 增加 fallback、错误码与日志集成
  - 为无活动文档、句柄无效、RevitLookup 不可用、超时和成员展开失败补充统一错误输出。
  - 接入项目规范的中文日志。
  - _Requirements: 5.3, 5.4, 7.4, 9.1, 9.2, 9.4_

- [x] 7. 将新查询协议接入 `command.json` 和服务端工具注册
  - 在 `commandset` 注册命令并在 MCP server 暴露对应工具。
  - 确保 inspection 场景优先走新协议。
  - _Requirements: 8.1, 8.2, 8.3, 9.3_

- [x] 7.1 为 `execute` 共存路径增加边界保护
  - 保持 `execute` 作为自定义分析和修改场景后备能力。
  - 增加新旧路径共存回归测试。
  - _Requirements: 8.1, 8.2, 8.3_

## Phase 1.5（文档契约冻结与工程化加固，进行中）

- [ ] 8. 冻结 Code Mode v1 工具契约并发布“单一事实来源”
  - 在 README 明确默认暴露的完整工具集合与职责边界（inspection flow / execute / lookup / search / alias）。
  - 补充 `REVIT_MCP_TOOLSET` 与 `REVIT_MCP_ENABLE_LEGACY_TOOLS` 行为定义。
  - 将 tool order 与推荐调用路径写成可测试契约，而不只停留在描述。
  - _Requirements: 6.4, 8.1, 8.2, 8.3, 9.3_

- [ ] 8.1 对齐 `README.md` 与 `README-zh.md` 的行为描述
  - 统一默认工具列表、`read_only/modify` 约束、`search` 使用时机。
  - 统一平台安装示例（Windows 与 macOS/Linux）。
  - 增加“文档变更必须同步中英文”的贡献约束。
  - _Requirements: 8.1, 8.2, 9.3_

- [ ] 9. 增加协议稳定性测试（payload schema / 字段兼容）
  - 为 `selection_roots` / `object_member_groups` / `expand_members` / `navigate_object` 建立关键字段快照测试。
  - 为 `execute` / `lookup_engine_query` 的 `nextBestAction`、`completionHint`、错误码增加契约回归测试。
  - _Requirements: 3.5, 6.3, 7.4, 9.3_

- [ ] 10. 增加 PR 级 CI 质量门禁（非 release）
  - 增加 GitHub Actions：server build + server tests。
  - 增加最小静态检查（TypeScript compile、基础 lint 或格式检查）。
  - 在 PR 阶段阻断明显契约回归。
  - _Requirements: 9.3, 9.4_

- [ ] 11. 发布链路安全化
  - 重构 `scripts/release.ps1`：默认禁止 `git reset --hard`，改为“工作区必须干净”预检。
  - 对版本号与 tag 一致性增加自动校验。
  - 在发布文档中明确“需要 clean tree”的前置条件。
  - _Requirements: 9.3, 9.4_

## Phase 2（能力扩展，待启动）

- [ ] 12. 在保持 Code Mode 聚焦前提下，评估 legacy/full 工具重引入策略
  - 先定义评估标准：token 成本、稳定性、复用价值。
  - 对 legacy 工具按“可维护性 + 契约清晰度”分级。
  - 仅将通过评估的能力纳入默认路径或独立模式。
  - _Requirements: 6.1, 6.4, 8.1, 8.3_
