import type { Locale } from "./config";

export interface BilingualEntity {
  name?: string | null;
  nameAr?: string | null;
  nameEn?: string | null;
  description?: string | null;
  descriptionAr?: string | null;
  descriptionEn?: string | null;
}

export function localizedName(
  entity: BilingualEntity | null | undefined,
  locale: Locale,
): string {
  if (!entity) return "";
  if (locale === "en" && entity.nameEn) return entity.nameEn;
  return entity.nameAr || entity.name || entity.nameEn || "";
}

export function localizedDescription(
  entity: BilingualEntity | null | undefined,
  locale: Locale,
): string {
  if (!entity) return "";
  if (locale === "en" && entity.descriptionEn) return entity.descriptionEn;
  return entity.descriptionAr || entity.description || entity.descriptionEn || "";
}
