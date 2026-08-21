import type { Locale } from "./config";
import type { Messages } from "./translate";

import arCommon from "@/locales/ar/common.json";
import arNavigation from "@/locales/ar/navigation.json";
import arValidation from "@/locales/ar/validation.json";
import enCommon from "@/locales/en/common.json";
import enNavigation from "@/locales/en/navigation.json";
import enValidation from "@/locales/en/validation.json";

// Each catalog file uses a distinct top-level namespace, so a shallow
// merge is sufficient. New module slices add their file to both locales here.
export const catalogs: Record<Locale, Messages> = {
  ar: { ...arCommon, ...arNavigation, ...arValidation },
  en: { ...enCommon, ...enNavigation, ...enValidation },
};
