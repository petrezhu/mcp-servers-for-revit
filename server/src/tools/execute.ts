import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Execute generated C# code inside the Revit plugin through the existing transport bridge. Phase 1 keeps compatibility by forwarding to the legacy send_code_to_revit command.",
    {
      code: z.string().min(1).describe("C# code to execute inside Revit."),
      parameters: z.array(z.any()).optional().default([]).describe("Optional parameters passed through to the Revit command."),
      mode: z.enum(["read_only", "legacy"]).optional().default("read_only").describe("Execution mode hint. Current Phase 1 implementation forwards through the legacy command path."),
    },
    async (args) => {
      const params = {
        code: args.code,
        parameters: args.parameters ?? [],
        mode: args.mode,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("send_code_to_revit", params);
        });

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: true,
                  forwardedCommand: "send_code_to_revit",
                  phase: "phase-1-compatibility",
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
