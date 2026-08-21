import { describe, it, expect } from "vitest";
import { localizedName, localizedDescription } from "../localized-name";

describe("localizedName", () => {
  it("prefers nameEn in English when present", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "Department" }, "en")).toBe("Department");
  });

  it("falls back to Arabic when English is empty", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "" }, "en")).toBe("القسم");
    expect(localizedName({ nameAr: "القسم" }, "en")).toBe("القسم");
  });

  it("uses nameAr in Arabic even if English exists", () => {
    expect(localizedName({ nameAr: "القسم", nameEn: "Department" }, "ar")).toBe("القسم");
  });

  it("falls back to legacy `name`, then to empty string", () => {
    expect(localizedName({ name: "Legacy" }, "en")).toBe("Legacy");
    expect(localizedName(null, "en")).toBe("");
    expect(localizedName({}, "ar")).toBe("");
  });

  it("localizedDescription mirrors the same rules", () => {
    expect(localizedDescription({ descriptionAr: "وصف", descriptionEn: "Desc" }, "en")).toBe("Desc");
    expect(localizedDescription({ descriptionAr: "وصف" }, "en")).toBe("وصف");
  });
});
