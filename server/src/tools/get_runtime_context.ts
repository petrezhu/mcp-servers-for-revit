import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { sendCodeExecutionCommand } from "./codeExecution.js";

type RuntimeContext = {
  revitVersion: string;
  runtime: string;
  apiFeatures: {
    elementIdNumericProperty: string;
    unitApi: string;
  };
  supportedCodeCommand: string[];
};

type BridgeError = {
  type?: string;
  errorCode?: string;
  diagnostics?: unknown[];
  retrySuggested?: boolean;
  suggestedFix?: string;
};

type BridgeExecutionResult = {
  Success?: boolean;
  Result?: unknown;
  ErrorMessage?: string;
  Error?: BridgeError;
};

const RUNTIME_CONTEXT_PROBE = [
  "var app = uiApp?.Application ?? document?.Application;",
  "var revitVersion = app?.VersionNumber ?? \"unknown\";",
  "var dbAssembly = typeof(Document).Assembly;",
  "var elementIdType = dbAssembly.GetType(\"Autodesk.Revit.DB.ElementId\");",
  "var elementIdNumericProperty = \"unknown\";",
  "if (elementIdType != null)",
  "{",
  "    if (elementIdType.GetProperty(\"Value\") != null)",
  "    {",
  "        elementIdNumericProperty = \"Value\";",
  "    }",
  "    else if (elementIdType.GetProperty(\"IntegerValue\") != null)",
  "    {",
  "        elementIdNumericProperty = \"IntegerValue\";",
  "    }",
  "}",
  "var unitApi = \"unknown\";",
  "if (dbAssembly.GetType(\"Autodesk.Revit.DB.UnitTypeId\") != null)",
  "{",
  "    unitApi = \"ForgeTypeId\";",
  "}",
  "else if (dbAssembly.GetType(\"Autodesk.Revit.DB.DisplayUnitType\") != null)",
  "{",
  "    unitApi = \"DisplayUnitType\";",
  "}",
  "var framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription ?? \"unknown\";",
  "var runtime = framework.IndexOf(\".NET Framework\", StringComparison.OrdinalIgnoreCase) >= 0",
  "    ? \"net48\"",
  "    : framework.IndexOf(\".NET\", StringComparison.OrdinalIgnoreCase) >= 0 ? \"net8.0\" : framework;",
  "return new",
  "{",
  "    revitVersion,",
  "    runtime,",
  "    apiFeatures = new",
  "    {",
  "        elementIdNumericProperty,",
  "        unitApi",
  "    },",
  "    supportedCodeCommand = new[] { \"execute\", \"exec\" }",
  "};",
].join("\n");

function asBridgeExecutionResult(value: unknown): BridgeExecutionResult | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  return value as BridgeExecutionResult;
}

function fallbackRuntimeContext(): RuntimeContext {
  return {
    revitVersion: "unknown",
    runtime: "unknown",
    apiFeatures: {
      elementIdNumericProperty: "unknown",
      unitApi: "unknown",
    },
    supportedCodeCommand: ["execute", "exec"],
  };
}

function parseRuntimeContext(response: unknown): RuntimeContext | null {
  const bridge = asBridgeExecutionResult(response);
  if (!bridge || bridge.Success !== true) {
    return null;
  }

  const rawResult = bridge.Result;
  if (rawResult && typeof rawResult === "object") {
    return rawResult as RuntimeContext;
  }

  if (typeof rawResult === "string") {
    try {
      return JSON.parse(rawResult) as RuntimeContext;
    } catch {
      return null;
    }
  }

  return null;
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
        "Bridge command not found. Ensure plugin commandset is loaded and supports `execute`/`exec`.",
    };
  }

  if (normalized.includes("timed out")) {
    return {
      errorCode: "ERR_EXECUTION_TIMEOUT",
      retrySuggested: true,
      nextBestAction: "retry_execute",
      suggestedFix:
        "Runtime context probe timed out. Retry once after reducing bridge load.",
    };
  }

  return {
    errorCode: "ERR_RPC_CONNECTION_FAILED",
    retrySuggested: true,
    nextBestAction: "retry_execute",
    suggestedFix:
      "Check whether Revit plugin bridge is running and port/host settings are correct, then retry.",
  };
}

export function registerGetRuntimeContextTool(server: McpServer) {
  server.tool(
    "get_runtime_context",
    "Read-only runtime probe that returns current Revit version, runtime target, and key API feature flags before generating code.",
    {},
    async () => {
      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await sendCodeExecutionCommand(revitClient, {
            code: RUNTIME_CONTEXT_PROBE,
            parameters: [],
            mode: "read_only",
          });
        });

        const parsedContext = parseRuntimeContext(response);
        const bridge = asBridgeExecutionResult(response);
        const succeeded = bridge?.Success === true && parsedContext !== null;
        const error = bridge?.Error ?? null;

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: succeeded,
                  tool: "get_runtime_context",
                  completionHint: succeeded ? "answer_ready" : "partial",
                  nextBestAction: succeeded ? "respond_to_user" : "retry_execute",
                  retryRecommended:
                    error?.retrySuggested ?? (succeeded ? false : true),
                  runtimeContext: parsedContext ?? fallbackRuntimeContext(),
                  errorMessage:
                    bridge?.ErrorMessage ??
                    (succeeded ? null : "Failed to parse runtime context from execute result."),
                  error,
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
                  tool: "get_runtime_context",
                  completionHint: "partial",
                  nextBestAction: classified.nextBestAction,
                  retryRecommended: classified.retrySuggested,
                  runtimeContext: fallbackRuntimeContext(),
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
