import { Badge } from "@/components/ui/badge";
import { STATE_AR } from "@/lib/api/payroll";

const STATE_STYLES: Record<string, string> = {
  Draft: "bg-zinc-500/10 text-zinc-500 border-zinc-500/20",
  Preview: "bg-blue-500/10 text-blue-600 border-blue-500/20",
  Validated: "bg-cyan-500/10 text-cyan-600 border-cyan-500/20",
  PendingApproval: "bg-amber-500/10 text-amber-600 border-amber-500/20",
  Approved: "bg-indigo-500/10 text-indigo-600 border-indigo-500/20",
  Executing: "bg-purple-500/10 text-purple-600 border-purple-500/20",
  Completed: "bg-green-500/10 text-green-600 border-green-500/20",
  Locked: "bg-green-600/10 text-green-700 border-green-600/20",
  Archived: "bg-zinc-500/10 text-zinc-400 border-zinc-500/20",
  Failed: "bg-destructive/10 text-destructive border-destructive/20",
  Cancelled: "bg-zinc-500/10 text-zinc-400 border-zinc-500/20",
  Voided: "bg-rose-500/10 text-rose-600 border-rose-500/20",
};

export function StateBadge({ state }: { state: string }) {
  return <Badge variant="outline" className={`text-xs ${STATE_STYLES[state] ?? ""}`}>{STATE_AR[state] ?? state}</Badge>;
}
