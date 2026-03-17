# mcp-server-for-revit

MCP server for interacting with Autodesk Revit through AI assistants like Claude.

This package is the MCP server component of [mcp-servers-for-revit](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit). It exposes Revit operations as MCP tools that AI clients can call. The server communicates with the [Revit plugin](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit) over WebSocket to execute commands inside Revit.

> [!NOTE]
> This server requires the mcp-servers-for-revit Revit plugin to be installed and running inside Revit. See the [full project README](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit) for setup instructions.

## Setup

**Claude Code**

```bash
claude mcp add mcp-server-for-revit -- npx -y mcp-server-for-revit
```

**Claude Desktop**

Claude Desktop → Settings → Developer → Edit Config → `claude_desktop_config.json`:

```json
{
    "mcpServers": {
        "mcp-server-for-revit": {
            "command": "npx",
            "args": ["-y", "mcp-server-for-revit"]
        }
    }
}
```

Restart Claude Desktop. When you see the hammer icon, the MCP server is connected.

## Tool Modes

By default, the server starts in `Code Mode` and only registers the Phase 1 entrypoints. `execute` is the default path and should be tried first for almost every normal query; `search` is only a compact API gap-filler when the agent is missing one specific Revit detail after an `execute` attempt.


| Tool | Description |
| ---- | ----------- |
| `execute` | Primary Code Mode tool for running generated C# in `read_only` or `modify` mode through the Revit bridge |
| `search` | Fill Revit API knowledge gaps with short answers, snippets, and pitfalls for Code Mode |

## Supported Tools

### Default Code Mode

| Tool | Description |
| ---- | ----------- |
| `execute` | Primary Code Mode tool for running generated C# in `read_only` or `modify` mode through the Revit bridge |
| `search` | Fill Revit API knowledge gaps with short answers, snippets, and pitfalls for Code Mode |

Simple queries should be `0 x search + 1 x execute`.

Requests like "get the first wall id", "read selected elements", or "inspect the current view" should go straight to `execute` with no search preflight.

If a Revit API detail is unclear, use `search` once only after an `execute` attempt fails, then immediately continue with `execute`.

`execute` defaults to `read_only` for inspection and analysis.

Use `mode: "modify"` only after the user explicitly confirms that the model should be changed.

## Smoke Test

Use `execute` for the Phase 1 end-to-end bridge check. The code is inserted directly into the generated `Execute(Document document, object[] parameters)` method body, so a minimal smoke payload is:

```csharp
TaskDialog.Show("Revit MCP", "Hello Revit");
return new { message = "Hello Revit" };
```

Expected result:

- Revit shows a `Hello Revit` dialog.
- The MCP tool returns a success payload from the `execute` MCP tool.

## Development

```bash
npm install
npm run build
```

## License

[MIT](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/blob/main/LICENSE)
