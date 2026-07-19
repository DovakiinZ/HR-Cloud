---
title: Attendance Lifecycle
aliases: [Attendance Flow, Time Tracking]
tags: [domain, lifecycle]
---

# Attendance Lifecycle

> Up: [[DOMAIN_MAP]] · Module: [[Attendance]] · Payroll link: [[Attendance Payroll Impact]]

```
Capture → Validate → Aggregate → Payroll Impact
```

- **Capture** — biometric machines, geo-fence, mobile/GPS, manual entry.
- **Validate** — against assigned shift, geofence boundaries, GPS.
- **Aggregate** — worked hours, overtime, absences, late/shortage per period.
- **Payroll Impact** — penalties and overtime become **visible [[Payroll Additions and Deductions|PayrollTransaction]] records** (no hidden deductions) via the [[Attendance Payroll Impact|sync service]].

**Rules**
- Shifts define expected hours; deviations produce overtime or deductions.
- Geofence/GPS validation gates mobile check-ins.
- Approved excuse/leave **cancels** the related deduction before posting ([[Subproject 2E Attendance Daily Overtime Excuse]]).

## Related
[[Attendance]] · [[Attendance Payroll Impact]] · [[Payroll Lifecycle]] · [[Subproject 2D Attendance Deduction Records]]
