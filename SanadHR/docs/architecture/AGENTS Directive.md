---
title: AGENTS Directive
aliases: [AGENTS, Next.js 16 Warning]
tags: [rules, frontend]
---

# AGENTS Directive — "This is NOT the Next.js you know"

> The single rule in `AGENTS.md`, surfaced as its own note because it bites often.
> Up: [[CLAUDE]] · Stack: [[Tech Stack]]

**Next.js 16 has breaking changes** vs training data — APIs, conventions, and file structure may all differ. **Read the relevant guide in `node_modules/next/dist/docs/` before writing any frontend code.** Heed deprecation notices.

The app uses the App Router with route groups `(auth)` and `(dashboard)`; root layout is `<html lang="ar" dir="rtl">`. Dev server runs on **port 3001**. See [[Employees]]/[[ESS]] frontend notes for route structure and [[Deployment and Infrastructure]] for the local CORS proxy.

## Related
[[Tech Stack]] · [[CLAUDE]] · [[Arabic RTL]]
