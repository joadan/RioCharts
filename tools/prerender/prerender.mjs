// Prerenders the published docs site (Blazor WebAssembly) so search engines get each page's text,
// title and description in the HTML instead of an empty loading screen.
//
//   cd tools/prerender && npm install && npx playwright install chromium
//   node tools/prerender/prerender.mjs <published wwwroot> <Pages folder> [origin]
//
// It serves the published site locally, opens every @page route in headless Chromium, waits for
// Blazor to render it and writes the result as <route>.html (index.html for "/"), plus sitemap.xml
// and robots.txt. Blazor starts on top of the prerendered page as before and renders it again.
// The docs workflow (.github/workflows/docs.yml) runs it after dotnet publish.

import { chromium } from "playwright";
import http from "node:http";
import fs from "node:fs/promises";
import path from "node:path";

const [rootArg, pagesArg, originArg = "https://riocharts.com"] = process.argv.slice(2);
if (!rootArg || !pagesArg) {
    console.error("Usage: node prerender.mjs <published wwwroot> <Pages folder> [origin]");
    process.exit(2);
}
const root = path.resolve(rootArg);
const origin = originArg.replace(/\/$/, "");
const parallel = 4;

// Routes from the @page directives: the first one per file is the page's own address (canonical),
// any further ones are aliases that point to it.
const routes = [];
for (const file of (await fs.readdir(pagesArg)).filter(f => f.endsWith(".razor")).sort()) {
    const text = await fs.readFile(path.join(pagesArg, file), "utf8");
    const pages = [...text.matchAll(/@page\s+"(\/[^"]*)"/g)].map(m => m[1]).filter(r => r !== "/not-found");
    pages.forEach(r => routes.push({ route: r, canonical: pages[0] }));
}
if (routes.length === 0) throw new Error(`No @page routes found in ${pagesArg}`);

const shell = await fs.readFile(path.join(root, "index.html"), "utf8");

// A small static server: files as they are, any path without a file the app shell (like GitHub Pages
// with its per-route copies). Always the original shell, never a page written by this script.
const types = {
    ".html": "text/html; charset=utf-8", ".js": "text/javascript", ".mjs": "text/javascript",
    ".wasm": "application/wasm", ".json": "application/json", ".css": "text/css", ".svg": "image/svg+xml",
    ".png": "image/png", ".txt": "text/plain; charset=utf-8", ".dat": "application/octet-stream",
    ".woff": "font/woff", ".woff2": "font/woff2", ".ico": "image/x-icon",
};
const server = http.createServer(async (req, res) => {
    const pathname = decodeURIComponent(new URL(req.url, "http://localhost").pathname);
    const file = path.join(root, pathname);
    if (file.startsWith(root) && pathname !== "/" && pathname !== "/index.html") {
        try {
            const body = await fs.readFile(file);
            res.writeHead(200, { "Content-Type": types[path.extname(file)] ?? "application/octet-stream" });
            res.end(body);
            return;
        } catch { /* not a file: fall through */ }
    }
    if (path.extname(pathname) && pathname !== "/index.html") {
        res.writeHead(404);
        res.end();
        return;
    }
    res.writeHead(200, { "Content-Type": types[".html"] });
    res.end(shell);
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const local = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch();
const results = new Map();
const failures = [];
try {
    const queue = [...routes];
    await Promise.all(Array.from({ length: parallel }, async () => {
        const context = await browser.newContext({ viewport: { width: 1280, height: 900 }, colorScheme: "light" });
        const page = await context.newPage();
        const errors = [];
        page.on("pageerror", e => errors.push(e.message));
        for (let item; (item = queue.shift());) {
            errors.length = 0;
            try {
                await page.goto(local + item.route, { waitUntil: "load" });
                // The boot screen has no h1; a rendered page does.
                await page.waitForSelector("#app h1", { timeout: 30_000 });
                await page.waitForLoadState("networkidle");
                await page.waitForTimeout(500);
                const snapshot = await page.evaluate(() => ({
                    title: document.title,
                    lead: (document.querySelector("#app .lead, #app .hero-lead")?.textContent ?? "").replace(/\s+/g, " ").trim(),
                    app: document.getElementById("app").innerHTML,
                }));
                if (errors.length) throw new Error(errors.join("; "));
                results.set(item.route, { ...item, ...snapshot });
                console.log(`${item.route} → ${snapshot.title}`);
            } catch (e) {
                failures.push(`${item.route}: ${e.message}`);
                queue.length = 0;   // stop: one failure usually means all would fail (the app did not start)
            }
        }
        await context.close();
    }));
} finally {
    await browser.close();
    server.close();
}
if (failures.length) {
    console.error("Prerendering failed:\n  " + failures.join("\n  "));
    process.exit(1);
}

const escape = s => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;");

// Search results show about 155 characters: keep the whole sentences that fit, or cut at a word
// when the first sentence alone is too long.
function describe(lead) {
    if (!lead) return null;
    if (lead.length <= 160) return lead;
    const sentences = lead.slice(0, 161).match(/^.*[.!?](?=\s)/);
    if (sentences && sentences[0].length >= 50) return sentences[0];
    const cut = lead.slice(0, 157);
    return cut.slice(0, cut.lastIndexOf(" ")).replace(/[,;:]$/, "") + "…";
}

function replaceOnce(html, pattern, replacement, what) {
    if (!pattern.test(html)) throw new Error(`index.html has no ${what}`);
    return html.replace(pattern, replacement);
}

function build(page) {
    const url = origin + (page.canonical === "/" ? "/" : page.canonical);
    const title = escape(page.title);
    const description = describe(page.lead);
    let html = shell;
    html = replaceOnce(html, /<title>[\s\S]*?<\/title>/, `<title>${title}</title>\n    <link rel="canonical" href="${url}" />`, "<title>");
    html = replaceOnce(html, /(<meta property="og:title" content=")[^"]*/, `$1${title}`, "og:title");
    html = replaceOnce(html, /(<meta property="og:url" content=")[^"]*/, `$1${url}`, "og:url");
    if (description) {
        html = replaceOnce(html, /(<meta name="description" content=")[^"]*/, `$1${escape(description)}`, "description");
        html = replaceOnce(html, /(<meta property="og:description" content=")[^"]*/, `$1${escape(description)}`, "og:description");
    }
    // The page as rendered, in place of the boot screen. It was rendered in the light theme: switch
    // it to dark before the first paint when the visitor uses dark (the layout's own classes).
    const start = html.indexOf('<div id="app">');
    const end = html.indexOf('<div id="blazor-error-ui">');
    if (start < 0 || end < 0) throw new Error('index.html has no <div id="app"> or blazor-error-ui');
    const darkScript = `<script>if (window.rioDocs.isDark()) { const s = document.querySelector("#app .site"); s?.classList.replace("rio-theme-light", "rio-theme-dark"); s?.classList.add("dark"); }</script>`;
    return html.slice(0, start) + `<div id="app">${page.app}</div>\n    ${darkScript}\n\n    ` + html.slice(end);
}

for (const page of results.values()) {
    const name = page.route === "/" ? "index.html" : page.route.slice(1) + ".html";
    await fs.writeFile(path.join(root, name), build(page));
}

const canonical = [...new Set(routes.map(r => r.canonical))];
const sitemap = `<?xml version="1.0" encoding="UTF-8"?>
<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
${canonical.map(r => `  <url><loc>${origin}${r}</loc></url>`).join("\n")}
</urlset>
`;
await fs.writeFile(path.join(root, "sitemap.xml"), sitemap);
await fs.writeFile(path.join(root, "robots.txt"), `User-agent: *\nAllow: /\n\nSitemap: ${origin}/sitemap.xml\n`);
console.log(`Prerendered ${results.size} pages; sitemap.xml lists ${canonical.length}.`);
