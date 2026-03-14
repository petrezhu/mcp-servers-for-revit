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

By default, the server starts in `Code Mode` and only registers the Phase 1 entrypoints:

| Tool | Description |
| ---- | ----------- |
| `search` | Search the prebuilt Revit API index for Code Mode guidance |
| `execute` | Execute generated C# through the Revit bridge |

To temporarily restore the legacy tool surface, start the server with either:

```bash
REVIT_MCP_TOOLSET=full npx -y mcp-server-for-revit
```

or

```bash
REVIT_MCP_ENABLE_LEGACY_TOOLS=true npx -y mcp-server-for-revit
```

That enables the original tool set plus the legacy `send_code_to_revit` alias. Both `execute` and `send_code_to_revit` forward to the plugin-side bridge command `exec`.

## Supported Tools

### Default Code Mode

| Tool | Description |
| ---- | ----------- |
| `search` | Search the prebuilt Revit API index for Code Mode guidance |
| `execute` | Execute generated C# through the Revit bridge |

### Legacy Full Mode

`REVIT_MCP_TOOLSET=full` restores the original tool surface, including:

- `get_current_view_info`
- `get_current_view_elements`
- `get_available_family_types`
- `get_selected_elements`
- `get_material_quantities`
- `ai_element_filter`
- `analyze_model_statistics`
- `create_point_based_element`
- `create_line_based_element`
- `create_surface_based_element`
- `create_grid`
- `create_level`
- `create_room`
- `create_dimensions`
- `create_structural_framing_system`
- `delete_element`
- `operate_element`
- `color_elements`
- `tag_all_walls`
- `tag_all_rooms`
- `export_room_data`
- `store_project_data`
- `store_room_data`
- `query_stored_data`
- `send_code_to_revit`
- `say_hello`

## Smoke Test

Use `execute` for the Phase 1 end-to-end bridge check. The code is inserted directly into the generated `Execute(Document document, object[] parameters)` method body, so a minimal smoke payload is:

```csharp
TaskDialog.Show("Revit MCP", "Hello Revit");
return new { message = "Hello Revit" };
```

Expected result:

- Revit shows a `Hello Revit` dialog.
- The MCP tool returns a success payload from the `execute` MCP tool while the plugin bridge executes `exec`.

## Development

```bash
npm install
npm run build
```

## License

[MIT](https://github.com/mcp-servers-for-revit/mcp-servers-for-revit/blob/main/LICENSE)
