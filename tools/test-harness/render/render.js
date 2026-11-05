// Usage: node render.js <inDir> <outDir> [scale=1] [label]
// Renders every *.json CUI snapshot in inDir to a 1920x1080 PNG over a neutral in-game style backdrop.
const fs = require('fs');
const path = require('path');
const { chromium } = require('playwright');

const [inDir, outDir, scaleArg, label] = process.argv.slice(2);
const DSF = Number(scaleArg || 1);
fs.mkdirSync(outDir, { recursive: true });

const { IMAGES, css, SCENE } = require('./page');

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: DSF });
  const cuiJs = fs.readFileSync(path.join(__dirname, 'cui.js'), 'utf8');
  const files = fs.readdirSync(inDir).filter(f => f.endsWith('.json') && !f.startsWith('_')).sort();
  const storePath = path.join(inDir, '_files.json');
  const STORE = fs.existsSync(storePath) ? JSON.parse(fs.readFileSync(storePath, 'utf8')) : {};
  const FILES = Object.fromEntries(Object.entries(STORE).map(([k, b64]) => [k, 'data:image/png;base64,' + b64]));
  for (const f of files) {
    const elements = JSON.parse(fs.readFileSync(path.join(inDir, f), 'utf8'));
    const html = `<!doctype html><html><head><meta charset="utf-8"><style>${css}</style></head>
      <body><div id="screen">${SCENE}${label ? `<div id="tag">${label}</div>` : ''}</div>
      <script>window.__S = 1080/720; window.__IMAGES = ${JSON.stringify(IMAGES)}; window.__FILES = ${JSON.stringify(FILES)};</script>
      <script>${cuiJs}</script>
      <script>renderCui(${JSON.stringify(elements)}, document.getElementById('screen'));</script>
      </body></html>`;
    await page.setContent(html, { waitUntil: 'load' });
    await page.evaluate(() => document.fonts.ready);
    await page.waitForTimeout(150);
    const out = path.join(outDir, f.replace(/\.json$/, '.png'));
    await page.locator('#screen').screenshot({ path: out });
    console.log('rendered', out);
  }
  await browser.close();
})();
