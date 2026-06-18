/**
 * Prepare public/ for Cloudflare Pages deploy.
 * - Drops uncompressed Unity blobs when a .br sibling exists
 * - Fails fast with a clear message if any asset exceeds 25 MiB (Pages limit)
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const PAGES_MAX_BYTES = 25 * 1024 * 1024;
const root = path.dirname(fileURLToPath(import.meta.url));
const publicRoot = path.resolve(root, "..", "public");
const debugLogPath = process.env.CI_DEBUG_LOG;

const UNCOMPRESSED_SUFFIXES = [".data", ".wasm", ".framework.js", ".loader.js"];

function appendDebug(entry) {
  if (!debugLogPath) return;
  fs.mkdirSync(path.dirname(debugLogPath), { recursive: true });
  fs.appendFileSync(debugLogPath, `${JSON.stringify(entry)}\n`);
}

function listFiles(dir) {
  const files = [];
  if (!fs.existsSync(dir)) return files;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) files.push(...listFiles(full));
    else files.push(full);
  }
  return files;
}

function pruneUncompressedDuplicates(gameDir) {
  if (!fs.existsSync(gameDir)) return [];
  const removed = [];
  for (const file of listFiles(gameDir)) {
    if (file.endsWith(".br")) continue;
    const hasBr = fs.existsSync(`${file}.br`);
    const matchesSuffix = UNCOMPRESSED_SUFFIXES.some((suffix) => file.endsWith(suffix));
    if (hasBr && matchesSuffix) {
      fs.unlinkSync(file);
      removed.push(path.relative(publicRoot, file));
    }
  }
  return removed;
}

function collectSizes(dir) {
  return listFiles(dir)
    .map((file) => {
      const { size } = fs.statSync(file);
      return {
        path: path.relative(publicRoot, file).replace(/\\/g, "/"),
        bytes: size,
        mib: Number((size / (1024 * 1024)).toFixed(2)),
      };
    })
    .sort((a, b) => b.bytes - a.bytes);
}

function findOversized(files) {
  return files.filter((f) => f.bytes > PAGES_MAX_BYTES);
}

const gameDir = path.join(publicRoot, "game");
const removed = pruneUncompressedDuplicates(gameDir);
const sizes = collectSizes(publicRoot);
const oversized = findOversized(sizes);

appendDebug({
  sessionId: "a89d64",
  timestamp: Date.now(),
  location: "prepare-pages-public.mjs",
  message: "pages public prepared",
  hypothesisId: "H1",
  data: {
    removedDuplicates: removed,
    fileCount: sizes.length,
    largest: sizes.slice(0, 8),
    oversized,
  },
});

console.log(`Pages public: ${sizes.length} files under ${publicRoot}`);
for (const file of sizes.slice(0, 10)) {
  console.log(`  ${file.mib} MiB  ${file.path}`);
}

if (removed.length) {
  console.log(`Pruned ${removed.length} uncompressed duplicate(s): ${removed.join(", ")}`);
}

if (oversized.length) {
  console.error("\n::error::Cloudflare Pages rejects files larger than 25 MiB per asset.");
  for (const file of oversized) {
    console.error(`  ${file.mib} MiB  ${file.path}`);
  }
  console.error(
    "\nFix: enable WebGL Brotli in Project Settings, or host game/ on R2. See tools/UNITY_WEBGL.md",
  );
  process.exit(1);
}
