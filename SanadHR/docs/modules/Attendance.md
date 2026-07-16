---
title: Attendance
aliases: [Attendance Module, HR.Modules.Attendance, Time Tracking]
tags: [module]
---

# Attendance

> Punches → computed daily records → payroll impact. The feeder for [[Payroll Engine|payroll]] deductions and overtime.
> Up: [[MODULE_INDEX]] · Lifecycle: [[Attendance Lifecycle]] · Payroll link: [[Attendance Payroll Impact]]

## Purpose
Capture time (biometric/geo/GPS/manual), validate against shifts, aggregate worked/overtime/absence/late/shortage per period, and materialize **visible payroll impacts** (no hidden deductions).

## Architecture
`HR.Modules.Attendance` — `AttendanceService`, `AttendanceCalculationService`, `ShiftResolver`, `AttendanceExporter`; controllers `Attendance`, `Shifts`, settings.

## Entities
`AttendanceRecord`, `AttendancePunch`, `AttendanceCorrection`, `AttendancePolicy`, `AttendanceHoliday`, `AttendanceAuditLog`, `Shift`, `ShiftAssignment` (`HR.Domain/Engines/Attendance/`). Leave siblings: `LeaveRecord`, `LeaveBalance`.

## Services
`AttendanceCalculationService` (punches→records); `ShiftResolver`; and the cross-module `IAttendancePayrollSyncService` + `AttendanceWageCalculator` (shared wage math) that turn penalties/overtime into [[Payroll Additions and Deductions|PayrollTransaction]] records → [[Attendance Payroll Impact]].

## Events
Attendance changes drive payroll sync at Calculate-time and on-demand; approved excuse/leave [[Completion Effects Engine|completion effects]] zero penalty minutes → cancel-on-zero.

## Dependencies
[[Payroll Engine]] (impact sync), [[Completion Effects Engine]] (excuse/leave executors), [[Master Data Engine]] (ABSENCE/LATE/SHORTAGE deduction types, OVERTIME addition type).

## API
`api/attendance`, `api/shifts`, attendance settings, `api/attendance/payroll-impact/sync` (2E daily action, permission `Attendance.PayrollImpact.Create`). → [[API Endpoint Map]]. Frontend: `/attendance`, `/settings/attendance/shifts`.

## Current Status
✅ Built + deployed; ✅ 2D attendance→deduction records shipped; 🔧 2E daily actions + overtime + excuse-fix built. Tests: attendance-payroll integration suite ([[Test Suite]]).

## Future Work
Biometric integrations, geofence, mobile GPS, anomaly detection → [[ROADMAP]].

## Related Notes
[[Attendance Lifecycle]] · [[Attendance Payroll Impact]] · [[Subproject 2D Attendance Deduction Records]] · [[Subproject 2E Attendance Daily Overtime Excuse]] · [[Payroll Engine]]
