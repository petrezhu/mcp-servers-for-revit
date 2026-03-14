import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Execute generated C# code inside Revit through the Code Mode bridge.",
    {
      code: z.string().min(1).describe("C# code to execute inside Revit."),
      parameters: z.array(z.any()).optional().default([]).describe("Optional parameters passed through to the Revit command."),
      mode: z.enum(["read_only", "legacy"]).optional().default("legacy").describe("Execution mode hint for the Revit-side executor. Defaults to legacy for compatibility with current plugin-side behavior."),
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
                  forwardedCommand: "exec",
                  phase: "phase-1-code-mode",
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
