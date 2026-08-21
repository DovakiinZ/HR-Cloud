import { readdirSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";

// Usage: node scripts/i18n-no-hardcoded.mjs <dir> [<dir> ...]
const targets = process.argv.slice(2);
if (targets.length === 0) {
  console.error("Usage: node scripts/i18n-no-hardcoded.mjs <dir> [<dir> ...]");
  process.exit(2);
}

const ARABIC = /[؀-ۿ]/;

function walk(dir) {
  const files = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) files.push(...walk(full));
    else if (full.endsWith(".tsx")) files.push(full);
  }
  return files;
}

let offenders = 0;
for (const dir of targets) {
  for (const file of walk(dir)) {
    const lines = readFileSync(file, "utf8").split("\n");
    lines.forEach((line, i) => {
      // Skip the allowlist marker for intentional literals (e.g. brand text).
      if (line.includes("i18n-allow")) return;
      if (ARABIC.test(line)) {
        offenders++;
        console.error(`${file}:${i + 1}: ${line.trim()}`);
      }
    });
  }
}

if (offenders > 0) {
  console.error(`\nFound ${offenders} hard-coded Arabic literal(s). Move them to a catalog or add an "i18n-allow" comment if intentional.`);
  process.exit(1);
}
console.log("No hard-coded Arabic literals found in target(s).");
