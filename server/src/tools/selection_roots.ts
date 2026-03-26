import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerSelectionRootsTool(server: McpServer) {
  server.tool(
    "selection_roots",
    "Get RevitLookup-style root objects. Returns selected elements as roots, or active-view elements when selection is empty.",
    {
      source: z
        .enum(["selection_or_active_view"])
        .optional()
        .default("selection_or_active_view")
        .describe("Root source mode. Current implementation follows RevitLookup selection fallback semantics."),
      limitGroups: z
        .number()
        .int()
        .min(1)
        .max(100)
        .optional()
        .default(20)
        .describe("Maximum number of runtime-type groups to return."),
      limitItemsPerGroup: z
        .number()
        .int()
        .min(1)
        .max(200)
        .optional()
        .default(20)
        .describe("Maximum number of root objects to return per group."),
      tokenBudgetHint: z
        .number()
        .int()
        .min(1)
        .optional()
        .describe("Optional token budget hint for server-side truncation."),
    },
    async (args) => {
      const params = {
        source: args.source,
        limitGroups: args.limitGroups,
        limitItemsPerGroup: args.limitItemsPerGroup,
        tokenBudgetHint: args.tokenBudgetHint,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("selection_roots", params);
        });

        return {
          content: [{ type: "text", text: JSON.stringify(response, null, 2) }],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `selection_roots failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
