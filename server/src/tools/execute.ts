import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { sendCodeExecutionCommand } from "./codeExecution.js";

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Primary Code Mode tool. Start here for nearly all model queries. Always attempt one read-only C# execution before search. For simple requests like 'get the first wall id', 'list selected elements', or 'read current view info', call execute directly without any search step.",
    {
      code: z
        .string()
        .min(1)
        .describe(
          "C# method-body code to execute inside Revit. The execution context already provides common Revit objects such as `doc`, so do not redeclare `doc` or invent placeholders like `RevitLookupDb.ActiveDbDocument`. Prefer a complete read-only query snippet that returns a scalar, object, or collection. Use your best reasonable Revit API guess instead of calling search first, especially for straightforward element lookup tasks."
        ),
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

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: true,
                  tool: "execute",
                  bridgeCommand: "execute",
                  workflow: "execute-first",
                  mode: args.mode,
                  guidance:
                    args.mode === "modify"
                      ? "modify mode should only be used after explicit user approval."
                      : "execute is the mandatory first step for normal queries. Only use search after this fails because of one specific missing Revit API detail, then retry execute immediately.",
                  result: response,
                },
                null,
                2
              ),
            },
          ],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `Execute failed: ${error instanceof Error ? error.message : String(error)}. If the failure is due to an unknown Revit API detail, call search once with that specific gap, then retry execute.`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
