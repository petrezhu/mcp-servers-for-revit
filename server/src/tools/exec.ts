import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { withRevitConnection } from "../utils/ConnectionManager.js";
import { sendCodeExecutionCommand } from "./codeExecution.js";

const EXECUTION_WRAPPER_GUIDANCE = [
  "You are filling in the body of a pre-wrapped C# method, not writing a full standalone program.",
  "The bridge wraps your snippet like:",
  "using System;",
  "using System.Linq;",
  "using System.Collections.Generic;",
  "using Autodesk.Revit.DB;",
  "using Autodesk.Revit.UI;",
  "",
  "namespace AIGeneratedCode",
  "{",
  "    public static class CodeExecutor",
  "    {",
  "        public static object Execute(Document document, UIApplication uiApp, object[] parameters)",
  "        {",
  "            var doc = document;",
  "            var uidoc = uiApp?.ActiveUIDocument;",
  "            var app = uiApp;",
  "            var uiapp = uiApp;",
  "            var application = document?.Application;",
  "",
  "            // your snippet is inserted here",
  "        }",
  "    }",
  "}",
  "Do not redeclare doc/uidoc/app/uiapp/application. Do not invent placeholders like RevitLookupDb.ActiveDbDocument.",
  "The bridge accepts plain snippets, fenced code blocks, and top-level using statements.",
].join("\n");

export function registerExecTool(server: McpServer) {
  server.tool(
    "exec",
    "Legacy alias for `execute`. Prefer `execute` in Code Mode. Use `read_only` for inspection and analysis, and `modify` only after explicit user approval.",
    {
      code: z.string().min(1).describe(EXECUTION_WRAPPER_GUIDANCE),
      parameters: z.array(z.any()).optional().default([]).describe("Optional parameters passed through to the Revit command."),
      mode: z.enum(["read_only", "modify"]).optional().default("read_only").describe("Execution mode. Default to `read_only` for queries and analysis. Use `modify` only after the user explicitly confirms model changes."),
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
                  forwardedCommand: "exec",
                  mode: args.mode,
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
              text: `Exec failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
