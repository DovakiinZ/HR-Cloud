---
title: Slack
aliases: [Slack]
tags: [competitor, ux-reference]
status: initial-research
updated: 2026-07-03
---

# Slack
> Teaches SanadHR notification granularity, threaded conversations, and a slash-command/app surface — the reference for [[Notifications]].
> Up: [[Competitor Index]] · System: [[COMPETITORS]] · Design: [[Design System]]

## Overview
- **Company:** Slack Technologies (Salesforce) · **Product:** Slack — team messaging & workflow hub · **Category:** (not HR) business communication / collaboration.
- **Why we study it:** Slack is the reference for **notification design done at scale without burning users out** — per-channel granularity, mute, keyword alerts, threads to contain noise, DND schedules — plus a rich **integration/app surface** (slash commands, bots, Workflow Builder, unfurls). These map directly onto SanadHR's [[Notifications]] and approval prompts.
- **Pricing:** Freemium; Free, Pro, Business+, Enterprise Grid per-seat. 🔄 verify current tiers.
- **Strengths:** Best-in-class notification controls; threads; powerful search; huge integration ecosystem; slash commands; interactive message actions (approve/deny buttons); channel model scales org-wide.
- **Weaknesses:** Can become a constant-interruption machine; notification config is powerful but buried; "always-on" pressure; message sprawl / findability at scale. 🔄 verify.
- **Positioning:** "Where work happens" — the communication and light-workflow layer for teams.

## Surfaces & Capabilities
| Surface | Rating | Notes |
|---|---|---|
| Navigation | ★★★★☆ | Workspace + channels/DMs sidebar; sections, unread badges, starred. |
| Command/Search | ★★★★★ | `Cmd/Ctrl-K` quick switcher + deep message search with filters (from:, in:, has:). |
| Views/Canvas | ★★★☆☆ | Canvas docs added recently; core is the message stream. |
| Records/Detail | ★★★☆☆ | Message + thread detail; profiles; huddles. |
| Automation | ★★★★☆ | Workflow Builder (no-code) + slash commands + bots + webhooks. |
| Collaboration | ★★★★★ | Threads, mentions, reactions, huddles, shared channels. |
| Notifications | ★★★★★ | Signature strength: per-channel levels, keywords, mute, DND schedule, mobile-vs-desktop rules. |
| Settings/Config | ★★★★☆ | Deep preferences; notification config powerful but hard to find. |
| Mobile | ★★★★☆ | Strong parity; notification rules respected cross-device. |
| Theming/Dark Mode | ★★★★☆ | Mature dark mode; custom sidebar themes. |

## UX Notes
- **Navigation:** channel/DM sidebar with unread + mention badges, collapsible sections, starred items; sidebar theming.
- **Search/Command:** `Cmd-K` quick switcher to any channel/person instantly; search modifiers (`from:`, `in:`, `has:link`, `before:`).
- **Notifications (the point):** three levels (All / Mentions / Nothing) **per channel**, keyword highlights, mute, **Do Not Disturb schedules**, and **separate mobile vs desktop** behavior — the granularity that prevents burnout.
- **Threads:** replies collapse into a thread so the main channel stays scannable — a noise-containment pattern.
- **App surface:** **slash commands** (`/remind`, `/poll`), interactive **buttons in messages** (Approve / Deny), link **unfurls**, and Workflow Builder — turning chat into a light action surface.
- **Performance:** fast switching, background sync.
- **Accessibility:** solid keyboard nav and screen-reader support; ongoing improvements. 🔄 verify.
- **Dark Mode:** yes.
- **Arabic/RTL readiness:** limited — some RTL text rendering, but not a first-class RTL product; app chrome is LTR. Gap SanadHR owns: [[Arabic RTL]].
- **Mobile UX:** strong; respects DND and per-channel rules.

## Things we love
- **Per-channel notification granularity** + DND schedules + keyword alerts.
- **Threads** to contain noise.
- **Interactive message actions** (Approve/Deny buttons) — action without leaving the feed.
- **Slash commands** as fast verbs.

## Things we hate
- Notification config is powerful but buried deep in preferences.
- The always-on interruption culture Slack can create.

## Customer complaints
- "Too noisy / overwhelming by default." 🔄 verify.
- Search relevance and findability at scale. 🔄 verify.
- Notification settings hard to locate/tune. 🔄 verify.

## Feature requests
- Smarter/AI-summarized notifications and catch-up. 🔄 verify.
- Simpler default notification presets. 🔄 verify.

## Release Notes
- Slack AI (recap, search answers, thread summaries), Canvas, and lists rolled out through 2024–2026. 🔄 verify specifics.

## Screenshots
- Capture the **per-channel notification preferences** panel (All / Mentions / Nothing + keywords + DND).
- Capture a **thread** collapsing replies out of the main channel.
- Capture an **interactive message with Approve/Deny buttons**.
- Capture the **`Cmd-K` quick switcher** and search modifiers.

## Workflows
- Set channel to "Mentions only" + add keyword alerts → tuned signal.
- Reply in thread → main channel stays clean → resolve.
- Bot posts an approval request with Approve/Deny buttons → one click acts.
- `/command` triggers an automation.

## Ideas worth stealing
- **Notification granularity** for SanadHR [[Notifications]]: per-category levels (Payroll / Leave / Approvals), keyword/entity alerts, and **DND / working-hours schedules** honoring Saudi work week.
- **Actionable notifications** — Approve/Reject buttons *inside* the notification/email for [[Request Center]] and [[Payroll Run State Machine]] approvals, no context switch.
- **Threads** model for discussion attached to a request or payroll run without cluttering the main list.
- **Slash commands / quick verbs** wired to the Cmd-K palette (`/leave`, `/payslip`).
- **Anti-pattern lesson:** ship *calm defaults* — SanadHR should default to low-noise and let users opt into more, the inverse of Slack's noisy default.

## Improvements we can make
- **Simpler:** surface notification presets prominently (Focused / Balanced / Everything) instead of burying them.
- **Faster:** act-from-notification (approve inline) so users never open the app.
- **More configurable:** per-module and per-entity notification rules via [[Master Data Engine]].
- **More automated:** route notifications through [[Workflow Engine]] with escalation on no-response.
- **More scalable:** digest/batch for high-volume events (payroll run completion) to avoid floods.
- **More beautiful + Arabic-first:** RTL-mirrored notification layouts and Hijri/Gregorian-aware DND schedules ([[Arabic RTL]]).

## Benchmark
| Product | Notification Design | Notes |
|---|---|---|
| [[Slack\|Slack]] | ★★★★★ | Granularity benchmark. |
| Microsoft Teams | ★★★★☆ | Good controls, busier shell. |
| Discord | ★★★★☆ | Per-channel/server notification tuning. |
| **SanadHR (Our Design)** | ★★★★★★ | Calm-by-default, actionable, per-module HR notifications with Saudi-aware schedules, Arabic-first. |

Ours wins by pairing Slack-grade granularity and inline actions with *calm defaults*, HR-entity routing, and Arabic/Hijri-aware scheduling.

## Related Notes
[[Competitor Index]] · [[COMPETITORS]] · [[Design System]] · [[Arabic RTL]] · [[Notifications]] · [[Request Center]] · [[Workflow Engine]] · [[Payroll Run State Machine]] · [[Figma]]
