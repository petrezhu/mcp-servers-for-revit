import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const CODE_MODE_TOOL_FILES = new Set([
  "exec.ts",
  "exec.js",
  "execute.ts",
  "execute.js",
  "search.ts",
  "search.js",
]);

const CODE_MODE_TOOL_PRIORITY = new Map([
  ["exec.ts", 0],
  ["exec.js", 0],
  ["execute.ts", 1],
  ["execute.js", 1],
  ["search.ts", 2],
  ["search.js", 2],
]);

function resolveToolsetMode(): "code" | "full" {
  const toolset = process.env.REVIT_MCP_TOOLSET?.trim().toLowerCase();
  const enableLegacyTools = /^(1|true|yes|on)$/i.test(
    process.env.REVIT_MCP_ENABLE_LEGACY_TOOLS ?? ""
  );

  if (enableLegacyTools || toolset === "full" || toolset === "legacy") {
    return "full";
  }

  return "code";
}

export async function registerTools(server: McpServer) {
  // 获取当前文件的目录路径
  const __filename = fileURLToPath(import.meta.url);
  const __dirname = path.dirname(__filename);

  // 读取tools目录下的所有文件
  const files = fs.readdirSync(__dirname);

  // 过滤出.ts或.js文件，但排除index文件和register文件
  const toolFiles = files.filter(
    (file) =>
      (file.endsWith(".ts") || file.endsWith(".js")) &&
      file !== "index.ts" &&
      file !== "index.js" &&
      file !== "register.ts" &&
      file !== "register.js"
  );

  const toolsetMode = resolveToolsetMode();
  const selectedToolFiles =
    toolsetMode === "full"
      ? toolFiles
      : toolFiles
          .filter((file) => CODE_MODE_TOOL_FILES.has(file))
          .sort((a, b) => {
            const left = CODE_MODE_TOOL_PRIORITY.get(a) ?? Number.MAX_SAFE_INTEGER;
            const right =
              CODE_MODE_TOOL_PRIORITY.get(b) ?? Number.MAX_SAFE_INTEGER;
            return left - right || a.localeCompare(b);
          });

  console.error(`Tool registration mode: ${toolsetMode}`);

  // 动态导入并注册每个工具
  for (const file of selectedToolFiles) {
    try {
      // 构建导入路径
      const importPath = `./${file.replace(/\.(ts|js)$/, ".js")}`;

      // 动态导入模块
      const module = await import(importPath);

      // 查找并执行注册函数
      const registerFunctionName = Object.keys(module).find(
        (key) => key.startsWith("register") && typeof module[key] === "function"
      );

      if (registerFunctionName) {
        module[registerFunctionName](server);
        console.error(`已注册工具: ${file}`);
      } else {
        console.warn(`警告: 在文件 ${file} 中未找到注册函数`);
      }
    } catch (error) {
      console.error(`注册工具 ${file} 时出错:`, error);
    }
  }

  if (toolsetMode === "code") {
    const skippedToolCount = toolFiles.length - selectedToolFiles.length;
    console.error(
      `Code Mode active: skipped ${skippedToolCount} legacy tool registrations. Set REVIT_MCP_TOOLSET=full or REVIT_MCP_ENABLE_LEGACY_TOOLS=true to restore them.`
    );
  }
}
