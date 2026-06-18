import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const cloudflareRoot = path.resolve(root, "..");
const webAppRoot = path.resolve(cloudflareRoot, "..", "web-app");
const publicRoot = path.resolve(cloudflareRoot, "public");
const unitySourceRoot = process.env.UNITY_WEBGL_SOURCE
  ? path.resolve(process.env.UNITY_WEBGL_SOURCE)
  : path.resolve(cloudflareRoot, "..", "..", "build", "WebGL");

/** game-ci default output is build/WebGL/WebGL (see legacy main.yml). */
function resolveUnityBuildDir(rootDir) {
  const candidates = [
    path.join(rootDir, "WebGL"),
    rootDir,
    path.join(rootDir, "TPDArena"),
  ];
  for (const dir of candidates) {
    if (fs.existsSync(path.join(dir, "index.html"))) {
      return dir;
    }
  }
  return candidates[0];
}

const unitySource = resolveUnityBuildDir(unitySourceRoot);

function resolveBuildId() {
  const fromEnv = process.env.APP_BUILD_ID?.trim();
  if (fromEnv) return fromEnv.replace(/[^a-zA-Z0-9._-]/g, "").slice(0, 40);
  return String(Date.now());
}

const buildId = resolveBuildId();

function injectBuildId(html) {
  return html.replaceAll("__BUILD_ID__", buildId);
}

function patchGameIndexHtml(filePath) {
  if (!fs.existsSync(filePath)) return;
  let html = fs.readFileSync(filePath, "utf8");
  if (!html.includes("no-cache, no-store")) {
    const cacheMeta =
      '<meta http-equiv="Cache-Control" content="no-cache, no-store, must-revalidate" />';
    if (html.includes("<head>")) {
      html = html.replace("<head>", `<head>\n    ${cacheMeta}`);
    } else {
      html = `${cacheMeta}\n${html}`;
    }
  }
  // Unity uses fixed Build/* filenames — bust cache after each deploy
  html = html.replace(/(Build\/[^"'\s?]+)(?!\?v=)/g, `$1?v=${buildId}`);
  fs.writeFileSync(filePath, html);
}

function copyFile(src, dest) {
  fs.mkdirSync(path.dirname(dest), { recursive: true });
  fs.copyFileSync(src, dest);
}

function copyDir(src, dest) {
  if (!fs.existsSync(src)) return false;
  fs.mkdirSync(dest, { recursive: true });
  for (const entry of fs.readdirSync(src, { withFileTypes: true })) {
    const from = path.join(src, entry.name);
    const to = path.join(dest, entry.name);
    if (entry.isDirectory()) copyDir(from, to);
    else copyFile(from, to);
  }
  return true;
}

function emptyDir(dir) {
  if (!fs.existsSync(dir)) {
    fs.mkdirSync(dir, { recursive: true });
    return;
  }
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const target = path.join(dir, entry.name);
    if (entry.isDirectory()) fs.rmSync(target, { recursive: true, force: true });
    else fs.unlinkSync(target);
  }
}

fs.mkdirSync(publicRoot, { recursive: true });

// Telegram shell → Mini App entry (lobby, then Unity WebGL)
const shellSrc = fs.readFileSync(path.join(webAppRoot, "shell-index.html"), "utf8");
fs.writeFileSync(path.join(publicRoot, "index.html"), injectBuildId(shellSrc));
copyFile(path.join(webAppRoot, "arena-lobby.js"), path.join(publicRoot, "arena-lobby.js"));
fs.writeFileSync(
  path.join(publicRoot, "version.json"),
  JSON.stringify({ buildId, builtAt: new Date().toISOString() }, null, 2),
);

// Legacy JS fallback (optional dev / until Unity handles all flows)
const legacyFiles = ["styles.css", "app.js", "battle-sim.js", "battle-renderer.js"];
const legacyDir = path.join(publicRoot, "_legacy");
emptyDir(legacyDir);
for (const file of legacyFiles) {
  const src = path.join(webAppRoot, file);
  if (fs.existsSync(src)) {
    copyFile(src, path.join(legacyDir, file));
  }
}

const headersSrc = path.join(webAppRoot, "_headers");
if (fs.existsSync(headersSrc)) {
  copyFile(headersSrc, path.join(publicRoot, "_headers"));
}

// Unity WebGL build
const gameDir = path.join(publicRoot, "game");
const requireGame = process.env.REQUIRE_GAME === "1";
if (copyDir(unitySource, gameDir)) {
  console.log(`Copied Unity WebGL from ${unitySource} → ${gameDir}`);
  patchGameIndexHtml(path.join(gameDir, "index.html"));
} else if (requireGame) {
  console.error(`WebGL build required but not found (checked under ${unitySourceRoot})`);
  console.error("Run workflow_dispatch with Unity build, or restore WebGL cache/artifact.");
  process.exit(1);
} else {
  console.warn(`Unity WebGL build not found (checked under ${unitySourceRoot})`);
  console.warn("Deploy will serve shell only until CI produces build/WebGL.");
  fs.mkdirSync(gameDir, { recursive: true });
  fs.writeFileSync(
    path.join(gameDir, "index.html"),
    `<!DOCTYPE html><html lang="ru"><body style="font-family:sans-serif;background:#0d111c;color:#e8ecf8;padding:24px">
<h1>TPD Arena</h1>
<p>Unity WebGL билд ещё не собран. Запустите GitHub Actions workflow или локально:</p>
<pre>Unity → File → Build Settings → WebGL → Build → build/WebGL</pre>
</body></html>`,
  );
}

console.log(`Prepared Cloudflare public → ${publicRoot} (buildId=${buildId})`);
