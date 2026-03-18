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
  "            // document: Autodesk.Revit.DB.Document",
  "            // uiApp: Autodesk.Revit.UI.UIApplication",
  "            // parameters: object[]",
  "            // You may create local aliases such as:",
  "            // var doc = document;",
  "            // var uidoc = uiApp?.ActiveUIDocument;",
  "            // var app = uiApp;",
  "            // var uiapp = uiApp;",
  "            // var application = document?.Application;",
  "",
  "            // your snippet is inserted here",
  "        }",
  "    }",
  "}",
  "No local aliases are predeclared for you. Define any aliases yourself if you want them.",
  "Return a scalar, object, or collection from the snippet body.",
].join("\n");

export function registerExecuteTool(server: McpServer) {
  server.tool(
    "execute",
    "Primary Code Mode tool. Start here for nearly all model queries. Always attempt one read-only C# execution before search. For simple requests like 'get the first wall id', 'list selected elements', or 'read current view info', call execute directly without any search step.",
    {
      code: z.string().min(1).describe(EXECUTION_WRAPPER_GUIDANCE),
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
