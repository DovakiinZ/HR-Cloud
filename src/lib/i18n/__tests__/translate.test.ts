import { describe, it, expect } from "vitest";
import { translate, lookup } from "../translate";
import type { Locale } from "../config";

const catalogs = {
  ar: { common: { save: "حفظ" }, employee: { department: "القسم" } },
  en: { common: { save: "Save" } },
} as Record<Locale, Record<string, unknown>>;

describe("translate", () => {
  it("returns the value for the active locale", () => {
    expect(translate(catalogs, "en", "common.save")).toBe("Save");
    expect(translate(catalogs, "ar", "common.save")).toBe("حفظ");
  });

  it("falls back to Arabic when the English key is missing", () => {
    expect(translate(catalogs, "en", "employee.department")).toBe("القسم");
  });

  it("falls back to the raw key when missing in both locales", () => {
    expect(translate(catalogs, "en", "nope.here")).toBe("nope.here");
  });

  it("interpolates {param} tokens", () => {
    const c = { ar: { greet: "مرحبا {name}" }, en: { greet: "Hi {name}" } } as Record<Locale, Record<string, unknown>>;
    expect(translate(c, "en", "greet", { name: "Sara" })).toBe("Hi Sara");
  });

  it("lookup returns undefined for a non-string node", () => {
    expect(lookup(catalogs.ar, "common")).toBeUndefined();
  });
});
