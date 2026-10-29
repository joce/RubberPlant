// Validates .gltf and .glb files with the Khronos glTF Validator (docs/backends.md §9).
// Usage: node validate.mjs <file or directory>...
// Prints every issue; exits with 1 if any file has an error, or if there are no files.
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import validator from "gltf-validator";

const files = process.argv.slice(2).flatMap((arg) =>
  statSync(arg).isDirectory()
    ? readdirSync(arg)
        .filter((f) => /\.(gltf|glb)$/i.test(f))
        .sort()
        .map((f) => join(arg, f))
    : [arg],
);

if (files.length === 0) {
  console.error("no .gltf or .glb files to validate");
  process.exit(1);
}

let failed = 0;
for (const file of files) {
  const report = await validator.validateBytes(new Uint8Array(readFileSync(file)), {
    uri: file,
    maxIssues: 0,
  });
  const { numErrors, numWarnings, numInfos, numHints } = report.issues;
  const status = numErrors > 0 ? "ERROR" : "ok   ";
  console.log(`${status} ${file}: ${numErrors} errors, ${numWarnings} warnings, ${numInfos} infos, ${numHints} hints`);
  for (const m of report.issues.messages) {
    const severity = ["error", "warning", "info", "hint"][m.severity];
    console.log(`        ${severity} ${m.code} ${m.pointer ?? ""}: ${m.message}`);
  }
  if (numErrors > 0) failed++;
}

console.log(`${files.length} files, ${failed} with errors`);
process.exit(failed > 0 ? 1 : 0);
