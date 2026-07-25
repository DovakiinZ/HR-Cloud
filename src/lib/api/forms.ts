import { apiFetch } from "../api-client";

// ── Forms engine (api/platform/forms) ──
// Dynamic form definitions consumed by Request Types and rendered for end users.

/** Classification values derived from MetadataJson by the backend. */
export type FieldClassification = "SystemRequired" | "BusinessRequired" | "Optional" | "Custom";

export interface FormField {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  fieldType: string; // Text|Number|Decimal|Date|DateTime|Boolean|Dropdown|MultiSelect|TextArea|Email|Phone|Url|Currency|Percentage|File|Image
  isRequired: boolean;
  sortOrder: number;
  sectionName?: string | null;
  placeholder?: string | null;
  defaultValue?: string | null;
  validationRules?: string | null; // JSON
  options?: string | null;         // JSON (e.g. select options, or {lookup:"slug"})
  /** Read-only classification exposed by the backend. Defaults to "Optional" for legacy fields. */
  classification: FieldClassification;
}

export interface FormDefinition {
  id: string;
  code: string;
  nameEn: string;
  nameAr: string;
  description?: string | null;
  module: string;
  version: number;
  isPublished: boolean;
  isActive: boolean;
  fields: FormField[];
}

interface Paginated<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
}

const BASE = "/api/platform/forms";

export function formLabel(f: FormDefinition): string {
  return f.nameAr || f.nameEn || f.code;
}

// Lightweight list for dropdowns (pull a generous page to cover all tenant forms).
export async function getFormDefinitions(module?: string): Promise<FormDefinition[]> {
  const q = new URLSearchParams({ pageNumber: "1", pageSize: "200" });
  if (module) q.set("module", module);
  const res = await apiFetch<Paginated<FormDefinition>>(`${BASE}?${q.toString()}`);
  return res?.items ?? [];
}

export async function getFormDefinition(id: string): Promise<FormDefinition> {
  return apiFetch<FormDefinition>(`${BASE}/${id}`);
}

// ── Form field CRUD ─────────────────────────────────────────────────────────────────

export const FIELD_TYPES = [
  "Text", "Number", "Decimal", "Date", "DateTime", "Boolean",
  "Dropdown", "MultiSelect", "TextArea", "Email", "Phone", "Url",
  "Currency", "Percentage", "File", "Image",
] as const;

export type FieldTypeName = typeof FIELD_TYPES[number];

export const FIELD_TYPE_LABEL: Record<string, string> = {
  Text: "نص", Number: "رقم صحيح", Decimal: "رقم عشري", Date: "تاريخ",
  DateTime: "تاريخ ووقت", Boolean: "نعم/لا", Dropdown: "قائمة منسدلة",
  MultiSelect: "اختيار متعدد", TextArea: "نص طويل", Email: "بريد إلكتروني",
  Phone: "هاتف", Url: "رابط", Currency: "عملة", Percentage: "نسبة مئوية",
  File: "ملف", Image: "صورة",
};

export interface AddFormFieldInput {
  code: string;
  nameEn: string;
  nameAr: string;
  fieldType: string;
  isRequired: boolean;
  sortOrder: number;
  sectionName?: string | null;
  placeholder?: string | null;
  defaultValue?: string | null;
  validationRules?: string | null;
  options?: string | null;
}

export interface UpdateFormFieldInput {
  /** Only sent for Custom-classified fields; omit otherwise. */
  code?: string | null;
  nameEn: string;
  nameAr: string;
  fieldType: string;
  isRequired: boolean;
  sortOrder: number;
  sectionName?: string | null;
  placeholder?: string | null;
  defaultValue?: string | null;
  validationRules?: string | null;
  options?: string | null;
}

export async function addFormField(formId: string, input: AddFormFieldInput): Promise<FormField> {
  return apiFetch<FormField>(`${BASE}/${formId}/fields`, { method: "POST", body: input });
}

export async function updateFormField(formId: string, fieldId: string, input: UpdateFormFieldInput): Promise<FormField> {
  return apiFetch<FormField>(`${BASE}/${formId}/fields/${fieldId}`, { method: "PUT", body: { id: fieldId, ...input } });
}

export async function deleteFormField(formId: string, fieldId: string): Promise<void> {
  return apiFetch<void>(`${BASE}/${formId}/fields/${fieldId}`, { method: "DELETE" });
}

export async function reorderFormFields(formId: string, fieldIds: string[]): Promise<void> {
  return apiFetch<void>(`${BASE}/${formId}/fields/reorder`, { method: "PUT", body: { formDefinitionId: formId, fieldIds } });
}

// ── Form submission ──────────────────────────────────────────────────────────────────

export interface FormSubmissionValueInput {
  formFieldId: string;
  fieldCode: string;
  value?: string | null;
  fileUrl?: string | null;
}

export interface FormSubmission {
  id: string;
  formDefinitionId: string;
  submittedById: string;
  submittedAt: string;
  status: string;
}

export async function submitForm(
  formDefinitionId: string,
  values: FormSubmissionValueInput[]
): Promise<FormSubmission> {
  return apiFetch<FormSubmission>(`${BASE}/${formDefinitionId}/submit`, {
    method: "POST",
    body: { formDefinitionId, values },
  });
}
