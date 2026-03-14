import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

type ApiIndexEntry = {
  id: string;
  type: string;
  name: string;
  class?: string;
  enum?: string;
  namespace?: string;
  signature?: string;
  description?: string;
  keywords?: string[];
  related?: string[];
  example?: string;
};

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

function resolveApiIndexPath(): string {
  const candidatePaths = [
    path.resolve(__dirname, "../data/api-index.json"),
    path.resolve(__dirname, "../../src/data/api-index.json"),
    path.resolve(process.cwd(), "build/data/api-index.json"),
    path.resolve(process.cwd(), "src/data/api-index.json"),
  ];

  const existingPath = candidatePaths.find((candidatePath) =>
    fs.existsSync(candidatePath)
  );

  if (!existingPath) {
    throw new Error(
      `api-index.json not found. Checked: ${candidatePaths.join(", ")}`
    );
  }

  return existingPath;
}

let cachedIndex: ApiIndexEntry[] | null = null;

function loadApiIndex(): ApiIndexEntry[] {
  if (cachedIndex) {
    return cachedIndex;
  }

  const raw = fs.readFileSync(resolveApiIndexPath(), "utf8");
  cachedIndex = JSON.parse(raw) as ApiIndexEntry[];
  return cachedIndex;
}

function scoreEntry(entry: ApiIndexEntry, queryTerms: string[]): number {
  const haystacks = [
    entry.id,
    entry.name,
    entry.class,
    entry.enum,
    entry.namespace,
    entry.signature,
    entry.description,
    ...(entry.keywords ?? []),
    ...(entry.related ?? []),
  ]
    .filter(Boolean)
    .map((value) => String(value).toLowerCase());

  let score = 0;
  for (const term of queryTerms) {
    for (const text of haystacks) {
      if (text === term) score += 8;
      else if (text.includes(term)) score += 3;
    }
  }

  return score;
}

export function registerSearchTool(server: McpServer) {
  server.tool(
    "search",
    "Search a prebuilt Revit API index and return type signatures, docs, examples, and related APIs for Code Mode workflows.",
    {
      query: z.string().min(1).describe("Natural language or API keyword query, such as 'wall type name' or 'door width parameter'."),
      category: z.string().optional().describe("Optional result type filter, such as 'class', 'property', or 'enum_value'."),
      limit: z.number().int().min(1).max(20).optional().default(5).describe("Maximum number of matches to return."),
    },
    async (args) => {
      try {
        const index = loadApiIndex();
        const queryTerms = args.query.toLowerCase().split(/\s+/).filter(Boolean);
        const filtered = index
          .filter((entry) => !args.category || entry.type === args.category)
          .map((entry) => ({ entry, score: scoreEntry(entry, queryTerms) }))
          .filter((item) => item.score > 0)
          .sort((a, b) => b.score - a.score)
          .slice(0, args.limit)
          .map(({ entry, score }) => ({ ...entry, score }));

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(
                {
                  success: true,
                  query: args.query,
                  category: args.category ?? null,
                  source: "mock-api-index",
                  totalMatches: filtered.length,
                  matches: filtered,
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
              text: `Search failed: ${error instanceof Error ? error.message : String(error)}`,
            },
          ],
          isError: true,
        };
      }
    }
  );
}
