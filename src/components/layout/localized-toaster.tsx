"use client";

import { Toaster } from "sonner";
import { useT } from "@/lib/i18n/use-t";

export function LocalizedToaster() {
  const { dir } = useT();
  return (
    <Toaster
      position="top-center"
      dir={dir}
      theme="light"
      richColors
      closeButton
      toastOptions={{ style: { fontFamily: "inherit" } }}
    />
  );
}
