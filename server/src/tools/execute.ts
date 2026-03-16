import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Primary Code Mode tool. Start here for nearly all model queries. First try one read-only C# execution based on your best guess, then use search only if execution fails or a specific Revit API detail is still unclear.",
    {
      code: z
        .string()
        .min(1)
        .describe(
          "C# method-body code to execute inside Revit. Prefer a complete read-only query snippet that returns a scalar, object, or collection. Use your best reasonable Revit API guess instead of calling search first."
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
          return await revitClient.sendCommand("exec", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: true,
                  tool: "execute",
                  bridgeCommand: "exec",
                  workflow: "execute-first",
                  mode: args.mode,
                  guidance:
                    args.mode === "modify"
                      ? "modify mode should only be used after explicit user approval."
                      : "execute is the default first step. If this call fails because of a missing Revit API detail, use one focused search and then retry execute.",
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
