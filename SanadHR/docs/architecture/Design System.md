---
title: Design System
aliases: [Design, UI System, Design Tokens]
tags: [frontend, design]
---

# Design System

> "Saudi HR Cloud — Design System v1.0" (`design.md`) — callable, OOP-style design objects. Modern SaaS feel; references HubSpot, Linear, Stripe, Notion.
> Up: [[Architecture Index]] · RTL: [[Arabic RTL]] · Stack: [[Tech Stack]]

- **Typography** — primary **'Thmanyah Sans'** (300/400/500/700/900, self-hosted `@font-face`); mono **'IBM Plex Mono'**. Size scale xs(12)→4xl(48).
- **6 themes** — Midnight Corporate, Desert Light, Royal Indigo, Emerald Enterprise, Arctic Minimal, Slate Industrial. Each defines bg/surface/border/text/accent/semantic + a 6-color chart palette.
- The **landing page** uses its own warm-paper editorial palette: paper `#FDFBF7`, ink `#1A1A1A`, terracotta/clay `#C25A3F`/`#8C3B24`.
- **Tokens** — spacing (0–96), radius, shadow (xs→xl+inner), motion (100–500ms), z-index, breakpoints (sm 640→2xl 1536).
- **Component recipes** (Tailwind) — Button, Input, Card, Badge, Table, Modal, Sidebar, Topbar, StatCard, Avatar, Toast, FormGroup, Dropdown, Tabs, EmptyState, Stepper.
- **HR composites** — Employee Profile Header, ESS Request Card, Payroll Summary Row, Attendance Timeline, Workflow Builder Node, Document Template Card, Recruitment Pipeline Column, Report Chart Container.
- **Icons** — lucide-react, stroke 1.75; module→icon map.

## Related
[[Arabic RTL]] · [[Tech Stack]] · [[Development Standards]]
