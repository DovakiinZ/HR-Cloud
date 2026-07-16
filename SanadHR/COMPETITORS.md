---
title: COMPETITORS
aliases: [Competitive Intelligence, Competitor System, CI System]
tags: [control, competitive-intelligence, process]
---

# 🎯 COMPETITORS.md — Competitive Intelligence System

> **This is not a one-time comparison. It is a permanent system Claude Code MUST consult before designing any new feature, workflow, module, report, or user experience.**
>
> Up: [[Home]] · Index: [[Competitor Index]] · Roadmap: [[ROADMAP]] · Features: [[FEATURE_MAP]]

## Why this exists

We do **not** copy competitors. We study them to understand *why* they designed something, *what* problem it solved, *where* users complain, and *what* they're missing — then we build something **noticeably better**.

> **Golden Rule.** If our implementation is only *equal* to competitors, it is **not good enough**. Every feature must explicitly answer: *"What makes SanadHR better than every competitor?"* If there is no compelling answer, the design is not finished.

---

## The Research Rule (run this before ANY new feature)

Before implementing a feature, workflow, module, report, or screen, Claude must answer these — writing the findings into the relevant competitor pages and the feature spec:

1. How does **[[SAP SuccessFactors|SAP]]** solve it? How does **[[Workday]]**? **[[Oracle HCM]]**?
2. How do the MENA leaders solve it — **[[Jisr]]**, **[[ZenHR]]**, **[[Bayzat]]**, **[[Menaitech]]**?
3. How do the modern players solve it — **[[Rippling]]**, **[[Deel]]**, **[[BambooHR]]**, **[[HiBob]]**?
4. How would a world-class **UX reference** solve it — **[[HubSpot]]**, **[[Linear]]**, **[[Notion]]**, **[[Stripe Dashboard]]**?
5. What do users **complain about** (G2 · Capterra · Gartner · TrustRadius · Reddit · YouTube · community forums)?
6. What can we **automate**? What can we **simplify**? What should we **never copy**?
7. **What makes SanadHR's version better?** (the Golden Rule answer)

A feature spec that doesn't cite this research is incomplete.

---

## How the system is organized

- **This file** — the process, the canonical page template, the research rule, the benchmark, the golden rule.
- **[[Competitor Index]]** — every competitor, by category, with quick links and comparison tables.
- **`docs/competitors/*`** — one Obsidian node per competitor. Each connects via wiki links to the SanadHR features it informs, so the **graph visually links competitors → the features they inspired**.

### Categories
- **Enterprise** — [[SAP SuccessFactors]] · [[Oracle HCM]] · [[Workday]] · [[Microsoft Dynamics HR]] · [[UKG Pro]] · [[ADP Workforce Now]]
- **Regional (MENA)** — [[Jisr]] · [[Menaitech]] · [[ZenHR]] · [[PalmHR]] · [[Bayzat]] · [[Darwinbox]] · [[Ojoor]] · [[Cerkl HR]] · [[GulfHR]]
- **Modern HR** — [[Rippling]] · [[Deel]] · [[BambooHR]] · [[HiBob]] · [[Gusto]] · [[Factorial HR]] · [[Personio]]
- **UX References** (inspire UX even though not HR) — [[HubSpot]] · [[Linear]] · [[Notion]] · [[Jira]] · [[Monday]] · [[ClickUp]] · [[Figma]] · [[Slack]] · [[Stripe Dashboard]] · [[Vercel Dashboard]]

---

## Canonical competitor page template

Every file in `docs/competitors/` follows this structure (copy it for new competitors):

```markdown
---
title: <Name>
aliases: [<short names>]
tags: [competitor, <category>]
status: initial-research   # initial-research | verified | monitoring
updated: <YYYY-MM-DD>
---

# <Name>

## Overview
Company · Product · Target Market · Pricing · Strengths · Weaknesses · Positioning

## Modules
Table rating: Payroll, Attendance, Employees, Recruitment, Performance, Workflow,
Approvals, Reports, Dashboards, ESS, Mobile, Documents, Assets, Loans, Expenses,
Training, Integrations, AI, Analytics, Permissions, Organization, Settings, Master Data

## UX Notes
Navigation · Search · Dashboard · Configuration · Automation · Performance ·
Accessibility · Dark Mode · Arabic Support · Mobile UX

## Things we love
## Things we hate
## Customer complaints        (recurring themes from G2/Capterra/Gartner/TrustRadius/Reddit/YouTube)
## Feature requests
## Release Notes
## Screenshots                (describe UI patterns; add captures over time)
## Workflows                  (Payroll, Attendance, Leave, Recruitment, Approvals, Reports)
## Ideas worth stealing       (patterns worth *improving*, not copying)
## Improvements we can make    (Simpler / Faster / More configurable / More automated / More scalable / More beautiful)
## Benchmark
## Related Notes              (wiki links to the SanadHR features it informs)
```

> **Honesty rule.** Base content on well-established public knowledge (product reputation, documented UX, common review themes) up to the research date. **Do not fabricate** specific quotes, dates, or statistics. Tag anything needing live confirmation with `🔄 verify`.

---

## Benchmark table (end every feature spec with this)

Rate each competitor's handling of the feature, then rate ours and explain why it wins. "Our Design" uses a sixth star to make the bar explicit — beyond parity.

| Product | Rating |
|---|---|
| SAP SuccessFactors | ★★★★☆ |
| Workday | ★★★★★ |
| Jisr | ★★★☆☆ |
| ZenHR | ★★★☆☆ |
| Rippling | ★★★★★ |
| **SanadHR (Our Design)** | ★★★★★★ |

*Then write the paragraph: **why ours is superior** — the specific configurability, automation, reproducibility ([[Financial Calculation Engine]]), Saudi-first depth ([[End of Service]], WPS/GOSI), or UX move ([[Design System]]) that no competitor combines.*

---

## Continuous learning (never finished)

Whenever Claude researches a competitor for a feature, **update** the relevant pages: new findings, UX observations, release-note summaries, customer complaints, feature requests, screenshots, and ideas. Bump `updated:` and set `status:` accordingly. The knowledge base grows with the project.

## What "better" means here (SanadHR's edge to press)

Every SanadHR feature should lean on advantages competitors rarely combine:
- **Configuration-first** — no hardcoded policy ([[Master Data Engine]]), vs enterprise rigidity + consultant lock-in.
- **Reproducible, immutable finance** — the [[Financial Calculation Engine]] + [[Immutable Ledger]], vs opaque payroll black boxes.
- **Saudi-first depth** — [[End of Service|EOS Articles 84/85]], GOSI/WPS/Mudad/Qiwa, [[Arabic RTL]] as a first-class citizen, vs bolt-on localization.
- **Modern product UX** — [[Design System]] inspired by [[Linear]]/[[HubSpot]]/[[Stripe Dashboard]], vs 2000s enterprise UIs.
- **Automation + no-code** — [[Workflow Engine]] + [[Completion Effects Engine]], vs manual, ticket-driven ops.

## Related
[[Competitor Index]] · [[Home]] · [[ROADMAP]] · [[PROJECT_STATUS]] · [[FEATURE_MAP]] · [[Design System]]
