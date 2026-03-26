import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerObjectMemberGroupsTool(server: McpServer) {
  server.tool(
    "object_member_groups",
    "Inspection step 2. Get inheritance-aware member groups for a RevitLookup-style object handle returned by selection_roots or navigate_object.",
    {
      objectHandle: z
        .string()
        .min(1)
        .describe("Opaque object handle returned by selection_roots or navigate_object."),
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
        objectHandle: args.objectHandle,
        limitGroups: args.limitGroups,
        limitMembersPerGroup: args.limitMembersPerGroup,
        tokenBudgetHint: args.tokenBudgetHint,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("object_member_groups", params);
        });

        return {
          content: [{ type: "text", text: JSON.stringify(response, null, 2) }],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `object_member_groups failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
