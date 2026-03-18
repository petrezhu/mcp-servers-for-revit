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

      if (request.method === "lookup_engine_query") {
        socket.write(
          createJsonRpcResponse(request.id, {
            result: {
              query: request.params.query,
              matchedCount: 1,
              runtimeSource: "lookup_engine",
              results: [
                {
                  fullName: "Autodesk.Revit.DB.ElementId",
                  members: ["Property:Value"],
                },
              ],
            },
          })
        );
        return;
      }

      if (request.method === "exec") {
        socket.write(
          createJsonRpcResponse(request.id, {
            error: {
              code: -32601,
              message: "Method 'exec' not found",
            },
          })
        );
        return;
      }

      if (request.method === "execute") {
        socket.write(
          createJsonRpcResponse(request.id, {
            result: {
              Success: true,
              Result: JSON.stringify({
                found: true,
                wallId: 226064,
              }),
              CompletionHint: "answer_ready",
              NextBestAction: "respond_to_user",
              RetryRecommended: false,
            },
          })
        );
        return;
      }

      socket.write(
        createJsonRpcResponse(request.id, {
          error: {
            code: -32601,
            message: `Method '${request.method}' not found`,
          },
        })
      );
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

test("agent workflow: lookup_engine_query first, then execute, then stop on answer_ready", async () => {
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

    const lookupTool = server.tools.find(
      (tool) => tool.name === "lookup_engine_query"
    );
    const executeTool = server.tools.find((tool) => tool.name === "execute");
    assert.ok(lookupTool);
    assert.ok(executeTool);

    const lookupResponse = await lookupTool.handler({
      query: "ElementId",
      limit: 3,
      includeMembers: true,
    });
    assert.equal(lookupResponse.isError, undefined);

    const lookupPayload = JSON.parse(lookupResponse.content[0].text);
    assert.equal(lookupPayload.success, true);
    assert.equal(lookupPayload.tool, "lookup_engine_query");
    assert.equal(lookupPayload.nextBestAction, "retry_execute");
    assert.equal(lookupPayload.postLookupAction, "retry_execute_once");

    const executeResponse = await executeTool.handler({
      code: "return new { found = true, wallId = 226064 };",
      parameters: [],
      mode: "read_only",
    });
    assert.equal(executeResponse.isError, undefined);

    const executePayload = JSON.parse(executeResponse.content[0].text);
    assert.equal(executePayload.success, true);
    assert.equal(executePayload.tool, "execute");
    assert.equal(executePayload.completionHint, "answer_ready");
    assert.equal(executePayload.nextBestAction, "respond_to_user");
    assert.equal(executePayload.retryRecommended, false);

    assert.deepEqual(
      bridge.requests.map((request) => request.method),
      ["lookup_engine_query", "exec", "execute"]
    );
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
});
