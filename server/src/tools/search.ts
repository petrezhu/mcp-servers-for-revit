import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import { z } from "zod";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";

type ApiIndexEntry = {
  id: string;
  type: string;
  name: string;
  layer?: string;
  priority?: number;
  class?: string;
  enum?: string;
  namespace?: string;
  signature?: string;
  description?: string;
  keywords?: string[];
  related?: string[];
  example?: string;
  answer?: string;
  snippet?: string;
  pitfalls?: string[];
  symbols?: string[];
  questionPatterns?: string[];
  sourceRefs?: string[];
};

type ScoredEntry = {
  entry: ApiIndexEntry;
  score: number;
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

function normalizeText(text: string): string {
  return text.toLowerCase().trim();
}

function tokenize(text: string): string[] {
  return normalizeText(text)
    .split(/[^a-z0-9_.]+/)
    .map((token) => token.trim())
    .filter(Boolean);
}

function uniqueValues(values: Array<string | undefined>, limit = 6): string[] {
  const seen = new Set<string>();
  const result: string[] = [];

  for (const value of values) {
    if (!value || seen.has(value)) {
      continue;
    }

    seen.add(value);
    result.push(value);

    if (result.length >= limit) {
      break;
    }
  }

  return result;
}

function isKnowledgePatch(entry: ApiIndexEntry): boolean {
  return (
    entry.type === "knowledge_patch" ||
    entry.layer === "knowledge_patch" ||
    (((entry.questionPatterns?.length ?? 0) > 0) &&
      Boolean(entry.answer || entry.snippet))
  );
}

function matchesCategory(entry: ApiIndexEntry, category?: string): boolean {
  if (!category) {
    return true;
  }

  const normalizedCategory = normalizeText(category);
  const candidates = [
    entry.type,
    entry.layer,
    entry.class,
    entry.enum,
    entry.namespace,
  ]
    .filter(Boolean)
    .map((value) => normalizeText(String(value)));

  return candidates.includes(normalizedCategory);
}

function scoreFields(
  fields: Array<string | undefined>,
  queryTerms: string[],
  exactScore: number,
  includesScore: number
): number {
  const normalizedFields = fields
    .filter(Boolean)
    .map((value) => normalizeText(String(value)));

  let score = 0;

  for (const term of queryTerms) {
    for (const text of normalizedFields) {
      if (text === term) {
        score += exactScore;
      } else if (text.includes(term)) {
        score += includesScore;
      }
    }
  }

  return score;
}

function scoreQuestionPatterns(
  questionPatterns: string[] | undefined,
  query: string,
  queryTerms: string[]
): number {
  if (!questionPatterns?.length) {
    return 0;
  }

  let score = 0;

  for (const pattern of questionPatterns) {
    const normalizedPattern = normalizeText(pattern);
    const patternTerms = tokenize(normalizedPattern);
    const overlap = patternTerms.filter((term) => queryTerms.includes(term)).length;

    if (normalizedPattern === query) {
      score += 60;
      continue;
    }

    if (normalizedPattern.includes(query) || query.includes(normalizedPattern)) {
      score += 25;
    }

    score += overlap * 8;
  }

  return score;
}

function scoreEntry(
  entry: ApiIndexEntry,
  query: string,
  queryTerms: string[]
): number {
  let score = entry.priority ?? 0;

  score += scoreQuestionPatterns(entry.questionPatterns, query, queryTerms);
  score += scoreFields([entry.name, entry.class, entry.enum], queryTerms, 10, 4);
  score += scoreFields([entry.id, entry.namespace, entry.signature], queryTerms, 6, 3);
  score += scoreFields(
    [
      ...uniqueValues(entry.keywords ?? [], 12),
      ...uniqueValues(entry.symbols ?? [], 12),
      ...uniqueValues(entry.related ?? [], 12),
    ],
    queryTerms,
    6,
    3
  );
  score += scoreFields(
    [entry.answer, entry.snippet, entry.description, ...(entry.pitfalls ?? [])],
    queryTerms,
    5,
    2
  );

  if (isKnowledgePatch(entry)) {
    score += 20;
  }

  return score;
}

function rankEntries(
  index: ApiIndexEntry[],
  query: string,
  category?: string
): ScoredEntry[] {
  const normalizedQuery = normalizeText(query);
  const queryTerms = tokenize(normalizedQuery);

  return index
    .filter((entry) => matchesCategory(entry, category))
    .map((entry) => ({
      entry,
      score: scoreEntry(entry, normalizedQuery, queryTerms),
    }))
    .filter((item) => item.score > 0)
    .sort(
      (a, b) =>
        b.score - a.score ||
        Number(isKnowledgePatch(b.entry)) - Number(isKnowledgePatch(a.entry)) ||
        a.entry.name.localeCompare(b.entry.name)
    );
}

function toSearchResult(item: ScoredEntry) {
  const { entry } = item;
  const symbols = uniqueValues(
    entry.symbols ?? [entry.namespace, entry.class, entry.enum, entry.name],
    6
  );
  const related = uniqueValues(entry.related ?? [], 3);
  const sourceRefs = uniqueValues(entry.sourceRefs ?? [entry.type], 3);

  if (isKnowledgePatch(entry)) {
    return {
      id: entry.id,
      type: "knowledge_patch",
      name: entry.name,
      answer:
        entry.answer ??
        entry.description ??
        `Use ${entry.name} from ${entry.namespace ?? "the Revit API"}.`,
      snippet: entry.snippet ?? entry.example ?? null,
      pitfalls: uniqueValues(entry.pitfalls ?? [], 3),
      symbols,
      related,
      sourceRefs,
    };
  }

  return {
    id: entry.id,
    type: "api_metadata_fallback",
    name: entry.name,
    answer:
      entry.description ??
      `Use ${entry.name} from ${entry.namespace ?? "the Revit API"}.`,
    snippet: entry.example ?? null,
    pitfalls: [],
    symbols,
    related,
    signature: entry.signature ?? null,
    sourceRefs,
  };
}

function buildSearchResponse(
  args: { query: string; category?: string; limit: number },
  ranked: ScoredEntry[]
) {
  const knowledgeMatches = ranked.filter((item) => isKnowledgePatch(item.entry));
  const metadataMatches = ranked.filter((item) => !isKnowledgePatch(item.entry));
  const preferredMatches =
    knowledgeMatches.length > 0 ? knowledgeMatches : metadataMatches;
  const strategy =
    knowledgeMatches.length > 0
      ? "knowledge_patch"
      : metadataMatches.length > 0
        ? "api_metadata_fallback"
        : "no_match";
  const results = preferredMatches.slice(0, args.limit).map(toSearchResult);

  return {
    success: true,
    query: args.query,
    category: args.category ?? null,
    mode: "gap-filler",
    strategy,
    source: "api-index",
    primaryTool: "execute",
    guidance:
      "Do not start with search. For simple tasks like getting the first wall id, current view info, or selected elements, execute must be tried first. Use search only after execute fails or when blocked on one specific Revit API detail, then continue with execute immediately.",
    totalMatches: preferredMatches.length,
    resultCount: results.length,
    results,
    message:
      strategy === "no_match"
        ? "No matching knowledge patch or API metadata was found for this coding question."
        : null,
  };
}

export function registerSearchTool(server: McpServer) {
  server.tool(
    "search",
    "Revit API coding gap-filler for Code Mode. Never use this as the first step for ordinary model queries. If the task is something simple like finding the first wall, reading parameters, listing selected elements, or inspecting the current view, execute must be attempted first. Search is only for one specific missing API detail after an execute attempt fails.",
    {
      query: z
        .string()
        .min(1)
        .describe(
          "A focused missing API detail discovered after an execute attempt fails, such as 'wall length internal units mm' or 'door width parameter'. Do not use this field for the user's whole task request."
        ),
      category: z
        .string()
        .optional()
        .describe(
          "Optional filter for a specific layer or symbol kind, such as 'knowledge_patch', 'class', 'property', or 'enum_value'."
        ),
      limit: z
        .number()
        .int()
        .min(1)
        .max(5)
        .optional()
        .default(3)
        .describe("Maximum number of compact results to return. Keep this small because search is only a follow-up patch step after execute."),
    },
    async (args) => {
      try {
        const index = loadApiIndex();
        const ranked = rankEntries(index, args.query, args.category);
        const payload = buildSearchResponse(args, ranked);

        return {
          content: [
            {
              type: "text",
              text: JSON.stringify(payload, null, 2),
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
