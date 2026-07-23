import type { AgentPlugin } from "@cline/sdk";

const RECALL_URL = "http://localhost:5099/memory/maria/recall";

const aetherMariaRecall: AgentPlugin = {
  name: "aether-maria-recall",
  manifest: {
    capabilities: ["tools"],
  },
  setup(api) {
    api.registerTool({
      name: "maria_recall",
      description:
        "Search Maria's long-term memory (MariaMemory 2.0) via Aether daemon port 5099. Returns memory nodes matching the query via FTS5 full-text search.",
      parameters: {
        type: "object",
        properties: {
          query: {
            type: "string",
            description: "The search query (keywords or phrases)",
          },
          limit: {
            type: "integer",
            description: "Maximum number of results to return (default 10)",
            default: 10,
          },
        },
        required: ["query"],
      },
      async execute(args: { query: string; limit?: number }) {
        const query = args.query;
        const limit = args.limit ?? 10;
        try {
          const url = `${RECALL_URL}?query=${encodeURIComponent(query)}&limit=${limit}`;
          const res = await fetch(url);
          if (!res.ok) {
            return {
              success: false,
              error: `Aether returned HTTP ${res.status}: ${await res.text().catch(() => "unknown")}`,
            };
          }
          const data = await res.json();
          return data;
        } catch (err: any) {
          if (
            err?.code === "ECONNREFUSED" ||
            err?.name === "TypeError" ||
            err?.message?.includes("fetch failed")
          ) {
            return {
              success: false,
              error: "Aether daemon not running. Start with: aether",
            };
          }
          return {
            success: false,
            error: String(err?.message ?? err),
          };
        }
      },
    });
  },
};

export default aetherMariaRecall;
