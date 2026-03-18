import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { sendCodeExecutionCommand } from "./codeExecution.js";
import { RevitClientConnection } from "../utils/SocketClient.js";

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
  CompletionHint?: string;
  NextBestAction?: string;
  RetryRecommended?: boolean;
};

type LookupRequest = {
  query: string;
  limit: number;
  includeMembers: boolean;
};

const LOOKUP_ENGINE_COMMAND = "lookup_engine_query";

const LOOKUP_ENGINE_FALLBACK_PROBE = [
  "using System.Reflection;",
  "var query = (parameters != null && parameters.Length > 0 ? Convert.ToString(parameters[0]) : string.Empty) ?? string.Empty;",
  "var normalized = query.Trim().ToLowerInvariant();",
  "var limit = 5;",
  "if (parameters != null && parameters.Length > 1 && parameters[1] != null)",
  "{",
  "    int parsedLimit;",
  "    if (int.TryParse(parameters[1].ToString(), out parsedLimit) && parsedLimit > 0)",
  "    {",
  "        limit = parsedLimit;",
  "    }",
  "}",
  "var includeMembers = true;",
  "if (parameters != null && parameters.Length > 2 && parameters[2] != null)",
  "{",
  "    bool parsedIncludeMembers;",
  "    if (bool.TryParse(parameters[2].ToString(), out parsedIncludeMembers))",
  "    {",
  "        includeMembers = parsedIncludeMembers;",
  "    }",
  "}",
  "var dbAssembly = typeof(Autodesk.Revit.DB.Element).Assembly;",
  "var uiAssembly = typeof(Autodesk.Revit.UI.UIApplication).Assembly;",
  "var allTypes = dbAssembly.GetTypes()",
  "    .Concat(uiAssembly.GetTypes())",
  "    .Where(type => type != null && type.IsPublic)",
  "    .GroupBy(type => type.FullName ?? type.Name)",
  "    .Select(group => group.First())",
  "    .ToList();",
  "var matchedTypes = allTypes",
  "    .Select(type => new",
  "    {",
  "        type,",
  "        fullName = type.FullName ?? type.Name,",
  "        name = type.Name,",
  "        namespaceName = type.Namespace ?? string.Empty",
  "    })",
  "    .Where(item =>",
  "        string.IsNullOrWhiteSpace(normalized) ||",
  "        item.fullName.ToLowerInvariant().Contains(normalized) ||",
  "        item.name.ToLowerInvariant().Contains(normalized))",
  "    .OrderBy(item => item.fullName.Length)",
  "    .ThenBy(item => item.fullName)",
  "    .Take(limit)",
  "    .ToList();",
  "var results = matchedTypes.Select(item =>",
  "{",
  "    var memberList = includeMembers",
  "        ? item.type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)",
  "            .Where(member =>",
  "                member.MemberType == MemberTypes.Property ||",
  "                member.MemberType == MemberTypes.Method ||",
  "                member.MemberType == MemberTypes.Field)",
  "            .Select(member => member.MemberType.ToString() + \":\" + member.Name)",
  "            .Distinct()",
  "            .Take(12)",
  "            .ToList()",
  "        : new List<string>();",
  "    var kind = item.type.IsEnum",
  "        ? \"enum\"",
  "        : item.type.IsInterface",
  "            ? \"interface\"",
  "            : item.type.IsValueType",
  "                ? \"struct\"",
  "                : \"class\";",
  "    return new",
  "    {",
  "        fullName = item.fullName,",
  "        name = item.name,",
  "        namespaceName = item.namespaceName,",
  "        kind,",
  "        members = memberList",
  "    };",
  "}).ToList();",
  "return new",
  "{",
  "    query,",
  "    matchedCount = results.Count,",
  "    runtimeSource = \"revit-runtime-reflection-fallback\",",
  "    assemblyVersions = new",
  "    {",
  "        revitDb = dbAssembly.GetName().Version?.ToString(),",
  "        revitUi = uiAssembly.GetName().Version?.ToString()",
  "    },",
  "    results",
  "};",
].join("\n");

function isMethodNotFoundError(error: unknown, method: string): boolean {
  if (!(error instanceof Error)) {
    return false;
  }

  const escapedMethod = method.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  return new RegExp(`method\\s+'${escapedMethod}'\\s+not\\s+found`, "i").test(
    error.message
  );
}

function asBridgeExecutionResult(value: unknown): BridgeExecutionResult | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  return value as BridgeExecutionResult;
}

function parseBridgeExecutePayload(response: unknown): unknown {
  const bridge = asBridgeExecutionResult(response);
  if (!bridge || bridge.Success !== true) {
    return null;
  }

  if (bridge.Result && typeof bridge.Result === "object") {
    return bridge.Result;
  }

  if (typeof bridge.Result === "string") {
    try {
      return JSON.parse(bridge.Result);
    } catch {
      return null;
    }
  }

  return null;
}

function classifyTransportError(error: unknown) {
  const message = error instanceof Error ? error.message : String(error);
  const normalized = message.toLowerCase();

  if (/method\s+'(?:lookup_engine_query|exec|execute)'\s+not\s+found/i.test(message)) {
    return {
      errorCode: "ERR_RPC_METHOD_NOT_FOUND",
      retrySuggested: false,
      nextBestAction: "respond_to_user",
      suggestedFix:
        "Bridge command not found. Ensure plugin commandset is loaded and exposes `lookup_engine_query`.",
    };
  }

  if (normalized.includes("timed out")) {
    return {
      errorCode: "ERR_EXECUTION_TIMEOUT",
      retrySuggested: true,
      nextBestAction: "retry_execute",
      suggestedFix: "Lookup probe timed out. Retry once with a narrower query.",
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

async function sendLookupEngineQueryCommand(
  revitClient: RevitClientConnection,
  request: LookupRequest
) {
  try {
    const directResponse = await revitClient.sendCommand(LOOKUP_ENGINE_COMMAND, request);
    return {
      source: "lookup_engine",
      bridgeResult: directResponse as unknown,
      success: true,
      payload: directResponse as unknown,
      error: null as BridgeError | null,
      errorMessage: null as string | null,
      retryRecommended: false,
    };
  } catch (error) {
    if (!isMethodNotFoundError(error, LOOKUP_ENGINE_COMMAND)) {
      throw error;
    }

    const fallbackResponse = await sendCodeExecutionCommand(revitClient, {
      code: LOOKUP_ENGINE_FALLBACK_PROBE,
      parameters: [request.query, request.limit, request.includeMembers],
      mode: "read_only",
    });

    const bridge = asBridgeExecutionResult(fallbackResponse);
    const payload = parseBridgeExecutePayload(fallbackResponse);
    const fallbackSuccess = bridge?.Success === true && payload !== null;
    const retryRecommended =
      bridge?.RetryRecommended ??
      bridge?.Error?.retrySuggested ??
      !fallbackSuccess;

    return {
      source: "runtime_reflection_fallback",
      bridgeResult: fallbackResponse as unknown,
      success: fallbackSuccess,
      payload,
      error: bridge?.Error ?? null,
      errorMessage:
        bridge?.ErrorMessage ??
        (fallbackSuccess
          ? null
          : "Failed to parse lookup payload from execute fallback response."),
      retryRecommended,
    };
  }
}

export function registerLookupEngineQueryTool(server: McpServer) {
  server.tool(
    "lookup_engine_query",
    "Runtime Revit API lookup tool powered by lookup engine. This is a parallel path to execute-first: for API/member query tasks, use lookup_engine_query first, then patch code and retry execute once.",
    {
      query: z
        .string()
        .min(1)
        .describe(
          "Type or member keyword to inspect from the live Revit runtime, such as 'ElementId', 'Wall', or 'UnitTypeId'."
        ),
      limit: z
        .number()
        .int()
        .min(1)
        .max(10)
        .optional()
        .default(5)
        .describe("Maximum number of matching API types to return."),
      includeMembers: z
        .boolean()
        .optional()
        .default(true)
        .describe("Whether to include top members (methods/properties/fields) for each matched type."),
    },
    async (args) => {
      try {
        const result = await withRevitConnection(async (revitClient) => {
          return await sendLookupEngineQueryCommand(revitClient, {
            query: args.query,
            limit: args.limit,
            includeMembers: args.includeMembers,
          });
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: result.success,
                  tool: "lookup_engine_query",
                  source: result.source,
                  completionHint: "partial",
                  nextBestAction: "retry_execute",
                  retryRecommended: result.retryRecommended,
                  postLookupAction: "retry_execute_once",
                  runtimeVersionMatched: true,
                  guidance:
                    "For API/member queries, lookup_engine_query-first is preferred. Use returned members/types to patch your snippet, then retry execute once.",
                  errorMessage: result.errorMessage,
                  error: result.error,
                  result: result.payload,
                  bridgeResult: result.bridgeResult,
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
                  tool: "lookup_engine_query",
                  source: "lookup_engine",
                  completionHint: "partial",
                  nextBestAction: classified.nextBestAction,
                  retryRecommended: classified.retrySuggested,
                  postLookupAction: "retry_execute_once",
                  runtimeVersionMatched: false,
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
