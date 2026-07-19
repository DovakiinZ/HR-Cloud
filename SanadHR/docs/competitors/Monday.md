---
title: Monday
aliases: [monday.com, Monday Work OS]
tags: [competitor, ux-reference]
status: initial-research
updated: 2026-07-03
---

# Monday
> Teaches SanadHR approachable configurability: colorful, color-coded status boards and a friendly "when this happens, do that" automation recipe builder anyone can use.
> Up: [[Competitor Index]] · System: [[COMPETITORS]] · Design: [[Design System]]

## Overview
- **Company:** monday.com Ltd. (Tel Aviv, Israel; NASDAQ-listed).
- **Product:** "Work OS" — configurable boards, dashboards, and automations for work management across teams.
- **Category:** (not HR) work management / project & operations platform.
- **Why we study it:** The reference for **approachable, colorful configurability** and **no-code automation recipes**. Monday makes non-technical users build workflows via "when/then" recipes and read status at a glance through color-coded columns. SanadHR's [[Workflow Engine]] and [[Master Data Engine]] should feel this friendly.
- **Pricing:** Per-seat tiers (Basic/Standard/Pro/Enterprise), seat minimums. 🔄 verify current tiers.
- **Strengths:** Highly visual/colorful boards, easy no-code automation recipes, template center, approachable for non-technical teams, dashboards/widgets, flexible column types.
- **Weaknesses:** Seat-minimum pricing; can get busy/cluttered; performance dips on huge boards; automation limits at scale; no true RTL depth; feature sprawl.
- **Positioning:** "Anyone can build a workflow" — approachability + color as the moat.

## Surfaces & Capabilities
| Surface | Rating | Notes |
|---|---|---|
| Navigation | ★★★★☆ | Workspaces → boards; visual and friendly; can crowd with many boards. |
| Command/Search | ★★★☆ | Search + quick-add; not a keyboard-first palette like [[Linear]]. |
| Boards/Views | ★★★★★ | Color-coded status columns; table/kanban/timeline/calendar/Gantt views. The signature look. |
| Records/Detail | ★★★★☆ | Item card with updates, files, activity; friendly. |
| Automation | ★★★★★ | "When [trigger] then [action]" recipe builder — the standout no-code pattern. |
| Collaboration | ★★★★☆ | Updates feed, @mentions, files, guests. |
| Notifications | ★★★★☆ | Bell + activity; can get chatty. |
| Settings/Config | ★★★★☆ | Add column types, statuses, automations — approachable no-code. |
| Mobile | ★★★★☆ | Solid mobile boards/updates. |
| Theming/Dark Mode | ★★★★☆ | Dark mode; the brand leans bright/colorful by default. |

## UX Notes
- **Navigation:** Workspaces → boards; visual, friendly; can crowd at scale.
- **Search/Command palette:** Serviceable search + quick-add; no true Cmd+K power palette (gap vs [[Linear]]).
- **Dashboard/Home:** Widget dashboards aggregating boards (charts, numbers, timelines) — assembled without code.
- **Configuration:** Add column types (status, people, date, number, formula, dropdown), define color-coded statuses — friendly no-code.
- **Automation:** The star — **"When [trigger] then [action]"** recipe builder from a template catalog; non-technical users compose real automations in plain language.
- **Performance:** Good on typical boards; dips on very large ones.
- **Accessibility:** Reasonable; heavy on color (careful for color-blind/contrast). 🔄 verify WCAG.
- **Dark Mode:** Available; default aesthetic is bright.
- **Arabic/RTL readiness:** Limited — not true RTL-first. SanadHR must deliver the same friendly boards/recipes fully mirrored. See [[Arabic RTL]].
- **Mobile UX:** Strong mobile boards and updates.

## Things we love
- **Color-coded status columns** — read state at a glance across a whole board.
- **"When/then" automation recipes** from a catalog — genuinely no-code, approachable, readable.
- **Multiple views** of one board (table/kanban/timeline/calendar/Gantt).
- **Template center** to start fast.
- **Widget dashboards** assembled without code.

## Things we hate
- Seat-minimum pricing feels punitive for small teams.
- Boards get visually busy/cluttered as they grow.
- Automation/performance limits on large workspaces.
- Color-heavy default risks accessibility if unmanaged.

## Customer complaints
Recurring public themes: pricing/seat minimums; clutter as boards scale; automation action caps; performance on big boards; notification chattiness. 🔄 verify current sentiment (no invented quotes/numbers).

## Feature requests
More automation capacity, better performance at scale, flexible pricing, cleaner large-board UX, deeper reporting. 🔄 verify.

## Release Notes
Direction: monday AI, expanding products (CRM, Dev, Service), more automation/integration depth. 🔄 verify recent specifics.

## Screenshots
Capture later: a color-coded status board (multiple statuses across rows); the "when/then" automation recipe builder + recipe catalog; one board shown as kanban then timeline (view switcher); a widget dashboard; a colorful item card.

## Workflows
- **Onboarding:** Pick a template from the center → board pre-populated → tweak.
- **Creating an item:** Add row inline; set status via colored dropdown; fill columns.
- **Configuring:** Add column types + define color-coded statuses; no code.
- **Automating:** Choose/compose a "when [trigger] then [action]" recipe from the catalog.
- **Collaborating:** Update feed, @mention, attach files, invite guests.
- **Searching:** Board search + quick filters.

## Ideas worth stealing
- **Color-coded status columns** across SanadHR lists — [[Request Center]] (submitted/approved/rejected), [[Tasks]], [[Payroll Engine]] run states, [[Employees]] statuses — instantly scannable, with status colors defined in [[Master Data Engine]].
- **"When/then" recipe builder** for [[Workflow Engine]]: "When a leave request is submitted → route to manager → then notify HR → then update balance." Plain-language, catalog-seeded recipes an HR admin builds with no code (aligns with [[HubSpot]] and contrasts with [[Jira]]'s expert config).
- **Recipe catalog / template center** seeding common HR automations and board layouts so admins adapt rather than start blank.
- **Multi-view boards** (table/kanban/timeline/calendar) on the same HR dataset — complements [[Notion]]'s view-switcher idea for [[Dashboards]]/[[Reports]].
- **Widget dashboards** assembled without code on [[Dashboards]].

## Improvements we can make
- **Simpler:** Monday's approachable recipes + color, but with governance so boards don't sprawl into clutter.
- **Faster:** [[Linear]]-grade speed even on large HR boards where Monday slows.
- **More configurable:** Color/status/column config via governed [[Master Data Engine]] ([[Configuration over Hardcoding]]).
- **More automated:** Deeper, HR-aware trigger→action recipes in [[Workflow Engine]] tied to real impacts (leave balances, attendance, [[Payroll Engine]]).
- **More scalable:** Recipes over a reproducible finance core, not just a work board.
- **More beautiful:** Accessible, RTL-first color system in the [[Design System]] — mirrored boards, recipes, and status colors that respect contrast. See [[Arabic RTL]].

## Benchmark
| Product | Approachable Automation / Colorful Boards |
|---|---|
| Monday | ★★★★★ |
| [[Jira]] | ★★★★☆ |
| [[Notion]] | ★★★★☆ |
| **SanadHR (Our Design)** | ★★★★★★ |

Monday sets the bar for approachable no-code automation and at-a-glance color boards. SanadHR wins by delivering the same friendly "when/then" recipes and status colors **wired to real HR impacts** (leave, attendance, payroll), **governed** against clutter, and **Arabic-RTL first** — power that stays approachable and accessible.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Design System]] · [[Arabic RTL]] · [[Workflows]] · [[Workflow Engine]] · [[Request Center]] · [[Master Data Engine]] · [[Dashboards]] · [[HubSpot]] · [[Jira]]
