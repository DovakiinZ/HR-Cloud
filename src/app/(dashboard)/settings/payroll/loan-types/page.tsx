import { SimpleMasterDataList } from "@/components/settings/simple-master-data-list";

export default function LoanTypesPage() {
  return (
    <SimpleMasterDataList
      objectType="LoanType"
      title="أنواع القروض والسلف"
      description="تصنيفات القروض والسلف (شخصي، طارئ…) المتاحة عند إضافة قرض/سلفة"
      backHref="/settings/payroll"
      backLabel="إعدادات الرواتب"
    />
  );
}
