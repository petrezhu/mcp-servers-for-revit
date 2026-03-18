import test from "node:test";
import assert from "node:assert/strict";
import net from "node:net";

import { registerExecuteTool } from "../build/tools/execute.js";

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
              echoedMode: request.params.mode,
              echoedCode: request.params.code,
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

test("execute tool falls back to legacy execute bridge command over socket", async () => {
  const bridge = await startMockRevitBridge();
  const previousHost = process.env.REVIT_MCP_HOST;
  const previousPort = process.env.REVIT_MCP_PORT;
  const server = new FakeServer();

  process.env.REVIT_MCP_HOST = bridge.host;
  process.env.REVIT_MCP_PORT = String(bridge.port);

  try {
    registerExecuteTool(server);

    const executeTool = server.tools.find((tool) => tool.name === "execute");
    assert.ok(executeTool);

    const response = await executeTool.handler({
      code: "return 42;",
      parameters: [],
      mode: "read_only",
    });

    assert.equal(response.isError, undefined);

    const payload = JSON.parse(response.content[0].text);
    assert.equal(payload.success, true);
    assert.equal(payload.tool, "execute");
    assert.equal(payload.completionHint, "answer_ready");
    assert.equal(payload.nextBestAction, "respond_to_user");
    assert.equal(payload.retryRecommended, false);
    assert.deepEqual(payload.result, {
      echoedMode: "read_only",
      echoedCode: "return 42;",
    });
    assert.deepEqual(
      bridge.requests.map((request) => request.method),
      ["exec", "execute"]
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

    await bridge.close();
  }
});
