// node compare.js out.png title before.png after.png [caption]
const { chromium } = require('playwright');
const fs = require('fs');
const [out, title, before, after, caption] = process.argv.slice(2);
const b64 = f => 'data:image/png;base64,' + fs.readFileSync(f).toString('base64');
(async () => {
  const b = await chromium.launch(); const p = await b.newPage({ viewport: { width: 1920, height: 640 } });
  await p.setContent(`<style>
    @font-face{font-family:RC;font-weight:700;src:url('file:///usr/share/fonts/truetype/roboto/unhinted/RobotoCondensed-Bold.ttf')}
    @font-face{font-family:RC;font-weight:400;src:url('file:///usr/share/fonts/truetype/roboto/unhinted/RobotoCondensed-Regular.ttf')}
    body{margin:0;background:#101317;font-family:RC,sans-serif;color:#e8eaed;width:1920px}
    h1{margin:0;padding:16px 24px 6px;font-size:28px;font-weight:700;letter-spacing:.5px}
    .cap{padding:0 24px 12px;font-size:17px;color:#9aa3ad;font-weight:400}
    .row{display:grid;grid-template-columns:952px 952px;gap:16px;padding:0 0 0 0}
    .cell{position:relative} img{width:952px;height:535.5px;display:block}
    .tag{position:absolute;left:12px;top:10px;padding:5px 12px;font-size:16px;font-weight:700;border-radius:3px}
    .b{background:#3a3f47;color:#cfd3d8}.a{background:#5bb5ff;color:#06121f}
  </style><h1>${title}</h1>${caption ? `<div class="cap">${caption}</div>` : ''}
  <div class="row"><div class="cell"><img src="${b64(before)}"><span class="tag b">BEFORE · 1.2.0</span></div>
  <div class="cell"><img src="${b64(after)}"><span class="tag a">AFTER · 2.0.0</span></div></div>`);
  await p.evaluate(() => document.fonts.ready);
  const h = await p.evaluate(() => document.body.scrollHeight);
  await p.setViewportSize({ width: 1920, height: h });
  await p.screenshot({ path: out }); await b.close();
})();
