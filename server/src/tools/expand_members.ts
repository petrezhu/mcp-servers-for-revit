import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";

export function registerExpandMembersTool(server: McpServer) {
  server.tool(
    "expand_members",
    "Inspection step 3. Expand only explicitly requested members from a RevitLookup-style object handle so routine inspection stays compact.",
    {
      objectHandle: z
        .string()
        .min(1)
        .describe("Opaque object handle returned by selection_roots or navigate_object."),
      members: z
        .array(
          z.object({
            declaringTypeName: z
              .string()
              .min(1)
              .describe("Declaring type group that owns the requested member."),
            memberName: z
              .string()
              .min(1)
              .describe("Member name to expand."),
          })
        )
        .min(1)
        .describe("Exact member list to expand."),
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
        members: args.members,
        tokenBudgetHint: args.tokenBudgetHint,
      };

      try {
        const response = await withRevitConnection(async (revitClient) => {
          return await revitClient.sendCommand("expand_members", params);
        });

        return {
          content: [{ type: "text", text: JSON.stringify(response, null, 2) }],
        };
      } catch (error) {
        return {
          content: [
            {
              type: "text",
              text: `expand_members failed: ${
                error instanceof Error ? error.message : String(error)
              }`,
            },
          ],
        };
      }
    }
  );
}
