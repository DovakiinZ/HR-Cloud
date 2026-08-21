import { describe, it, expect } from "vitest";
import { formatNumber, formatCurrency, formatDate } from "../format";

const ARABIC_INDIC = /[٠-٩]/;

describe("formatting uses Latin digits in both locales", () => {
  it("formats numbers with Latin digits in Arabic", () => {
    const out = formatNumber(1234.5, "ar");
    expect(out).toContain("1,234.5");
    expect(ARABIC_INDIC.test(out)).toBe(false);
  });

  it("formats currency as SAR with Latin digits", () => {
    const ar = formatCurrency(1234, "ar");
    const en = formatCurrency(1234, "en");
    expect(ar).toContain("1,234");
    expect(en).toContain("1,234");
    expect(ARABIC_INDIC.test(ar)).toBe(false);
  });

  it("formats a date with Latin digits and accepts an ISO string", () => {
    const out = formatDate("2026-01-15T00:00:00Z", "ar", { year: "numeric", month: "2-digit", day: "2-digit", timeZone: "UTC" });
    expect(out).toContain("2026");
    expect(ARABIC_INDIC.test(out)).toBe(false);
  });
});
