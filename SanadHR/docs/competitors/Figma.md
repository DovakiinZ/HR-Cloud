---
title: Figma
aliases: [Figma]
tags: [competitor, ux-reference]
status: initial-research
updated: 2026-07-03
---

# Figma
> Teaches SanadHR how real-time multiplayer, live cursors, and inline comments make collaboration feel alive — the bar for [[Workflows]] and [[Document Platform]] co-editing.
> Up: [[Competitor Index]] · System: [[COMPETITORS]] · Design: [[Design System]]

## Overview
- **Company:** Figma, Inc. (San Francisco) · **Product:** Figma — collaborative interface design; plus FigJam (whiteboard), Dev Mode, Figma Slides. · **Category:** (not HR) design & collaborative canvas tooling.
- **Why we study it:** Figma set the modern standard for **browser-based multiplayer collaboration** — live cursors with name tags, presence avatars, inline pinned comments, and conflict-free concurrent editing. Those interaction patterns translate directly to collaborative HR surfaces: workflow design, document co-authoring, org-chart editing.
- **Pricing:** Freemium; Starter (free), Professional, Organization, Enterprise per-editor. 🔄 verify current tiers/prices.
- **Strengths:** Real-time multiplayer; zero-install browser canvas; components/variants/auto-layout design system tooling; comment threads pinned to objects; superb performance on a demanding canvas.
- **Weaknesses:** Steep learning curve for non-designers; can be resource-heavy in-browser; enterprise admin depth still maturing. 🔄 verify.
- **Positioning:** "Where teams design together" — collaboration as the core primitive, not an add-on.

## Surfaces & Capabilities
| Surface | Rating | Notes |
|---|---|---|
| Navigation | ★★★★☆ | Files/projects/teams sidebar; recents; clean file browser. |
| Command/Search | ★★★★★ | Quick actions palette (`Cmd/Ctrl-/`) to run any command by name. |
| Views/Canvas | ★★★★★ | Infinite zoomable canvas; buttery pan/zoom; the reference for spatial UIs. |
| Records/Detail | ★★★★☆ | Right-hand properties inspector; contextual, object-driven. |
| Automation | ★★★☆☆ | Plugins/widgets extend behavior; not a rules engine. |
| Collaboration | ★★★★★ | Signature: live cursors + presence + inline comments + observation ("follow") mode. |
| Notifications | ★★★★☆ | Comment mentions, activity, file updates. |
| Settings/Config | ★★★★☆ | Team/org admin, permissions, design-system libraries. |
| Mobile | ★★★☆☆ | Mobile viewer/mirroring app; not for authoring. |
| Theming/Dark Mode | ★★★★☆ | Dark UI available; canvas chrome is calm and unobtrusive. |

## UX Notes
- **Navigation:** file → page → frame hierarchy; left layers panel, right properties panel — a stable, predictable three-column shell.
- **Search/Command:** `Cmd/Ctrl-/` quick actions is a masterclass — every menu action is searchable and executable without hunting.
- **Dashboard/Home:** recents + team files gallery; low-friction re-entry.
- **Collaboration (the point):** colored **live cursors with name labels**, **presence avatars** in the toolbar, **click-avatar-to-follow** observation mode, and **comment pins** anchored to exact canvas coordinates with threaded replies and emoji.
- **Performance:** custom rendering (WebGL/WASM) delivers 60fps on huge files — the benchmark for perceived responsiveness.
- **Accessibility:** keyboard-driven; contrast options improving. 🔄 verify.
- **Dark Mode:** yes; chrome recedes so content leads.
- **Arabic/RTL readiness:** design canvas supports RTL text content, but the *app chrome* is LTR — no first-class RTL product experience. Gap SanadHR owns: [[Arabic RTL]].
- **Mobile UX:** viewing/handoff, not authoring.

## Things we love
- Live multiplayer cursors + presence — collaboration you can *see*.
- Comment pins anchored to exact objects, threaded, resolvable.
- Quick actions command palette.
- Follow/observation mode for walkthroughs.

## Things we hate
- Heavy in-browser footprint on low-end machines. 🔄 verify.
- Non-designer onboarding is steep.

## Customer complaints
- Performance on very large files / older hardware. 🔄 verify.
- Pricing changes and seat-model confusion after editor-role restructuring. 🔄 verify.

## Feature requests
- Better offline support. 🔄 verify.
- Deeper native comment workflows / task assignment. 🔄 verify.

## Release Notes
- Ongoing expansion beyond design: Dev Mode, FigJam, Figma Slides, and AI-assisted features rolled out through 2024–2026. 🔄 verify specifics.

## Screenshots
- Capture **live cursors with name tags** and the **presence avatar stack** in the top bar.
- Capture an **inline comment pin** with a threaded reply.
- Capture the **quick-actions command palette**.
- Capture **follow/observation mode** banner.

## Workflows
- Open file → teammates' cursors appear → co-edit simultaneously.
- Drop a comment pin → mention a teammate → they get notified → thread resolves.
- Click a presence avatar → follow their viewport through a review.

## Ideas worth stealing
- **Live presence + cursors** on collaborative SanadHR surfaces: the [[Workflow Engine]] designer and [[Document Platform]] template editor — show who else is editing in real time.
- **Anchored inline comments** on documents and workflow nodes — thread discussion exactly where the decision lives, feeding [[Notifications]].
- **Follow/observation mode** for HR admins walking a manager through a [[Request Center]] approval flow.
- **Quick-actions palette** (`Cmd-/`) mirrored from ClickUp's Cmd-K — one accelerator across the app.

## Improvements we can make
- **Simpler:** collaboration presence without Figma's authoring complexity — read/comment first, edit when permitted.
- **Faster:** lightweight presence (WebSocket avatars) rather than a full canvas engine.
- **More configurable:** comment visibility scoped by role via [[Master Data Engine]] permissions.
- **More automated:** resolving a comment can trigger a [[Workflow Engine]] step.
- **More scalable:** presence on structured HR objects, not an infinite canvas — cheaper to sync.
- **More beautiful + Arabic-first:** RTL-native comment threads and mirrored cursor labels ([[Arabic RTL]]).

## Benchmark
| Product | Real-time Collaboration | Notes |
|---|---|---|
| [[Figma\|Figma]] | ★★★★★ | The multiplayer benchmark. |
| Google Docs | ★★★★☆ | Presence + comments, document-scoped. |
| Miro | ★★★★☆ | Canvas presence + sticky collaboration. |
| **SanadHR (Our Design)** | ★★★★★★ | Multiplayer presence + anchored comments on HR objects (workflows, documents), Arabic-first, permission-governed. |

Ours wins by bringing Figma-grade presence and anchored commenting to *structured HR workflows and documents*, RTL-native and role-scoped.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Design System]] · [[Arabic RTL]] · [[Workflows]] · [[Workflow Engine]] · [[Document Platform]] · [[Notifications]] · [[Slack]]
