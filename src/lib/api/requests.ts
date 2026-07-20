import { getLookup, LookupItem } from "./lookups";

// ── Request Types ──
//
// Request types are NOT master data. They are the RequestType entity in engine_request_types, and
// the client for them is ./request-types.ts. This module previously exposed CRUD over
// MasterDataItem rows with ObjectType="RequestType" — rows that no backend code ever read, so
// editing a "request type" here changed nothing about any actual request. Those helpers are gone;
// what remains are the two read-only lookup feeds that genuinely are master data.

export const REQUEST_CATEGORY = "RequestCategory";

// Active request types for the employee portal (read-only lookup feed, served from the real
// RequestType entity by the lookups endpoint).
export async function getRequestTypeLookup(): Promise<LookupItem[]> {
  return getLookup("request-types");
}

export async function getRequestCategoryLookup(): Promise<LookupItem[]> {
  return getLookup("request-categories");
}
