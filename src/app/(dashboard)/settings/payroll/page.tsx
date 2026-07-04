import Link from "next/link";
import { ArrowRight, Wallet, Coins, CreditCard, Users, ChevronLeft, LayoutList, TrendingUp, Receipt, HandCoins } from "lucide-react";

const modules = [
  {
    href: "/settings/payroll/types",
    title: "أنواع المسير",
    description: "Payroll Types — عرّف أنواع المسيرات (شهري، مكافآت، نهاية خدمة…) وإصداراتها ونطاق الموظفين",
    icon: LayoutList,
  },
  {
    href: "/settings/payroll/allowances",
    title: "البدلات",
    description: "أنواع البدلات وقواعد الاحتساب (ثابت/نسبة)، الأساس، GOSI، الضريبة، السماح بالتجاوز",
    icon: Wallet,
  },
  {
    href: "/settings/payroll/additions",
    title: "الإضافات",
    description: "أنواع الإضافات (مكافآت، عمولات، عمل إضافي، حوافز)",
    icon: TrendingUp,
  },
  {
    href: "/settings/payroll/deductions",
    title: "الاستقطاعات",
    description: "أنواع الاستقطاعات (تأمينات، سلف، جزاءات)",
    icon: Coins,
  },
  {
    href: "/settings/payroll/expenses",
    title: "فئات المصروفات",
    description: "أنواع المصروفات (سفر، إقامة، تدريب…) المتاحة عند إضافة مصروف",
    icon: Receipt,
  },
  {
    href: "/settings/payroll/loan-types",
    title: "أنواع القروض والسلف",
    description: "تصنيفات القروض والسلف المتاحة عند إضافة قرض/سلفة",
    icon: HandCoins,
  },
  {
    href: "/settings/payroll/groups",
    title: "مجموعات الرواتب",
    description: "مجموعات صرف الرواتب ودوراتها",
    icon: Users,
  },
  {
    href: "/settings/payroll/payment-methods",
    title: "طرق الدفع",
    description: "تحويل بنكي، نقدي، بطاقة راتب",
    icon: CreditCard,
  },
];

export default function PayrollSettingsPage() {
  return (
    <div className="space-y-6">
      <div className="flex items-center gap-2 text-sm">
        <Link href="/settings" className="text-muted-foreground hover:text-foreground transition-colors flex items-center gap-1">
          <ArrowRight className="h-4 w-4" /> الإعدادات
        </Link>
        <span className="text-muted-foreground">/</span>
        <span>إعدادات الرواتب</span>
      </div>

      <div>
        <h1 className="text-2xl font-bold">إعدادات الرواتب</h1>
        <p className="text-sm text-muted-foreground mt-1">البدلات والاستقطاعات ومجموعات الرواتب وطرق الدفع</p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {modules.map((m) => {
          const Icon = m.icon;
          return (
            <Link key={m.href} href={m.href} className="group border border-border bg-card p-5 hover:border-primary/50 hover:bg-card/70 transition-colors">
              <div className="flex items-start justify-between">
                <div className="flex h-10 w-10 items-center justify-center bg-primary/10 text-primary"><Icon className="h-5 w-5" /></div>
                <ChevronLeft className="h-4 w-4 text-muted-foreground group-hover:text-primary transition-colors" />
              </div>
              <h2 className="text-base font-bold mt-4">{m.title}</h2>
              <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{m.description}</p>
            </Link>
          );
        })}
      </div>
    </div>
  );
}
