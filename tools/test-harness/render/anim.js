// Usage: node anim.js <timeline.json> <out.mp4> [out.gif]
// Plays a CUI timeline (snapshots of one player's client UI at given times) frame by frame,
// applying each element's fadeIn / fadeOut exactly like the Rust client, with a cursor that
// moves to and clicks the button that triggered the next snapshot. Frames are encoded with ffmpeg.
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');
const { IMAGES, css, SCENE } = require('./page');

const [timelinePath, outMp4, outGif] = process.argv.slice(2);
const FPS = 30;
const timeline = JSON.parse(fs.readFileSync(timelinePath, 'utf8'));

const player = `
(function () {
  const T = window.__TIMELINE;
  const frames = T.frames;
  // When did each element appear, and which elements fade out at which frame?
  const appearedAt = {}; const fadingOut = {}; const ghosts = []; // ghosts: {start, duration, elements}
  let prevIds = new Map();
  frames.forEach((f, k) => {
    const ids = new Map();
    f.elements.forEach(e => { ids.set(e.__id, e); if (!prevIds.has(e.__id)) appearedAt[e.__id] = f.t; });
    const gone = [];
    prevIds.forEach((e, id) => { if (!ids.has(id) && (e.fadeOut || 0) > 0) { gone.push(e); fadingOut[id] = { start: f.t, duration: e.fadeOut }; } });
    if (gone.length) ghosts.push({ start: f.t, duration: Math.max(...gone.map(e => e.fadeOut)), elements: gone });
    prevIds = ids;
  });

  const screen = document.getElementById('screen');
  const cursor = document.getElementById('cursor');
  const ripple = document.getElementById('ripple');
  const home = { x: 1560, y: 930 };
  const targets = {}; // frame index -> {x,y}

  function frameAt(t) { let k = 0; for (let i = 0; i < frames.length; i++) if (frames[i].t <= t) k = i; return k; }
  function ease(x) { return x < 0.5 ? 2 * x * x : 1 - Math.pow(-2 * x + 2, 2) / 2; }

  function draw(t) {
    screen.querySelectorAll('.layer').forEach(l => l.remove());
    const k = frameAt(t);
    const live = ghosts.filter(g => t >= g.start && t < g.start + g.duration).flatMap(g => g.elements);
    renderCui(live.concat(frames[k].elements), screen, { t, appearedAt, fadingOut });
    return k;
  }

  function targetPos(command) {
    const el = [...screen.querySelectorAll('[data-command]')].find(d => d.dataset.command === command);
    if (!el) return null;
    const r = el.getBoundingClientRect(), s = screen.getBoundingClientRect();
    return { x: r.left - s.left + r.width / 2, y: r.top - s.top + r.height / 2 };
  }

  window.seek = function (t) {
    const k = draw(t);
    // Cursor: rests at the last click, glides to the next button, then clicks it.
    let pos = home;
    for (let i = 1; i <= k; i++) if (targets[i]) pos = targets[i];
    const next = frames[k + 1];
    let x = pos.x, y = pos.y, clickAmt = 0;
    if (next && next.click) {
      if (!targets[k + 1]) targets[k + 1] = targetPos(next.click) || pos;
      const to = targets[k + 1];
      const a = (t - (next.t - 0.95)) / 0.65;
      if (a > 0) { const e = ease(Math.min(1, a)); x = pos.x + (to.x - pos.x) * e; y = pos.y + (to.y - pos.y) * e; }
      const c = (t - (next.t - 0.25)) / 0.25;
      if (c > 0) clickAmt = Math.min(1, c);
    }
    cursor.style.transform = 'translate(' + x + 'px,' + y + 'px) scale(' + (1 - 0.15 * Math.sin(Math.PI * clickAmt)) + ')';
    ripple.style.transform = 'translate(' + (x - 22) + 'px,' + (y - 22) + 'px) scale(' + (0.4 + clickAmt) + ')';
    ripple.style.opacity = clickAmt > 0 ? String(0.55 * (1 - clickAmt)) : '0';
  };
})();`;

(async () => {
  const work = fs.mkdtempSync(path.join(os.tmpdir(), 'nb-anim-'));
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 } });
  const cuiJs = fs.readFileSync(path.join(__dirname, 'cui.js'), 'utf8');
  const extraCss = `
    #cursor { position:absolute; left:0; top:0; width:26px; height:26px; z-index:100; pointer-events:none; transform-origin: 0 0; }
    #ripple { position:absolute; left:0; top:0; width:44px; height:44px; border-radius:50%; border:3px solid rgba(255,255,255,0.9); z-index:99; opacity:0; }`;
  const cursorSvg = `<svg id="cursor" viewBox="0 0 26 26"><path d="M3 2 L3 21 L8.5 16 L12.5 24 L16 22.5 L12 14.5 L19.5 14.5 Z" fill="#fff" stroke="#111" stroke-width="1.6" stroke-linejoin="round"/></svg>`;
  await page.setContent(`<!doctype html><html><head><meta charset="utf-8"><style>${css}${extraCss}</style></head>
    <body><div id="screen">${SCENE}<div id="ripple"></div>${cursorSvg}</div>
    <script>window.__S = 1080/720; window.__IMAGES = ${JSON.stringify(IMAGES)}; window.__TIMELINE = ${JSON.stringify(timeline)};</script>
    <script>${cuiJs}</script><script>${player}</script></body></html>`, { waitUntil: 'load' });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(300);

  const total = Math.round(timeline.duration * FPS);
  for (let f = 0; f < total; f++) {
    await page.evaluate(t => window.seek(t), f / FPS);
    await page.locator('#screen').screenshot({ path: path.join(work, `f${String(f).padStart(5, '0')}.jpg`), type: 'jpeg', quality: 92 });
    if (f % 60 === 0) process.stdout.write(`frame ${f}/${total}\n`);
  }
  await browser.close();

  execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-framerate', String(FPS), '-i', path.join(work, 'f%05d.jpg'),
    '-vf', 'scale=1600:900:flags=lanczos', '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', outMp4]);
  console.log('wrote', outMp4);
  if (outGif) {
    execFileSync('ffmpeg', ['-y', '-loglevel', 'error', '-framerate', String(FPS), '-i', path.join(work, 'f%05d.jpg'),
      '-vf', 'fps=15,scale=960:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=160[p];[b][p]paletteuse=dither=bayer:bayer_scale=4', outGif]);
    console.log('wrote', outGif);
  }
  fs.rmSync(work, { recursive: true, force: true });
})();
