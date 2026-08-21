"use client";

import { useContext } from "react";
import { LocaleContext, type LocaleContextValue } from "./locale-provider";

export function useT(): LocaleContextValue {
  const ctx = useContext(LocaleContext);
  if (!ctx) {
    throw new Error("useT must be used within a LocaleProvider");
  }
  return ctx;
}
