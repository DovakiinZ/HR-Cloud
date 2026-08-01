import Link from "next/link";
import { ChevronLeft, Database, Inbox, Wallet, CalendarDays, Landmark, FileText, LayoutTemplate, Bell, Clock, ShieldCheck } from "lucide-react";

const categories = [
  {
    href: "/settings/access",
    title: "المستخدمون والصلاحيات",
    description: "حسابات المستخدمين وربطهم بالموظفين، الأدوار، قوالب الصلاحيات (مصفوفة)، وسجل تدقيق الوصول",
    icon: ShieldCheck,
  },
  {
    href: "/settings/document-templates",
    title: "قوالب المستندات",
    description: "مصمّم مرئي بالسحب والإفلات لبناء المستندات الرسمية بالعناصر والرموز — بدون أي معرفة برمجية",
    icon: FileText,
  },
  {
    href: "/settings/page-templates",
    title: "قوالب الصفحات",
    description: "الترويسة والتذييل والهوامش والعلامة المائية وموضع الشعار/الختم — تُورّثها قوالب المستندات",
    icon: LayoutTemplate,
  },
  {
    href: "/settings/company-organization",
    title: "الشركة والمؤسسة",
    description: "هوية الشركة والشعار والختم والتواقيع والسجل/الضريبي والمعلومات النظامية والهيكل التنظيمي (الأقسام/الفروع/المسميات/الدرجات)",
    icon: Landmark,
  },
  {
    href: "/settings/leave",
    title: "إعدادات الإجازات",
    description: "أنواع الإجازات وقواعدها (مدفوعة/رصيد/مرفقات) وأرصدة الموظفين",
    icon: CalendarDays,
  },
  {
    href: "/settings/attendance/shifts",
    title: "إعدادات الورديات والحضور",
    description: "إنشاء الورديات (ثابتة/مرنة) وفترات السماح وقواعد التأخير والإضافي ونهاية الأسبوع، وتعيينها للموظفين والإدارات والفروع",
    icon: Clock,
  },
  {
    href: "/settings/attendance/permissions",
    title: "إعدادات الاستئذان",
    description: "أنواع الاستئذان وقواعدها: مدفوع/غير مدفوع، الحدود اليومية والشهرية، السلوك عند التجاوز، والأهلية (الشركة/الإدارات/الفروع/موظفون محددون)",
    icon: Clock,
  },
  {
    href: "/settings/payroll",
    title: "إعدادات الرواتب",
    description: "أنواع البدلات وقواعد احتسابها، مجموعات الرواتب، طرق الدفع",
    icon: Wallet,
  },
  {
    href: "/settings/requests",
    title: "إعدادات الطلبات",
    description: "أنواع الطلبات، الفئات، النماذج، مسارات الموافقة، اتفاقيات مستوى الخدمة",
    icon: Inbox,
  },
  {
    href: "/settings/notifications",
    title: "إعدادات التنبيهات",
    description: "قواعد التنبيهات — تنبيهات انتهاء مستندات الموظفين قبل المدة المحددة، والمستلمون والقنوات",
    icon: Bell,
  },
];

export default function SettingsPage() {
  return (
    <div className="space-y-8">
      <div>
        <h1 className="text-2xl font-bold">الإعدادات</h1>
        <p className="text-sm text-muted-foreground mt-1">تكوين وحدات النظام — كل وحدة بإعداداتها وقواعدها</p>
      </div>

      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
        {categories.map((c) => {
          const Icon = c.icon;
          return (
            <Link key={c.href} href={c.href} className="group border border-border bg-card p-5 hover:border-primary/50 hover:bg-card/70 transition-colors">
              <div className="flex items-start justify-between">
                <div className="flex h-10 w-10 items-center justify-center bg-primary/10 text-primary"><Icon className="h-5 w-5" /></div>
                <ChevronLeft className="h-4 w-4 text-muted-foreground group-hover:text-primary transition-colors" />
              </div>
              <h2 className="text-base font-bold mt-4">{c.title}</h2>
              <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{c.description}</p>
            </Link>
          );
        })}
      </div>

      {/* Advanced raw catalog editor (covers catalog types without a dedicated page yet) */}
      <div className="border-t border-border pt-5">
        <Link href="/settings/master-data" className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors">
          <Database className="h-4 w-4" />
          محرّر البيانات الرئيسية المتقدم (كل القوائم)
        </Link>
      </div>
    </div>
  );
}
