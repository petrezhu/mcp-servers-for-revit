import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { sendCodeExecutionCommand } from "./codeExecution.js";

const EXECUTION_WRAPPER_GUIDANCE = [
  "You are filling in the body of a pre-wrapped C# method, not writing a full standalone program.",
  "The bridge wraps your snippet like:",
  "using System;",
  "using System.Linq;",
  "using System.Collections.Generic;",
  "using Autodesk.Revit.DB;",
  "using Autodesk.Revit.UI;",
  "",
  "namespace AIGeneratedCode",
  "{",
  "    public static class CodeExecutor",
  "    {",
  "        public static object Execute(Document document, UIApplication uiApp, object[] parameters)",
  "        {",
  "            // document: Autodesk.Revit.DB.Document",
  "            // uiApp: Autodesk.Revit.UI.UIApplication",
  "            // parameters: object[]",
  "            // You may create local aliases such as:",
  "            // var doc = document;",
  "            // var uidoc = uiApp?.ActiveUIDocument;",
  "            // var app = uiApp;",
  "            // var uiapp = uiApp;",
  "            // var application = document?.Application;",
  "",
  "            // your snippet is inserted here",
  "        }",
  "    }",
  "}",
  "No local aliases are predeclared for you. Define any aliases yourself if you want them.",
  "Return a scalar, object, or collection from the snippet body.",
].join("\n");

type BridgeError = {
  type?: string;
  errorCode?: string;
  diagnostics?: unknown[];
  retrySuggested?: boolean;
  suggestedFix?: string;
};

type BridgeExecutionResult = {
  Success?: boolean;
  ErrorMessage?: string;
  Error?: BridgeError;
  CompletionHint?: string;
  NextBestAction?: string;
  RetryRecommended?: boolean;
};

function asBridgeExecutionResult(value: unknown): BridgeExecutionResult | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  return value as BridgeExecutionResult;
}

function normalizeExecutionSignals(response: unknown) {
  const bridge = asBridgeExecutionResult(response);
  const executionSucceeded = bridge?.Success !== false;
  const error = bridge?.Error ?? null;

  const completionHint =
    bridge?.CompletionHint ??
    (executionSucceeded ? "answer_ready" : "partial");
  const nextBestAction =
    bridge?.NextBestAction ??
    (executionSucceeded ? "respond_to_user" : "retry_execute");
  const retryRecommended =
    bridge?.RetryRecommended ??
    (error?.retrySuggested ?? !executionSucceeded);

  return {
    success: executionSucceeded,
    completionHint,
    nextBestAction,
    retryRecommended,
    error,
    errorMessage: bridge?.ErrorMessage ?? null,
  };
}

function classifyTransportError(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  const normalized = message.toLowerCase();

  if (/method\s+'(?:exec|execute)'\s+not\s+found/i.test(message)) {
    return {
      errorCode: "ERR_RPC_METHOD_NOT_FOUND",
      retrySuggested: false,
      nextBestAction: "respond_to_user",
      suggestedFix:
        "Bridge command not found. Ensure plugin commandset is loaded and supports `execute`/`exec` aliases.",
    };
  }

  if (normalized.includes("timed out")) {
    return {
      errorCode: "ERR_EXECUTION_TIMEOUT",
      retrySuggested: true,
      nextBestAction: "retry_execute",
      suggestedFix:
        "Execution timed out. Simplify the snippet, reduce traversal scope, and retry once.",
    };
  }

  if (
    normalized.includes("connect to revit client failed") ||
    normalized.includes("连接到revit客户端失败") ||
    normalized.includes("econnrefused")
  ) {
    return {
      errorCode: "ERR_RPC_CONNECTION_FAILED",
      retrySuggested: true,
      nextBestAction: "retry_execute",
      suggestedFix:
        "Check whether Revit plugin bridge is running and port/host settings are correct, then retry.",
    };
  }

  return {
    errorCode: "ERR_RPC_CONNECTION_FAILED",
    retrySuggested: true,
    nextBestAction: "retry_execute",
    suggestedFix: "Retry after verifying bridge connectivity.",
  };
}

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Primary Code Mode execution tool for model/element tasks. Use execute-first for project data queries like 'get first wall id', selected elements, or current view info. For runtime API/member discovery tasks (e.g., checking whether ElementId has Value or IntegerValue), use lookup_engine_query first as a parallel path.",
    {
      code: z.string().min(1).describe(EXECUTION_WRAPPER_GUIDANCE),
      parameters: z
        .array(z.any())
        .optional()
        .default([])
        .describe("Optional parameters passed through to the Revit bridge command."),
      mode: z
        .enum(["read_only", "modify"])
        .optional()
        .default("read_only")
        .describe(
          "Execution mode. Default to `read_only` for safe querying. Use `modify` only after the user explicitly confirms model changes."
        ),
    },
    async (args) => {
      const params = {
        code: args.code,
        parameters: args.parameters ?? [],
        mode: args.mode,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await sendCodeExecutionCommand(revitClient, params);
        });
        const signals = normalizeExecutionSignals(response);

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: signals.success,
                  tool: "execute",
                  bridgeCommand: "execute",
                  workflow: "execute-first",
                  mode: args.mode,
                  completionHint: signals.completionHint,
                  nextBestAction: signals.nextBestAction,
                  retryRecommended: signals.retryRecommended,
                  guidance:
                    args.mode === "modify"
                      ? "modify mode should only be used after explicit user approval."
                      : "For model/element queries, execute-first is preferred. For API/member discovery queries, use lookup_engine_query first as a parallel path. Use search only when one specific API detail remains unclear, then retry execute once.",
                  error: signals.error,
                  errorMessage: signals.errorMessage,
                  result: response,
                },
                null,
                2
              ),
            },
          ],
        };
      } catch (error) {
        const classified = classifyTransportError(error);
        const message = error instanceof Error ? error.message : String(error);
        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: false,
                  tool: "execute",
                  bridgeCommand: "execute",
                  mode: args.mode,
                  completionHint: "partial",
                  nextBestAction: classified.nextBestAction,
                  retryRecommended: classified.retrySuggested,
                  errorMessage: message,
                  error: {
                    type: "transport",
                    errorCode: classified.errorCode,
                    diagnostics: [],
                    retrySuggested: classified.retrySuggested,
                    suggestedFix: classified.suggestedFix,
                  },
                },
                null,
                2
              ),
            },
          ],
          isError: true,
        };
      }
    }
  );
}
