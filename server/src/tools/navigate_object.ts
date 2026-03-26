import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerNavigateObjectTool(server: McpServer) {
  server.tool(
    "navigate_object",
    "Open a complex RevitLookup-style value handle as the next browsable object layer.",
    {
      valueHandle: z
        .string()
        .min(1)
        .describe("Opaque value handle returned by expand_members."),
      limitGroups: z
        .number()
        .int()
        .min(1)
        .max(100)
        .optional()
        .default(10)
        .describe("Maximum number of declaring-type groups to return."),
      limitMembersPerGroup: z
        .number()
        .int()
        .min(1)
        .max(200)
        .optional()
        .default(12)
        .describe("Maximum preview members to return for each declaring-type group."),
      tokenBudgetHint: z
        .number()
        .int()
        .min(1)
        .optional()
        .describe("Optional token budget hint for server-side truncation."),
    },
    async (args) => {
      const params = {
        valueHandle: args.valueHandle,
        limitGroups: args.limitGroups,
        limitMembersPerGroup: args.limitMembersPerGroup,
        tokenBudgetHint: args.tokenBudgetHint,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("navigate_object", params);
        });

        return {
          content: [{ type: "text", text: JSON.stringify(response, null, 2) }],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `navigate_object failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
