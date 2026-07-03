import { Badge } from "@/components/ui/badge";
import { CheckCircle2, AlertTriangle, Loader2, XCircle } from "lucide-react";
import type { CalculationStatus } from "@/lib/api/payroll";

/** Client-transient states not persisted by the backend */
export type CalcStatusExtended = CalculationStatus | "Calculating" | "Failed";

const STYLES: Record<CalcStatusExtended, string> = {
  UpToDate: "bg-green-500/10 text-green-600 border-green-500/20",
  RecalculationRequired: "bg-amber-500/10 text-amber-600 border-amber-500/20",
  Calculating: "bg-blue-500/10 text-blue-600 border-blue-500/20",
  Failed: "bg-destructive/10 text-destructive border-destructive/20",
};

const LABELS: Record<CalcStatusExtended, string> = {
  UpToDate: "محدّث",
  RecalculationRequired: "يتطلب إعادة احتساب",
  Calculating: "جاري الاحتساب…",
  Failed: "فشل الاحتساب",
};

const ICONS: Record<CalcStatusExtended, React.ReactNode> = {
  UpToDate: <CheckCircle2 className="h-3 w-3" />,
  RecalculationRequired: <AlertTriangle className="h-3 w-3" />,
  Calculating: <Loader2 className="h-3 w-3 animate-spin" />,
  Failed: <XCircle className="h-3 w-3" />,
};

export function CalcStatusBadge({ status }: { status: CalcStatusExtended }) {
  return (
    <Badge variant="outline" className={`gap-1 text-xs ${STYLES[status]}`}>
      {ICONS[status]}
      {LABELS[status]}
    </Badge>
  );
}
