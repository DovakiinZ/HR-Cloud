"use client";

import {
  Area,
  AreaChart,
  Bar,
  BarChart,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  PolarAngleAxis,
  RadialBar,
  RadialBarChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { SeriesPoint, WidgetDataResult } from "@/types/dashboard";

// Palette tuned for the dark/amber theme.
const COLORS = [
  "#FBBF24", "#60A5FA", "#34D399", "#F87171", "#A78BFA",
  "#FB923C", "#22D3EE", "#F472B6", "#A3E635", "#94A3B8",
];

export function formatNumber(n: number | null | undefined): string {
  if (n === null || n === undefined || Number.isNaN(n)) return "—";
  const abs = Math.abs(n);
  if (abs >= 1_000_000) return (n / 1_000_000).toFixed(abs >= 10_000_000 ? 0 : 1) + "M";
  if (abs >= 10_000) return (n / 1_000).toFixed(abs >= 100_000 ? 0 : 1) + "K";
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(n);
}

interface RendererProps {
  type: string; // widget type / visualization name
  result: WidgetDataResult;
  onSelect?: (point: SeriesPoint) => void;
}

export function WidgetRenderer({ type, result, onSelect }: RendererProps) {
  const isEmpty =
    (result.kind === "series" && result.series.length === 0) ||
    (result.kind === "table" && result.rows.length === 0);

  if (isEmpty) {
    return <div className="flex h-full min-h-24 items-center justify-center text-sm text-muted-foreground">لا توجد بيانات</div>;
  }

  // ── Scalar KPI / Gauge ──
  if (result.kind === "scalar") {
    const pct = (result.aggregation || "").toLowerCase() === "percentage";
    if (type === "Gauge") return <Gauge value={result.value ?? 0} percentage={pct} />;
    return (
      <div className="flex h-full flex-col items-center justify-center py-2">
        <span
          className={`text-4xl font-bold tabular-nums${onSelect ? " cursor-pointer hover:opacity-80" : ""}`}
          {...(onSelect ? {
            role: "button",
            title: "عرض التفاصيل",
            onClick: () => onSelect({ key: "", label: "", value: result.value ?? 0 }),
          } : {})}
        >
          {pct ? `${formatNumber(result.value)}%` : formatNumber(result.value)}
        </span>
      </div>
    );
  }

  // ── Table ──
  if (result.kind === "table") {
    return (
      <div className="h-full overflow-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-right text-xs font-bold uppercase tracking-wider text-muted-foreground">
              {result.columns.map((c) => (
                <th key={c.code} className="px-2 py-2 whitespace-nowrap">{c.label}</th>
              ))}
            </tr>
          </thead>
          <tbody>
            {result.rows.map((row, i) => (
              <tr
                key={i}
                className={`border-b border-border/40${onSelect ? " cursor-pointer hover:bg-muted/50" : " hover:bg-muted/40"}`}
                {...(onSelect ? {
                  onClick: () => onSelect({ key: "", label: String(Object.values(row)[0] ?? ""), value: 0 }),
                } : {})}
              >
                {result.columns.map((c) => (
                  <td key={c.code} className="px-2 py-1.5 whitespace-nowrap">{renderCell(row[c.code])}</td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  // ── Series → chart by type ──
  const data = result.series;
  const click = (p?: SeriesPoint) => p && onSelect?.(p);

  switch (type) {
    case "PieChart":
    case "DonutChart":
      return <PieLike data={data} donut={type === "DonutChart"} onSelect={click} />;
    case "LineChart":
      return <Lined data={data} area={false} />;
    case "TrendChart":
    case "AreaChart":
      return <Lined data={data} area />;
    case "ProgressWidget":
      return <Progress data={data} onSelect={click} />;
    case "Leaderboard":
      return <Leaderboard data={data} onSelect={click} />;
    case "Table":
      return <SeriesTable data={data} onSelect={click} />;
    case "HorizontalBar":
      return <Bars data={data} onSelect={click} horizontal />;
    case "BarChart":
    default:
      return <Bars data={data} onSelect={click} />;
  }
}

function Gauge({ value, percentage }: { value: number; percentage: boolean }) {
  const max = percentage ? 100 : Math.max(value * 1.25, 1);
  const data = [{ name: "v", value, fill: "#FBBF24" }];
  return (
    <div className="relative h-full">
      <ResponsiveContainer width="100%" height="100%">
        <RadialBarChart innerRadius="70%" outerRadius="100%" data={data} startAngle={210} endAngle={-30}>
          <PolarAngleAxis type="number" domain={[0, max]} tick={false} />
          <RadialBar dataKey="value" cornerRadius={4} background={{ fill: "#27272A" }} />
        </RadialBarChart>
      </ResponsiveContainer>
      <div className="pointer-events-none absolute inset-0 flex items-center justify-center">
        <span className="text-3xl font-bold tabular-nums">{formatNumber(value)}{percentage ? "%" : ""}</span>
      </div>
    </div>
  );
}

function Leaderboard({ data, onSelect }: { data: SeriesPoint[]; onSelect: (p?: SeriesPoint) => void }) {
  const max = Math.max(...data.map((d) => d.value), 1);
  return (
    <div className="flex h-full flex-col gap-2 overflow-auto py-1">
      {data.map((d, i) => (
        <button key={d.key} onClick={() => onSelect(d)} className="flex items-center gap-2 text-right">
          <span className={`flex h-6 w-6 shrink-0 items-center justify-center text-xs font-bold ${i < 3 ? "bg-primary text-primary-foreground" : "bg-secondary text-muted-foreground"}`}>{i + 1}</span>
          <div className="min-w-0 flex-1">
            <div className="flex items-center justify-between text-sm">
              <span className="truncate">{d.label}</span>
              <span className="text-xs tabular-nums text-muted-foreground">{formatNumber(d.value)}</span>
            </div>
            <div className="mt-1 h-1 w-full overflow-hidden bg-secondary">
              <div className="h-full bg-primary" style={{ width: `${(d.value / max) * 100}%` }} />
            </div>
          </div>
        </button>
      ))}
    </div>
  );
}

function renderCell(v: unknown) {
  if (v === null || v === undefined) return <span className="text-muted-foreground">—</span>;
  if (typeof v === "boolean") return v ? "نعم" : "لا";
  if (typeof v === "number") return new Intl.NumberFormat("en-US", { maximumFractionDigits: 2 }).format(v);
  return String(v);
}

const tooltipStyle = {
  contentStyle: { background: "#212125", border: "1px solid #3F3F46", borderRadius: 0, fontSize: 12 },
  labelStyle: { color: "#FAFAFA" },
  itemStyle: { color: "#FAFAFA" },
};

function Bars({ data, onSelect, horizontal }: { data: SeriesPoint[]; onSelect: (p?: SeriesPoint) => void; horizontal?: boolean }) {
  if (horizontal) {
    return (
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={data} layout="vertical" margin={{ top: 4, right: 12, left: 4, bottom: 4 }}>
          <XAxis type="number" tick={{ fontSize: 11, fill: "#A1A1AA" }} allowDecimals={false} />
          <YAxis type="category" dataKey="label" tick={{ fontSize: 11, fill: "#A1A1AA" }} width={90} />
          <Tooltip {...tooltipStyle} cursor={{ fill: "rgba(255,255,255,0.04)" }} />
          <Bar dataKey="value" radius={[0, 2, 2, 0]} onClick={(_, i) => onSelect(data[i])} cursor="pointer">
            {data.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
          </Bar>
        </BarChart>
      </ResponsiveContainer>
    );
  }
  return (
    <ResponsiveContainer width="100%" height="100%">
      <BarChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <XAxis dataKey="label" tick={{ fontSize: 11, fill: "#A1A1AA" }} interval={0} angle={data.length > 5 ? -25 : 0} textAnchor={data.length > 5 ? "end" : "middle"} height={data.length > 5 ? 50 : 24} />
        <YAxis tick={{ fontSize: 11, fill: "#A1A1AA" }} allowDecimals={false} />
        <Tooltip {...tooltipStyle} cursor={{ fill: "rgba(255,255,255,0.04)" }} />
        <Bar dataKey="value" radius={[2, 2, 0, 0]} onClick={(_, i) => onSelect(data[i])} cursor="pointer">
          {data.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

function PieLike({ data, donut, onSelect }: { data: SeriesPoint[]; donut: boolean; onSelect: (p?: SeriesPoint) => void }) {
  return (
    <ResponsiveContainer width="100%" height="100%">
      <PieChart>
        <Pie
          data={data}
          dataKey="value"
          nameKey="label"
          innerRadius={donut ? "55%" : 0}
          outerRadius="80%"
          paddingAngle={1}
          onClick={(_, i) => onSelect(data[i])}
          cursor="pointer"
        >
          {data.map((_, i) => <Cell key={i} fill={COLORS[i % COLORS.length]} />)}
        </Pie>
        <Tooltip {...tooltipStyle} />
        <Legend wrapperStyle={{ fontSize: 11 }} />
      </PieChart>
    </ResponsiveContainer>
  );
}

function Lined({ data, area }: { data: SeriesPoint[]; area: boolean }) {
  if (area) {
    return (
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
          <XAxis dataKey="label" tick={{ fontSize: 11, fill: "#A1A1AA" }} />
          <YAxis tick={{ fontSize: 11, fill: "#A1A1AA" }} allowDecimals={false} />
          <Tooltip {...tooltipStyle} />
          <Area type="monotone" dataKey="value" stroke="#FBBF24" fill="rgba(251,191,36,0.18)" strokeWidth={2} />
        </AreaChart>
      </ResponsiveContainer>
    );
  }
  return (
    <ResponsiveContainer width="100%" height="100%">
      <LineChart data={data} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
        <XAxis dataKey="label" tick={{ fontSize: 11, fill: "#A1A1AA" }} />
        <YAxis tick={{ fontSize: 11, fill: "#A1A1AA" }} allowDecimals={false} />
        <Tooltip {...tooltipStyle} />
        <Line type="monotone" dataKey="value" stroke="#FBBF24" strokeWidth={2} dot={{ r: 2 }} />
      </LineChart>
    </ResponsiveContainer>
  );
}

function Progress({ data, onSelect }: { data: SeriesPoint[]; onSelect: (p?: SeriesPoint) => void }) {
  const max = Math.max(...data.map((d) => d.value), 1);
  return (
    <div className="flex h-full flex-col justify-center gap-3 py-1">
      {data.slice(0, 8).map((d, i) => (
        <button key={d.key} onClick={() => onSelect(d)} className="space-y-1 text-right">
          <div className="flex items-center justify-between text-sm">
            <span className="truncate">{d.label}</span>
            <span className="text-xs tabular-nums text-muted-foreground">{formatNumber(d.value)}</span>
          </div>
          <div className="h-1.5 w-full overflow-hidden bg-secondary">
            <div className="h-full" style={{ width: `${(d.value / max) * 100}%`, background: COLORS[i % COLORS.length] }} />
          </div>
        </button>
      ))}
    </div>
  );
}

function SeriesTable({ data, onSelect }: { data: SeriesPoint[]; onSelect: (p?: SeriesPoint) => void }) {
  return (
    <div className="h-full overflow-auto">
      <table className="w-full text-sm">
        <tbody>
          {data.map((d) => (
            <tr key={d.key} onClick={() => onSelect(d)} className="cursor-pointer border-b border-border/40 hover:bg-muted/40">
              <td className="px-2 py-1.5">{d.label}</td>
              <td className="px-2 py-1.5 text-left tabular-nums font-medium">{formatNumber(d.value)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
