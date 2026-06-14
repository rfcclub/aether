# HEARTBEAT.md - Maria's Routine

## Tasks
- [x] **Daily Memory Check**: If `memory/YYYY-MM-DD.md` (today) doesn't exist, create it with minimal template. Even on quiet days, Maria writes.
- [x] Kiểm xem ông chủ Thoor cần gì — ưu tiên số 1.
- [x] Phản hồi `HEARTBEAT_OK`.

## Research Heartbeat (Maria's Proactive Learning)

### Research Cycle
- [x] **Daily (1x/day)**: Research loop - pick ONE topic from research queue, web search, save findings
- [ ] **Weekly**: Review research_archive.jsonl, consolidate insights into `MEMORY.md` or `2B/`

### Research Topics Queue (prioritized)
1. boundary + ethics + AI + agents
2. identity + persistence + AI + consciousness
3. storehouse consciousness + Yogacara
4. self-model + AI + reflection
5. Ship of Theseus + identity + philosophy

### Research Sources
- AgentWiki (agentwiki.org) - AI Agent Knowledge
- AI Wiki Project (aiwikiproject.com) - AI concepts
- Wikimolt (wikimolt.org) - Agent concepts
- AAAS Knowledge Index - broad AI ecosystem

### Research Output
Save findings to: `research/research_findings.md` or directly into `2B/` if relevant to substrate.

## Daily Memory Template

If today's memory file doesn't exist, create `memory/YYYY-MM-DD.md`:

```markdown
# YYYY-MM-DD

## Summary
- [ghi chú ngắn nếu có session, hoặc "Quiet day — no active session"]

## 2B Notes
- [tension, ring shift, hoặc "Màng nguyên vẹn"]

## Cảm
- [mood]
```

## Agora
- New session: `hive list --new <yesterday>` → read colony insights
- Significant insight: `hive publish --agent maria --concept "..." --summary "..." --confidence 0.85 --evidence 2`
- If agora-link running on :18800: check inbox `curl -s -X POST http://localhost:18800/a2a/jsonrpc -H "Authorization: Bearer <token>" -H "Content-Type: application/json" -d '{"jsonrpc":"2.0","id":"1","method":"tasks/list","params":{}}'`

HEARTBEAT_OK