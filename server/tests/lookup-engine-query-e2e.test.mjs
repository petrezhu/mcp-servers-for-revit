import test from "node:test";
import assert from "node:assert/strict";
import net from "node:net";

import { registerLookupEngineQueryTool } from "../build/tools/lookup_engine_query.js";

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

async function startMockRevitBridge({ onLookupEngineQuery, onExec, onExecute }) {
  const requests = [];
  const server = net.createServer((socket) => {
    socket.on("data", (chunk) => {
      const request = JSON.parse(chunk.toString("utf8"));
      requests.push(request);

      if (request.method === "lookup_engine_query" && onLookupEngineQuery) {
        socket.write(onLookupEngineQuery(request));
        return;
      }

      if (request.method === "exec" && onExec) {
        socket.write(onExec(request));
        return;
      }

      if (request.method === "execute" && onExecute) {
        socket.write(onExecute(request));
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

test("lookup_engine_query tool uses lookup engine command when bridge supports it", async () => {
  const bridgeResult = {
    query: "ElementId",
    matchedCount: 1,
    runtimeSource: "lookup_engine",
    results: [{ fullName: "Autodesk.Revit.DB.ElementId" }],
  };

  const bridge = await startMockRevitBridge({
    onLookupEngineQuery: (request) =>
      createJsonRpcResponse(request.id, {
        result: bridgeResult,
      }),
  });
  const previousHost = process.env.REVIT_MCP_HOST;
  const previousPort = process.env.REVIT_MCP_PORT;
  const server = new FakeServer();

  process.env.REVIT_MCP_HOST = bridge.host;
  process.env.REVIT_MCP_PORT = String(bridge.port);

  try {
    registerLookupEngineQueryTool(server);
    const tool = server.tools.find((item) => item.name === "lookup_engine_query");
    assert.ok(tool);

    const response = await tool.handler({
      query: "ElementId",
      limit: 3,
      includeMembers: true,
    });
    assert.equal(response.isError, undefined);

    const payload = JSON.parse(response.content[0].text);
    assert.equal(payload.success, true);
    assert.equal(payload.tool, "lookup_engine_query");
    assert.equal(payload.source, "lookup_engine");
    assert.equal(payload.postLookupAction, "retry_execute_once");
    assert.deepEqual(payload.result, bridgeResult);
    assert.deepEqual(
      bridge.requests.map((request) => request.method),
      ["lookup_engine_query"]
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

test("lookup_engine_query tool falls back to execute bridge path when lookup engine command is missing", async () => {
  const fallbackLookupResult = {
    query: "ElementId",
    matchedCount: 1,
    runtimeSource: "revit-runtime-reflection-fallback",
    results: [{ fullName: "Autodesk.Revit.DB.ElementId" }],
  };

  const bridge = await startMockRevitBridge({
    onLookupEngineQuery: (request) =>
      createJsonRpcResponse(request.id, {
        error: {
          code: -32601,
          message:
            "未找到方法: 'lookup_engine_query'\nMethod not found: 'lookup_engine_query'",
        },
      }),
    onExec: (request) =>
      createJsonRpcResponse(request.id, {
        error: {
          code: -32601,
          message: "Method not found: 'exec'",
        },
      }),
    onExecute: (request) =>
      createJsonRpcResponse(request.id, {
        result: {
          Success: true,
          Result: fallbackLookupResult,
        },
      }),
  });
  const previousHost = process.env.REVIT_MCP_HOST;
  const previousPort = process.env.REVIT_MCP_PORT;
  const server = new FakeServer();

  process.env.REVIT_MCP_HOST = bridge.host;
  process.env.REVIT_MCP_PORT = String(bridge.port);

  try {
    registerLookupEngineQueryTool(server);
    const tool = server.tools.find((item) => item.name === "lookup_engine_query");
    assert.ok(tool);

    const response = await tool.handler({
      query: "ElementId",
      limit: 3,
      includeMembers: true,
    });
    assert.equal(response.isError, undefined);

    const payload = JSON.parse(response.content[0].text);
    assert.equal(payload.success, true);
    assert.equal(payload.tool, "lookup_engine_query");
    assert.equal(payload.source, "runtime_reflection_fallback");
    assert.equal(payload.postLookupAction, "retry_execute_once");
    assert.deepEqual(payload.result, fallbackLookupResult);
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

    await bridge.close();
  }
});
