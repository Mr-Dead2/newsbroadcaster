// Shared page pieces for the CUI renderers (static screenshots and the animation player).
const svg = s => 'data:image/svg+xml;base64,' + Buffer.from(s).toString('base64');

const WIPE = svg(`<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1000" viewBox="0 0 1600 1000" preserveAspectRatio="none">
<defs>
 <linearGradient id="sky" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#1b2a4a"/><stop offset="0.45" stop-color="#c2553a"/><stop offset="0.7" stop-color="#f2a65a"/><stop offset="1" stop-color="#f6d28b"/></linearGradient>
 <radialGradient id="sun" cx="0.62" cy="0.62" r="0.18"><stop offset="0" stop-color="#fff3c4"/><stop offset="0.5" stop-color="#ffd27a" stop-opacity="0.9"/><stop offset="1" stop-color="#ffb35a" stop-opacity="0"/></radialGradient>
 <linearGradient id="sea" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#3d4f6b"/><stop offset="1" stop-color="#141c2b"/></linearGradient>
</defs>
<rect width="1600" height="1000" fill="url(#sky)"/>
<rect width="1600" height="1000" fill="url(#sun)"/>
<path d="M0 640 L180 520 L300 590 L470 430 L620 560 L760 480 L900 600 L1600 600 L1600 1000 L0 1000 Z" fill="#2a2433" opacity="0.85"/>
<path d="M0 700 L240 610 L420 680 L640 560 L860 690 L1100 600 L1350 680 L1600 620 L1600 1000 L0 1000 Z" fill="#1d1824"/>
<rect y="760" width="1600" height="240" fill="url(#sea)"/>
<g fill="#0e0c12">
 <rect x="1180" y="380" width="26" height="330"/><rect x="1150" y="360" width="86" height="26"/>
 <path d="M1210 430 L1330 430 L1330 445 L1210 445 Z"/><rect x="1300" y="445" width="8" height="70"/>
 <path d="M80 760 L120 700 L140 700 L180 760 Z"/><rect x="250" y="720" width="120" height="40"/><rect x="270" y="690" width="40" height="30"/>
</g>
<g fill="#0e0c12" opacity="0.9"><circle cx="1400" cy="300" r="3"/><path d="M1380 300 l20 -6 l20 6 l-20 2 z"/><path d="M1460 250 l14 -4 l14 4 l-14 1 z"/></g>
</svg>`);

const PATCH = svg(`<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1000" viewBox="0 0 1600 1000" preserveAspectRatio="none">
<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="#0f2a3d"/><stop offset="1" stop-color="#1e6f8f"/></linearGradient></defs>
<rect width="1600" height="1000" fill="url(#g)"/>
<g stroke="#7fd6ff" stroke-opacity="0.18" stroke-width="2">${Array.from({ length: 20 }, (_, i) => `<line x1="${i * 90}" y1="0" x2="${i * 90 - 400}" y2="1000"/>`).join('')}</g>
<g fill="none" stroke="#bdeeff" stroke-width="26" stroke-linecap="round" opacity="0.9">
 <circle cx="800" cy="480" r="170"/><path d="M800 250 v60 M800 650 v60 M570 480 h60 M970 480 h60 M640 320 l42 42 M918 598 l42 42 M640 640 l42 -42 M918 362 l42 -42"/>
</g>
<circle cx="800" cy="480" r="70" fill="#bdeeff" opacity="0.9"/>
</svg>`);

const IMAGES = { 'https://img.example/wipe-day.png': WIPE, 'https://img.example/patch-notes.png': PATCH };

const BACKDROP = `
  radial-gradient(ellipse at 70% 35%, rgba(255,190,120,0.28), rgba(0,0,0,0) 55%),
  linear-gradient(180deg, #5d6f7f 0%, #8b9aa3 38%, #6a6a55 52%, #474a35 66%, #2c2e22 100%)`;

const css = `
@font-face { font-family: 'Roboto Condensed'; font-weight: 700; src: local('Roboto Condensed Bold'), local('RobotoCondensed-Bold'), url('file:///usr/share/fonts/truetype/roboto/unhinted/RobotoCondensed-Bold.ttf'); }
@font-face { font-family: 'Roboto Condensed'; font-weight: 400; src: local('Roboto Condensed'), local('RobotoCondensed-Regular'), url('file:///usr/share/fonts/truetype/roboto/unhinted/RobotoCondensed-Regular.ttf'); }
html, body { margin: 0; padding: 0; background: #000; }
#screen { position: relative; width: 1920px; height: 1080px; overflow: hidden; background: ${BACKDROP}; }
#scene { position: absolute; inset: 0; }
.layer { position: absolute; left: 0; top: 0; width: 1920px; height: 1080px; }
.el { position: absolute; box-sizing: border-box; }
.skin { position: absolute; inset: 0; }
.raw { position: absolute; left: 0; top: 0; width: 100%; height: 100%; object-fit: fill; }
.txt { position: absolute; inset: 0; display: flex; flex-direction: column; }
.inner { white-space: pre-wrap; overflow-wrap: break-word; line-height: 1.17; width: 100%; }
#tag { position: absolute; right: 14px; bottom: 10px; font: 600 15px 'Roboto Condensed', sans-serif; color: rgba(255,255,255,0.55); z-index: 99; letter-spacing: 0.5px; }
`;

const SCENE = `
<svg id="scene" viewBox="0 0 1920 1080" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
 <path d="M0 560 L260 470 L420 520 L640 430 L860 500 L1080 440 L1300 520 L1560 450 L1920 520 L1920 1080 L0 1080 Z" fill="#59604a" opacity="0.55"/>
 <path d="M0 640 L300 600 L620 650 L900 610 L1240 660 L1600 620 L1920 650 L1920 1080 L0 1080 Z" fill="#3b3e2c"/>
 <path d="M0 760 L1920 720 L1920 1080 L0 1080 Z" fill="#2b2c20"/>
 <g fill="#1c1d15" opacity="0.9"><rect x="1500" y="520" width="16" height="150"/><path d="M1460 540 l48 -70 l48 70 z"/><path d="M1470 590 l38 -60 l38 60 z"/>
 <rect x="240" y="560" width="12" height="120"/><path d="M206 580 l40 -64 l40 64 z"/><path d="M214 624 l32 -50 l32 50 z"/></g>
</svg>`;


module.exports = { IMAGES, css, SCENE };
