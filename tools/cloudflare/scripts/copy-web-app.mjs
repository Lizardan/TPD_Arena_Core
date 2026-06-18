import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const cloudflareRoot = path.resolve(root, "..");
const webAppRoot = path.resolve(cloudflareRoot, "..", "web-app");
const publicRoot = path.resolve(cloudflareRoot, "public");

const staticFiles = [
  "index.html",
  "styles.css",
  "app.js",
  "battle-sim.js",
  "battle-renderer.js",
];

fs.mkdirSync(publicRoot, { recursive: true });

for (const file of staticFiles) {
  fs.copyFileSync(path.join(webAppRoot, file), path.join(publicRoot, file));
}

console.log(`Copied web-app → ${publicRoot}`);
