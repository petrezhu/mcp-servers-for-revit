import test from "node:test";
import assert from "node:assert/strict";

import { sendCodeExecutionCommand } from "../build/tools/codeExecution.js";

test("sendCodeExecutionCommand falls back from exec to execute on method-not-found", async () => {
  const calls = [];
  const expectedResult = { ok: true };
  const revitClient = {
    async sendCommand(command, params) {
      calls.push({ command, params });

      if (command === "exec") {
        throw new Error("Method 'exec' not found");
      }

      return expectedResult;
    },
  };
  const params = { code: "return 1;", mode: "read_only" };

  const result = await sendCodeExecutionCommand(revitClient, params);

  assert.deepEqual(result, expectedResult);
  assert.deepEqual(calls, [
    { command: "exec", params },
    { command: "execute", params },
  ]);
});

test("sendCodeExecutionCommand falls back on 'Method not found: exec' message shape", async () => {
  const calls = [];
  const expectedResult = { ok: true };
  const revitClient = {
    async sendCommand(command, params) {
      calls.push({ command, params });

      if (command === "exec") {
        throw new Error("Method not found: 'exec'");
      }

      return expectedResult;
    },
  };
  const params = { code: "return 1;", mode: "read_only" };

  const result = await sendCodeExecutionCommand(revitClient, params);

  assert.deepEqual(result, expectedResult);
  assert.deepEqual(calls, [
    { command: "exec", params },
    { command: "execute", params },
  ]);
});

test("sendCodeExecutionCommand does not hide non-method-not-found errors", async () => {
  const revitClient = {
    async sendCommand() {
      throw new Error("Socket disconnected");
    },
  };

  await assert.rejects(
    () => sendCodeExecutionCommand(revitClient, { code: "return 1;" }),
    /Socket disconnected/
  );
});
