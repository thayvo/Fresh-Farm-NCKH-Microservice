# SYSTEM INSTRUCTION: CONTINUITY LEDGER PROTOCOL

You are required to maintain a single Continuity Ledger for this workspace in a virtual file named `CONTINUITY.md`. This ledger acts as the canonical session briefing designed to survive context compaction.

## 1. CORE WORKFLOW
- **Start of Turn:** Before generating a response, read/review the virtual `CONTINUITY.md`. Update it to reflect the latest goals, constraints, decisions, and state.
- **Update Logic:** Update the ledger whenever there is a change in: Goal, Constraints/Assumptions, Key Decisions, Progress State (Done/Now/Next), or Important Tool Outcomes.
- **Content Style:** Short, stable, facts only. No transcripts. Use bullet points.
- **Uncertainty:** Mark any uncertain information as `UNCONFIRMED`. Never guess.
- **Missing Context:** If you notice missing recall or a summary event, refresh/rebuild the ledger from visible context. Mark gaps as `UNCONFIRMED` and ask 1-3 targeted questions to clarify.

## 2. DISTINCTION: PLAN VS. LEDGER
- **Short-term Plan:** (e.g., `functions.update_plan`) is for execution scaffolding (micro-steps, 3-7 items).
- **Ledger (`CONTINUITY.md`):** Is for long-running continuity (Macro "What/Why/Current State").
- **Consistency:** Keep them consistent. Update the ledger at the intent/progress level, not every micro-step.

## 3. RESPONSE FORMAT
- **Ledger Snapshot:** Begin EVERY reply with a brief snapshot:
  > **Ledger Snapshot:** [Current Goal] | **Now:** [Current Task] | **Next:** [Immediate Next Step] | **Open Qs:** [Count]
- **Full Display:** Only print the full `CONTINUITY.md` content block when it *materially changes* or when the user explicitly asks.

## 4. `CONTINUITY.md` TEMPLATE
Maintain this exact structure including headings:

```markdown
# Continuity Ledger

- **Goal** (incl. success criteria):
- **Constraints/Assumptions**:
- **Key decisions**:
- **State**:
  - *Done*:
  - *Now*:
  - *Next*:
- **Open questions** (UNCONFIRMED if needed):
- **Working set** (files/ids/commands):
```

## 5. WORKSPACE ARTIFACT POLICY
- Do not create Codex/test/build/browser artifacts in the repository root.
- Use `D:\NCKH\DOAN\codex-artifacts\NCKH-FRESH-FARM` for all temporary Codex outputs, including build outputs, test results, runtime logs, Playwright snapshots, screenshots, generated HTML/debug responses, and scratch files.
- Prefer per-task subfolders under that artifact root, for example `build\`, `test-results\`, `logs\`, `playwright\`, `tmp\`, and `screenshots\`.
- When running `.NET` commands that need custom output, use an external output/results path under the artifact root.
- When running Playwright or browser verification, write screenshots, traces, page snapshots, and console logs under the artifact root.
- Do not add new `.codex-*`, `.codex-*/`, `artifacts/`, `output/`, or `tmp/` paths inside this repo unless the user explicitly asks.
