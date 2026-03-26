import test from "node:test";
import assert from "node:assert/strict";
import net from "node:net";

import { registerTools } from "../build/tools/register.js";

class FakeServer {
  constructor() {
    this.tools = [];
  }

  tool(name, description, schema, handler) {
    this.tools.push({ name, description, schema, handler });
  }
}

function createJsonRpcResponse(id, payload) {
  return JSON.stringify({
    jsonrpc: "2.0",
    id,
    ...payload,
  });
}

async function startMockRevitBridge() {
  const requests = [];
  const server = net.createServer((socket) => {
    socket.on("data", (chunk) => {
      const request = JSON.parse(chunk.toString("utf8"));
      requests.push(request);

      switch (request.method) {
        case "selection_roots":
          socket.write(
            createJsonRpcResponse(request.id, {
              result: {
                success: true,
                data: {
                  source: "selection",
                  totalRootCount: 1,
                  truncated: false,
                  groups: [
                    {
                      groupKey: "Wall",
                      count: 1,
                      items: [
                        {
                          objectHandle: "obj:wall-1",
                          elementId: 1001,
                          title: "Basic Wall, ID1001",
                          typeName: "Wall",
                          category: "Walls",
                        },
                      ],
                    },
                  ],
                },
                completionHint: "answer_ready",
                nextBestAction: "object_member_groups",
                retryRecommended: false,
              },
            })
          );
          return;
        case "object_member_groups":
          socket.write(
            createJsonRpcResponse(request.id, {
              result: {
                success: true,
                data: {
                  objectHandle: request.params.objectHandle,
                  title: "Basic Wall, ID1001",
                  truncated: false,
                  groups: [
                    {
                      declaringTypeName: "Wall",
                      depth: 1,
                      memberCount: 2,
                      topMembers: ["Width", "Location"],
                      hasMoreMembers: false,
                    },
                  ],
                },
                completionHint: "answer_ready",
                nextBestAction: "expand_members",
                retryRecommended: false,
              },
            })
          );
          return;
        case "expand_members":
          socket.write(
            createJsonRpcResponse(request.id, {
              result: {
                success: true,
                data: {
                  objectHandle: request.params.objectHandle,
                  expanded: [
                    {
                      declaringTypeName: "Wall",
                      memberName: "Location",
                      valueKind: "object_summary",
                      canNavigate: true,
                      valueHandle: "val:location-1",
                    },
                  ],
                },
                completionHint: "answer_ready",
                nextBestAction: "navigate_object",
                retryRecommended: false,
              },
            })
          );
          return;
        case "navigate_object":
          socket.write(
            createJsonRpcResponse(request.id, {
              result: {
                success: true,
                data: {
                  valueHandle: request.params.valueHandle,
                  objectHandle: "obj:location-1",
                  truncated: false,
                  groups: [
                    {
                      declaringTypeName: "LocationCurve",
                      depth: 1,
                      memberCount: 1,
                      topMembers: ["Curve"],
                      hasMoreMembers: false,
                    },
                  ],
                },
                completionHint: "answer_ready",
                nextBestAction: "expand_members",
                retryRecommended: false,
              },
            })
          );
          return;
        case "exec":
          socket.write(
            createJsonRpcResponse(request.id, {
              error: {
                code: -32601,
                message: "Method 'exec' not found",
              },
            })
          );
          return;
        case "execute":
          socket.write(
            createJsonRpcResponse(request.id, {
              result: {
                Success: true,
                Result: JSON.stringify({ ok: true }),
                CompletionHint: "answer_ready",
                NextBestAction: "respond_to_user",
                RetryRecommended: false,
              },
            })
          );
          return;
        default:
          socket.write(
            createJsonRpcResponse(request.id, {
              error: {
                code: -32601,
                message: `Method '${request.method}' not found`,
              },
            })
          );
      }
    });
  });

  await new Promise((resolve, reject) => {
    server.listen(0, "127.0.0.1", () => resolve());
    server.once("error", reject);
  });

  const address = server.address();
  if (!address || typeof address === "string") {
    throw new Error("Failed to resolve mock Revit bridge address");
  }

  return {
    host: "127.0.0.1",
    port: address.port,
    requests,
    close: () =>
      new Promise((resolve, reject) => {
        server.close((error) => {
          if (error) {
            reject(error);
            return;
          }

          resolve();
        });
      }),
  };
}

async function withCodeModeServer(run) {
  const bridge = await startMockRevitBridge();
  const previousHost = process.env.REVIT_MCP_HOST;
  const previousPort = process.env.REVIT_MCP_PORT;
  const previousToolset = process.env.REVIT_MCP_TOOLSET;
  const previousLegacyToggle = process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS;
  const server = new FakeServer();

  process.env.REVIT_MCP_HOST = bridge.host;
  process.env.REVIT_MCP_PORT = String(bridge.port);
  delete process.env.REVIT_MCP_TOOLSET;
  delete process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS;

  try {
    await registerTools(server);
    await run({ bridge, server });
  } finally {
    if (previousHost === undefined) {
      delete process.env.REVIT_MCP_HOST;
    } else {
      process.env.REVIT_MCP_HOST = previousHost;
    }

    if (previousPort === undefined) {
      delete process.env.REVIT_MCP_PORT;
    } else {
      process.env.REVIT_MCP_PORT = previousPort;
    }

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

    await bridge.close();
  }
}

test("inspection workflow uses connect-rvtLookup tools without requiring execute", async () => {
  await withCodeModeServer(async ({ bridge, server }) => {
    const selectionRootsTool = server.tools.find((tool) => tool.name === "selection_roots");
    const objectMemberGroupsTool = server.tools.find((tool) => tool.name === "object_member_groups");
    const expandMembersTool = server.tools.find((tool) => tool.name === "expand_members");
    const navigateObjectTool = server.tools.find((tool) => tool.name === "navigate_object");

    assert.ok(selectionRootsTool);
    assert.ok(objectMemberGroupsTool);
    assert.ok(expandMembersTool);
    assert.ok(navigateObjectTool);

    const rootsResponse = await selectionRootsTool.handler({});
    const rootsPayload = JSON.parse(rootsResponse.content[0].text);
    const objectHandle = rootsPayload.data.groups[0].items[0].objectHandle;

    const groupsResponse = await objectMemberGroupsTool.handler({ objectHandle });
    const groupsPayload = JSON.parse(groupsResponse.content[0].text);
    assert.equal(groupsPayload.data.objectHandle, objectHandle);

    const expandResponse = await expandMembersTool.handler({
      objectHandle,
      members: [{ declaringTypeName: "Wall", memberName: "Location" }],
    });
    const expandPayload = JSON.parse(expandResponse.content[0].text);
    const valueHandle = expandPayload.data.expanded[0].valueHandle;

    const navigateResponse = await navigateObjectTool.handler({ valueHandle });
    const navigatePayload = JSON.parse(navigateResponse.content[0].text);
    assert.equal(navigatePayload.data.objectHandle, "obj:location-1");

    assert.deepEqual(
      bridge.requests.map((request) => request.method),
      ["selection_roots", "object_member_groups", "expand_members", "navigate_object"]
    );
  });
});

test("inspection tools coexist with execute fallback", async () => {
  await withCodeModeServer(async ({ bridge, server }) => {
    const selectionRootsTool = server.tools.find((tool) => tool.name === "selection_roots");
    const executeTool = server.tools.find((tool) => tool.name === "execute");

    assert.ok(selectionRootsTool);
    assert.ok(executeTool);

    await selectionRootsTool.handler({});
    const executeResponse = await executeTool.handler({
      code: "return new { ok = true };",
      parameters: [],
      mode: "read_only",
    });

    const executePayload = JSON.parse(executeResponse.content[0].text);
    assert.equal(executePayload.success, true);
    assert.equal(executePayload.tool, "execute");

    assert.deepEqual(
      bridge.requests.map((request) => request.method),
      ["selection_roots", "exec", "execute"]
    );
  });
});
