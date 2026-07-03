import type { RunKpis } from "@/lib/api/payroll";
import { money } from "@/lib/api/payroll";
import { Users, UserX, TrendingUp, TrendingDown, Banknote, Receipt, Clock } from "lucide-react";

interface KpiCardProps {
  label: string;
  value: string;
  icon: React.ReactNode;
  accent?: boolean;
  muted?: boolean;
}

function KpiCard({ label, value, icon, accent, muted }: KpiCardProps) {
  return (
    <div className="border border-border bg-card p-4 flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <span className="text-xs text-muted-foreground">{label}</span>
        <span className={`${muted ? "text-muted-foreground/50" : accent ? "text-primary" : "text-muted-foreground"}`}>
          {icon}
        </span>
      </div>
      <div className={`text-lg font-bold tabular-nums leading-tight ${accent ? "text-primary" : muted ? "text-muted-foreground" : ""}`}>
        {value}
      </div>
    </div>
  );
}

interface RunKpiCardsProps {
  kpis: RunKpis;
  currency: string;
}

export function RunKpiCards({ kpis, currency }: RunKpiCardsProps) {
  return (
    <div className="grid gap-3 grid-cols-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-7">
      <KpiCard
        label="الموظفون المشمولون"
        value={kpis.includedEmployees.toLocaleString("ar-SA")}
        icon={<Users className="h-4 w-4" />}
      />
      <KpiCard
        label="المستثنون"
        value={kpis.excludedEmployees.toLocaleString("ar-SA")}
        icon={<UserX className="h-4 w-4" />}
        muted={kpis.excludedEmployees === 0}
      />
      <KpiCard
        label="الإجمالي"
        value={money(kpis.gross, currency)}
        icon={<TrendingUp className="h-4 w-4" />}
      />
      <KpiCard
        label="إجمالي الاستقطاعات"
        value={money(kpis.deductions, currency)}
        icon={<TrendingDown className="h-4 w-4" />}
      />
      <KpiCard
        label="الصافي"
        value={money(kpis.net, currency)}
        icon={<Banknote className="h-4 w-4" />}
        accent
      />
      <KpiCard
        label="حركات مستهلكة"
        value={kpis.transactionsConsumed.toLocaleString("ar-SA")}
        icon={<Receipt className="h-4 w-4" />}
      />
      <KpiCard
        label="معتمدة غير مستهلكة"
        value={kpis.approvedNotConsumed.toLocaleString("ar-SA")}
        icon={<Clock className="h-4 w-4" />}
        muted={kpis.approvedNotConsumed === 0}
      />
    </div>
  );
}
