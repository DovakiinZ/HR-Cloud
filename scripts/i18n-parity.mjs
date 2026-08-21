import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";

const ROOT = "src/locales";
const LOCALES = ["ar", "en"];

function flatten(obj, prefix = "") {
  const out = {};
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k;
    if (v && typeof v === "object" && !Array.isArray(v)) {
      Object.assign(out, flatten(v, key));
    } else {
      out[key] = v;
    }
  }
  return out;
}

function loadLocale(locale) {
  const dir = join(ROOT, locale);
  const keys = {};
  for (const file of readdirSync(dir).filter((f) => f.endsWith(".json"))) {
    const json = JSON.parse(readFileSync(join(dir, file), "utf8"));
    Object.assign(keys, flatten(json));
  }
  return keys;
}

const ar = loadLocale("ar");
const en = loadLocale("en");
const arKeys = new Set(Object.keys(ar));
const enKeys = new Set(Object.keys(en));

const missingInEn = [...arKeys].filter((k) => !enKeys.has(k));
const missingInAr = [...enKeys].filter((k) => !arKeys.has(k));
const emptyEn = [...enKeys].filter((k) => en[k] === "");
const emptyAr = [...arKeys].filter((k) => ar[k] === "");

let failed = false;
const report = (label, arr) => {
  if (arr.length) {
    failed = true;
    console.error(`\n${label} (${arr.length}):`);
    for (const k of arr) console.error(`  - ${k}`);
  }
};

report("Keys present in ar but missing in en", missingInEn);
report("Keys present in en but missing in ar", missingInAr);
report("Empty string values in en", emptyEn);
report("Empty string values in ar", emptyAr);

if (failed) {
  console.error("\ni18n parity check FAILED");
  process.exit(1);
}
console.log(`i18n parity OK — ${arKeys.size} keys in both locales`);
