# RevitLookup-Driven Token-Efficient Query Spec

- Status: Draft
- Scope: `mcp-servers-for-revit`
- Updated: 2026-03-26

## 1. Summary

This spec defines how `mcp-servers-for-revit` should reuse RevitLookup's existing decomposition model to let AI callers inspect complex Revit elements with lower token usage.

The core decision is:

- Reuse RevitLookup's object organization, member grouping, and descriptor mapping as the browsing kernel.
- Stop building a parallel custom hierarchy for element classification and deep value formatting.
- Add a thin MCP-friendly query layer that returns compact summaries first, then expands only on demand.

The target interaction model is:

1. Get root objects from selection or current view.
2. Group roots by runtime type.
3. Open one root object and inspect member groups by declaring type.
4. Expand only selected members.
5. Navigate complex member values as next-layer objects only when necessary.

This follows the same mental model already implemented by RevitLookup instead of inventing a new static tree like `Selection -> Element -> Parameters -> ...`.

## 2. Background

Current AI-oriented querying in this repo has two problems:

1. `execute` is flexible but expensive.
   The caller often needs to generate dynamic C# and return large serialized payloads, which increases prompt size, runtime risk, and token consumption.

2. Existing element-returning commands are too shallow or too eager.
   `ElementInfo` and view/selection access commands either return too little structure for complex reasoning or encourage broad payloads without a layered retrieval strategy.

At the same time, RevitLookup already implements a strong object inspection model:

- root object collection from selection/current view
- grouping of roots by runtime type
- grouping of members by declaring type along the inheritance chain
- descriptor-based remapping of complex values into navigable variants

This spec treats those capabilities as the canonical source of truth for inspection structure.

## 3. Problem Statement

AI callers need to answer questions about complex BIM elements, but full object dumps are too expensive. We need a query path that:

- avoids sending full parameter tables and geometry by default
- avoids reimplementing element hierarchy classification in `mcp-servers-for-revit`
- supports progressive disclosure for complex values
- remains stable across Revit element types because it rides on RevitLookup's decomposition model

## 4. Goals

- Reuse RevitLookup's decomposition flow for object inspection.
- Reduce token usage for common model-inspection tasks.
- Provide deterministic MCP commands with compact structured output.
- Support progressive, multi-hop navigation instead of full recursive dumps.
- Preserve access to deep inspection when explicitly requested.

## 5. Non-Goals

- Replacing RevitLookup UI.
- Replacing the generic `execute` tool for all use cases.
- Creating a brand-new handcrafted taxonomy for all element classes.
- Returning every parameter, property, and geometry payload in a single response.
- Designing a static recursive tree detached from RevitLookup's actual runtime browsing behavior.

## 6. Existing RevitLookup Behavior To Reuse

### 6.1 Root objects

`Snoop Selection` ultimately resolves roots through `KnownDecompositionObject.Selection`. In `RevitObjectsCollector`, when a selection exists, each selected `ElementId` is resolved to an `Element` and returned directly. If nothing is selected, the collector falls back to elements in the active view.

Design implication:

- There is no mandatory top-level synthetic `Selection` container.
- The real roots are the selected `Element` instances themselves.
- Our MCP query model should preserve this behavior.

### 6.2 Left-side grouping

RevitLookup's summary view groups root objects by `TypeName`, then displays concrete objects under each runtime type group.

Design implication:

- We should reuse runtime type grouping instead of building and maintaining our own classification tree such as walls, doors, pipes, and so on.
- Runtime type grouping is the default root-list organization for MCP inspection results.

### 6.3 Right-side member grouping

When a root object is selected, its members are decomposed lazily. LookupEngine walks the inheritance chain and records member metadata such as `DeclaringTypeName` and `Depth`. The UI then groups the member grid by the declaring type.

Design implication:

- We should not flatten all members into one list by default.
- The default detail view should be:
  `object -> declaring type group -> members`

### 6.4 Descriptor remapping

RevitLookup routes values through `DescriptorsMap.FindDescriptor`, allowing special handling for types like `Element`, `Parameter`, `Reference`, `GeometryObject`, and more. In `ElementDescriptor`, values such as `Geometry`, `BoundingBox`, and material-related calls are remapped into navigable variants better suited for browsing.

Design implication:

- We should not reimplement deep formatting logic for geometry, references, parameters, or material collections unless necessary.
- Our query layer should consume descriptor-backed results and emit compact summaries plus navigation handles.

## 7. Design Principles

- Summary first, expansion later.
- Reuse runtime structure instead of inventing a parallel hierarchy.
- Return structured JSON, not narrative explanations.
- Prefer references and navigation handles over deep inline payloads.
- Keep complex values navigable but not eagerly expanded.
- Make token budget an explicit server-side concern.

## 8. Proposed Architecture

We introduce a thin AI query layer above RevitLookup's decomposition engine.

### 8.1 Layering

1. RevitLookup layer
   Responsible for object collection, decomposition, inheritance-aware member grouping, and descriptor remapping.

2. MCP query adapter layer
   Responsible for:
   - compact projection of root lists
   - member group summaries
   - targeted member expansion
   - navigation handles
   - token-budget-based truncation

3. AI caller
   Responsible for deciding which object to open, which member groups to inspect, and which values to expand next.

### 8.2 Interaction model

Instead of one large query, the caller moves through small structured steps:

1. `selection_roots`
2. `object_member_groups`
3. `expand_members`
4. `navigate_object`

This mirrors RevitLookup's real browsing behavior and keeps most requests small.

## 9. Query Model

### 9.1 Step 1: root discovery

Return the current set of root objects.

Default organization:

- `groupKey = runtime TypeName`
- each group contains a compact object list

Each object record should include only the minimum identity fields:

- `objectHandle`
- `elementId`
- `uniqueId` when available
- `title`
- `typeName`
- `category`

`title` should follow RevitLookup descriptor naming conventions when possible, such as `Name, ID12345`.

### 9.2 Step 2: member-group discovery

Given one `objectHandle`, return grouped member metadata rather than values.

Each group should include:

- `declaringTypeName`
- `depth`
- `memberCount`
- `topMembers`

`topMembers` is a capped preview list for fast caller orientation.

### 9.3 Step 3: targeted member expansion

Given one object and a list of requested members, return only those member values.

Returned values should be:

- scalar summary values for simple primitives
- compact value summaries for complex values
- navigation handles for deep objects or collections

### 9.4 Step 4: next-layer navigation

If a returned value is navigable, the caller can open it as a new object page using a `valueHandle` or `objectHandle`.

The same shape repeats:

- grouped members by declaring type
- targeted expansion only
- deeper navigation only on demand

## 10. Proposed MCP Commands

These commands are proposed additions. Names may be adjusted during implementation, but the interaction pattern should remain stable.

### 10.1 `selection_roots`

Purpose:

- expose RevitLookup-style root objects for current selection
- fall back to current-view elements when selection is empty

Input:

```json
{
  "source": "selection_or_active_view",
  "limitGroups": 20,
  "limitItemsPerGroup": 20,
  "tokenBudgetHint": 2000
}
```

Output:

```json
{
  "source": "selection_or_active_view",
  "totalRootCount": 42,
  "truncated": true,
  "groups": [
    {
      "groupKey": "Wall",
      "count": 12,
      "items": [
        {
          "objectHandle": "obj:wall:12345",
          "elementId": 12345,
          "uniqueId": "....",
          "title": "Basic Wall, ID12345",
          "typeName": "Wall",
          "category": "Walls"
        }
      ]
    }
  ]
}
```

### 10.2 `object_member_groups`

Purpose:

- return the inheritance-aware member directory for one object

Input:

```json
{
  "objectHandle": "obj:wall:12345",
  "limitGroups": 10,
  "limitMembersPerGroup": 12,
  "tokenBudgetHint": 1500
}
```

Output:

```json
{
  "objectHandle": "obj:wall:12345",
  "title": "Basic Wall, ID12345",
  "groups": [
    {
      "declaringTypeName": "Element",
      "depth": 1,
      "memberCount": 87,
      "topMembers": ["Id", "Name", "Category", "BoundingBox", "Geometry"]
    },
    {
      "declaringTypeName": "HostObject",
      "depth": 2,
      "memberCount": 14,
      "topMembers": ["FindInserts", "GetCompoundStructure"]
    },
    {
      "declaringTypeName": "Wall",
      "depth": 3,
      "memberCount": 22,
      "topMembers": ["Width", "Orientation", "WallType"]
    }
  ]
}
```

### 10.3 `expand_members`

Purpose:

- expand only explicitly requested members

Input:

```json
{
  "objectHandle": "obj:wall:12345",
  "members": [
    { "declaringTypeName": "Wall", "memberName": "Width" },
    { "declaringTypeName": "Element", "memberName": "BoundingBox" },
    { "declaringTypeName": "Element", "memberName": "Geometry" }
  ],
  "tokenBudgetHint": 2000
}
```

Output:

```json
{
  "objectHandle": "obj:wall:12345",
  "expanded": [
    {
      "declaringTypeName": "Wall",
      "memberName": "Width",
      "valueKind": "scalar",
      "displayValue": "0.2000"
    },
    {
      "declaringTypeName": "Element",
      "memberName": "BoundingBox",
      "valueKind": "object_summary",
      "displayValue": "Model + Active view bounding boxes",
      "canNavigate": true,
      "valueHandle": "val:bbox:12345"
    },
    {
      "declaringTypeName": "Element",
      "memberName": "Geometry",
      "valueKind": "object_summary",
      "displayValue": "10 geometry variants available",
      "canNavigate": true,
      "valueHandle": "val:geometry:12345"
    }
  ]
}
```

### 10.4 `navigate_object`

Purpose:

- open a complex member value as a next-layer object

Input:

```json
{
  "valueHandle": "val:geometry:12345",
  "limitGroups": 10,
  "limitMembersPerGroup": 12,
  "tokenBudgetHint": 1500
}
```

Output shape:

- same structure as `object_member_groups`
- uses a new handle for the navigated object if needed

## 11. Data Contracts

### 11.1 Handles

The protocol should use opaque handles to avoid re-sending full context.

Required handle categories:

- `objectHandle`
- `valueHandle`

Handle requirements:

- opaque to the caller
- short and stable within a session
- enough for the server to resolve the underlying object/value

### 11.2 Value kinds

Every expanded member should report a compact kind:

- `scalar`
- `enum`
- `collection_summary`
- `object_summary`
- `null`
- `unsupported`
- `error`

### 11.3 Summary fields

For non-scalar values, return:

- `displayValue`
- `canNavigate`
- `valueHandle` when navigable
- `itemCount` for collection summaries when known

## 12. Token Optimization Strategy

### 12.1 Default output discipline

Do not return by default:

- full parameter tables
- full geometry payloads
- full recursive object graphs
- large collections of child objects

Return by default:

- grouped directories
- small previews
- compact summaries
- navigation handles

### 12.2 Budget-aware truncation

Every query should support budget-oriented truncation.

Server-side truncation order:

1. reduce items per group
2. reduce member previews
3. remove non-essential fields
4. return counts plus top-N samples only

The response should indicate truncation explicitly with fields such as:

- `truncated`
- `nextSuggestedAction`

### 12.3 Lazy value materialization

Values are only materialized when the caller names them explicitly.

This is the main mechanism for keeping the common path cheap.

### 12.4 Descriptor-first formatting

When a descriptor exists, the query layer should prefer descriptor-backed summaries over raw reflection output because descriptors already normalize many high-cost Revit objects into more navigable forms.

## 13. Why This Reduces Work For Us

This design intentionally reduces custom work in `mcp-servers-for-revit`.

We do not need to maintain:

- a custom element taxonomy for every Revit class
- a custom inheritance/member grouping engine
- custom display logic for many Revit-specific complex value types

We only need to build:

- MCP query contracts
- compact projections
- handle resolution
- truncation and caching policy

## 14. Implementation Plan

### Phase 1: root discovery

Deliverables:

- `selection_roots` command
- root grouping by runtime type
- compact object identity projection

Primary reuse:

- RevitLookup root collection semantics
- descriptor naming

### Phase 2: member-group discovery

Deliverables:

- `object_member_groups` command
- inheritance-aware grouping by `DeclaringTypeName`
- preview-only member directory

Primary reuse:

- LookupEngine decomposition output

### Phase 3: targeted value expansion

Deliverables:

- `expand_members` command
- scalar summaries
- descriptor-backed summaries for complex values

Primary reuse:

- `DescriptorsMap`
- existing descriptors such as `ElementDescriptor`

### Phase 4: navigable deep inspection

Deliverables:

- `navigate_object` command
- recursive handle-based browsing
- collection and object navigation

Primary reuse:

- RevitLookup page-to-page browsing concept

### Phase 5: token-budget and cache layer

Deliverables:

- session-scoped handle store
- cache for root lists, member directories, and expanded values
- truncation metadata and retry hints

## 15. Technical Considerations

### 15.1 Session state

Handles require session-scoped state. The server should maintain a bounded cache keyed by request session or bridge connection.

### 15.2 Stability

Because handles refer to live Revit objects or derived values, invalidation is needed when:

- the active document changes
- the selection changes, for selection-scoped roots
- the model mutates in a way that invalidates cached values

### 15.3 Fallback behavior

If RevitLookup or a descriptor path cannot resolve a value cleanly, the server may fall back to reflection-backed metadata, but should still preserve the same output contract.

### 15.4 Coexistence with `execute`

`execute` remains available for:

- custom analysis beyond the spec
- model modifications after user approval
- debugging scenarios

But the new query flow should be the preferred path for inspection and question-answering against complex model content.

## 16. Acceptance Criteria

The design is successful when the following are true:

1. Common inspection tasks can complete without using `execute`.
2. The default first response for complex selections contains summaries and directories, not full payloads.
3. Root objects are grouped by runtime type in a way consistent with RevitLookup.
4. Member groups are organized by declaring type in a way consistent with RevitLookup.
5. Complex values such as geometry and bounding boxes are navigable without being eagerly dumped.
6. The system can progressively inspect a wall, family instance, or pipe across multiple hops using opaque handles.
7. Total token use for typical inspection tasks is materially lower than the `execute`-first approach.

## 17. Example Query Flow

Question:

- "这个墙体有哪些关键属性，它的几何和包围盒怎么继续看？"

Expected tool path:

1. `selection_roots`
   Caller picks the wall object from the `Wall` group.

2. `object_member_groups`
   Caller sees groups like `Element`, `HostObject`, and `Wall`.

3. `expand_members`
   Caller requests `Wall.Width`, `Wall.Orientation`, `Element.BoundingBox`, and `Element.Geometry`.

4. `navigate_object`
   Caller opens the returned `BoundingBox` or `Geometry` handle only if needed.

This avoids returning:

- all wall members
- all geometry variants inline
- every parameter on the first hop

## 18. Open Questions

1. Should `selection_roots` expose a mode that always forces active-view roots even when a selection exists?
2. How much of RevitLookup internals can be referenced directly from `commandset` without introducing packaging or dependency friction?
3. Should `expand_members` allow method invocation with arguments in v1, or only parameterless/property-like expansion?
4. How should session handles be serialized across the MCP server and Revit plugin boundary?
5. What is the minimum telemetry needed to compare token savings against the current `execute` path?

## 19. Recommended Next Steps

1. Confirm command naming and whether these commands live in `commandset` only or also need `server`-side affordances.
2. Prototype `selection_roots` first using one selected wall and one active-view fallback path.
3. Prototype `object_member_groups` on a wall and verify that grouping matches RevitLookup's UI.
4. Add `expand_members` for a small allowlist first: `Id`, `Name`, `Category`, `BoundingBox`, `Geometry`, `WallType`, `Width`, `Orientation`.
5. Add session handles and truncation metadata before expanding coverage.
