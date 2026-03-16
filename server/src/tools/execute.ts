import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Primary Code Mode tool. Execute agent-authored C# inside Revit. Prefer this first for queries and analysis; use search only when a Revit API detail is missing.",
    {
      code: z
        .string()
        .min(1)
        .describe(
          "C# method-body code to execute inside Revit. Prefer a complete read-only query snippet that returns a scalar, object, or collection."
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
                      : "execute is the primary Code Mode path for read-only inspection and analysis.",
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
              text: `Execute failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
