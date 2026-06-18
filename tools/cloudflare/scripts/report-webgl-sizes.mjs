/**
 * Report WebGL build file sizes (CI diagnostics for Pages 25 MiB limit).
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PAGES_MAX_BYTES = 25 * 1024 * 1024;
const root = path.dirname(fileURLToPath(import.meta.url));
const defaultSource = path.resolve(root, "..", "..", "..", "build", "WebGL");

function resolveBuildDir(sourceRoot) {
  const candidates = [path.join(sourceRoot, "WebGL"), sourceRoot];
  for (const dir of candidates) {
    if (fs.existsSync(path.join(dir, "index.html"))) return dir;
  }
  return null;
}

function listFiles(dir) {
  const files = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) files.push(...listFiles(full));
    else files.push(full);
  }
  return files;
}

const sourceRoot = process.env.UNITY_WEBGL_SOURCE
  ? path.resolve(process.env.UNITY_WEBGL_SOURCE)
  : defaultSource;
const buildDir = resolveBuildDir(sourceRoot);

if (!buildDir) {
  console.error(`::error::WebGL build dir not found under ${sourceRoot}`);
  process.exit(1);
}

const sizes = listFiles(buildDir)
  .map((file) => {
    const { size } = fs.statSync(file);
    return {
      path: path.relative(buildDir, file).replace(/\\/g, "/"),
      bytes: size,
      mib: Number((size / (1024 * 1024)).toFixed(2)),
    };
  })
  .sort((a, b) => b.bytes - a.bytes);

const oversized = sizes.filter((f) => f.bytes > PAGES_MAX_BYTES);
const reportPath = process.env.WEBGL_SIZE_REPORT || "webgl-size-report.txt";

const lines = [
  `WebGL build: ${buildDir}`,
  `Files: ${sizes.length}`,
  "",
  "Largest files:",
  ...sizes.slice(0, 15).map((f) => `  ${f.mib} MiB  ${f.path}`),
];

if (oversized.length) {
  lines.push("", "OVER PAGES 25 MiB LIMIT:");
  for (const f of oversized) {
    lines.push(`  ${f.mib} MiB  ${f.path}`);
  }
}

const report = lines.join("\n");
console.log(report);
fs.writeFileSync(reportPath, `${report}\n`);

if (oversized.length) {
  console.error("::error::WebGL build contains files over Cloudflare Pages 25 MiB limit");
  process.exit(1);
}
