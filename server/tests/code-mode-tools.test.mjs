import test from "node:test";
import assert from "node:assert/strict";

import { registerExecuteTool } from "../build/tools/execute.js";
import { registerSearchTool } from "../build/tools/search.js";
import { registerTools } from "../build/tools/register.js";

class FakeServer {
  constructor() {
    this.tools = [];
  }

  tool(name, description, schema, handler) {
    this.tools.push({ name, description, schema, handler });
  }
}

test("execute tool guidance keeps execute as the first step", async () => {
  const server = new FakeServer();

  registerExecuteTool(server);

  const executeTool = server.tools.find((tool) => tool.name === "execute");
  assert.ok(executeTool);
  assert.match(executeTool.description, /always attempt one read-only c# execution before search/i);
  assert.match(executeTool.schema.code.description, /filling in the body of a pre-wrapped C# method/i);
  assert.match(executeTool.schema.code.description, /public static object Execute\(Document document, UIApplication uiApp, object\[] parameters\)/i);
  assert.match(executeTool.schema.code.description, /var doc = document;/i);
  assert.match(executeTool.schema.code.description, /do not redeclare doc\/uidoc\/app\/uiapp\/application/i);
});

test("search tool payload tells the agent to try execute first", async () => {
  const server = new FakeServer();

  registerSearchTool(server);

  const searchTool = server.tools.find((tool) => tool.name === "search");
  assert.ok(searchTool);
  assert.match(searchTool.description, /never use this as the first step/i);

  const response = await searchTool.handler({
    query: "how to get first wall",
    limit: 1,
  });
  const payload = JSON.parse(response.content[0].text);

  assert.equal(payload.primaryTool, "execute");
  assert.match(payload.guidance, /execute must be tried first/i);
});

test("code mode registers execute before search before exec", async () => {
  const previousToolset = process.env.REVIT_MCP_TOOLSET;
  const previousLegacyToggle = process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS;
  const server = new FakeServer();

  delete process.env.REVIT_MCP_TOOLSET;
  delete process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS;

  try {
    await registerTools(server);
  } finally {
    if (previousToolset === undefined) {
      delete process.env.REVIT_MCP_TOOLSET;
    } else {
      process.env.REVIT_MCP_TOOLSET = previousToolset;
    }

    if (previousLegacyToggle === undefined) {
      delete process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS;
    } else {
      process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS = previousLegacyToggle;
    }
  }

  assert.deepEqual(
    server.tools.slice(0, 3).map((tool) => tool.name),
    ["execute", "search", "exec"]
  );
});
