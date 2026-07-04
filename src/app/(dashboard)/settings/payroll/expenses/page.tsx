import { SimpleMasterDataList } from "@/components/settings/simple-master-data-list";

export default function ExpenseTypesPage() {
  return (
    <SimpleMasterDataList
      objectType="ExpenseCategory"
      title="فئات المصروفات"
      description="أنواع المصروفات (سفر، إقامة، تدريب…) المتاحة عند إضافة مصروف"
      backHref="/settings/payroll"
      backLabel="إعدادات الرواتب"
    />
  );
}
