// Browser-side CUI renderer: draws a Rust CUI element list (as the client would hold it) into #screen.
// Reference canvas is 1280x720 units (offsets + font sizes), scaled to the screen height.
//
// renderCui(elements, screen, anim?) — anim (optional, for the animation player):
//   { t, appearedAt: {id: seconds}, fadingOut: {id: {start, duration}}, onButton: fn(div, command) }
// Like the Unity client, fadeIn/fadeOut affect each element's own graphic only (not its children).
(function () {
  const IMAGES = window.__IMAGES || {};

  function rgba(str, fallback) {
    if (!str) str = fallback || '1 1 1 1';
    const p = String(str).trim().split(/\s+/).map(Number);
    const [r, g, b, a] = [p[0] ?? 1, p[1] ?? 1, p[2] ?? 1, p[3] ?? 1];
    return `rgba(${Math.round(r * 255)},${Math.round(g * 255)},${Math.round(b * 255)},${a})`;
  }
  function vec(str, def) {
    if (!str) return def;
    const p = String(str).trim().split(/\s+/).map(Number);
    return [p[0] || 0, p[1] || 0];
  }
  function esc(s) { return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;'); }
  function richText(s) {
    if (s == null) return '';
    let out = '';
    const re = /<(\/?)(color|b|i|size)(=([^>]+))?>/gi; let m; let last = 0;
    while ((m = re.exec(s)) !== null) {
      out += esc(s.slice(last, m.index)); last = re.lastIndex;
      const closing = m[1] === '/'; const tag = m[2].toLowerCase(); const val = m[4];
      if (closing) { out += '</span>'; continue; }
      if (tag === 'color') {
        let c = val.replace(/"/g, '');
        if (/^#[0-9a-f]{8}$/i.test(c)) { const n = c.slice(1); c = `rgba(${parseInt(n.slice(0,2),16)},${parseInt(n.slice(2,4),16)},${parseInt(n.slice(4,6),16)},${parseInt(n.slice(6,8),16)/255})`; }
        out += `<span style="color:${c}">`;
      } else if (tag === 'b') out += '<span style="font-weight:700">';
      else if (tag === 'i') out += '<span style="font-style:italic">';
      else if (tag === 'size') out += `<span style="font-size:${parseFloat(val) * window.__S}px">`;
    }
    out += esc(s.slice(last));
    return out;
  }
  const ALIGN = {
    UpperLeft: ['flex-start', 'flex-start', 'left'], UpperCenter: ['center', 'flex-start', 'center'], UpperRight: ['flex-end', 'flex-start', 'right'],
    MiddleLeft: ['flex-start', 'center', 'left'], MiddleCenter: ['center', 'center', 'center'], MiddleRight: ['flex-end', 'center', 'right'],
    LowerLeft: ['flex-start', 'flex-end', 'left'], LowerCenter: ['center', 'flex-end', 'center'], LowerRight: ['flex-end', 'flex-end', 'right'],
  };
  function fontFor(f) {
    f = (f || 'robotocondensed-bold.ttf').toLowerCase();
    if (f.includes('mono')) return ['DejaVu Sans Mono, monospace', 400];
    if (f.includes('marker')) return ['"Permanent Marker", cursive', 400];
    if (f.includes('regular')) return ['"Roboto Condensed", sans-serif', 400];
    return ['"Roboto Condensed", sans-serif', 700];
  }

  window.renderCui = function (elements, screen, anim) {
    const S = window.__S;
    const W = screen.clientWidth, Hh = screen.clientHeight;
    const nodes = {};
    const layers = ['Under', 'Hud', 'Hud.Menu', 'Overlay', 'Overall'];
    layers.forEach((n, i) => {
      const d = document.createElement('div');
      d.className = 'layer'; d.style.zIndex = String(10 + i);
      screen.appendChild(d); nodes[n] = { div: d, w: W, h: Hh, content: d, cw: W, ch: Hh };
    });

    // Opacity of an element's own graphic at the current animation time.
    function opacityFor(el, fadeIn) {
      if (!anim || el.__id == null) return 1;
      let o = 1;
      const t0 = anim.appearedAt[el.__id];
      if (fadeIn > 0 && t0 != null) o = Math.max(0, Math.min(1, (anim.t - t0) / fadeIn));
      const fo = anim.fadingOut && anim.fadingOut[el.__id];
      if (fo) o *= Math.max(0, 1 - (anim.t - fo.start) / fo.duration);
      return o;
    }

    for (const el of elements) {
      const parent = nodes[el.parent];
      if (!parent) continue;
      const comps = el.components || [];
      const get = t => comps.find(c => c.type === t);
      const rt = get('RectTransform') || {};
      const amin = vec(rt.anchormin, [0, 0]), amax = vec(rt.anchormax, [1, 1]);
      const omin = vec(rt.offsetmin, [0, 0]), omax = vec(rt.offsetmax, [0, 0]);
      const pw = parent.cw, ph = parent.ch;
      const x0 = amin[0] * pw + omin[0] * S, x1 = amax[0] * pw + omax[0] * S;
      const y0 = amin[1] * ph + omin[1] * S, y1 = amax[1] * ph + omax[1] * S;
      const w = Math.max(0, x1 - x0), h = Math.max(0, y1 - y0);
      const d = document.createElement('div');
      d.className = 'el';
      d.dataset.name = el.name || '';
      d.style.left = x0 + 'px'; d.style.top = (ph - y1) + 'px';
      d.style.width = w + 'px'; d.style.height = h + 'px';

      const img = get('UnityEngine.UI.Image');
      const btn = get('UnityEngine.UI.Button');
      const raw = get('UnityEngine.UI.RawImage');
      const txt = get('UnityEngine.UI.Text');
      const inp = get('UnityEngine.UI.InputField');
      const outline = get('UnityEngine.UI.Outline');
      const scroll = get('UnityEngine.UI.ScrollView');
      const countdown = get('Countdown');

      // The element's own graphic lives in a "skin" so its fade does not affect children.
      const graphic = img || btn;
      if (graphic || raw) {
        const skin = document.createElement('div');
        skin.className = 'skin';
        if (graphic) {
          skin.style.background = rgba(graphic.color);
          if ((graphic.material || '').toLowerCase().includes('blur')) skin.style.backdropFilter = 'blur(10px)';
          skin.style.opacity = String(opacityFor(el, graphic.fadeIn || 0));
        }
        if (raw) {
          const generated = (window.__FILES || {})[raw.png];
          const src = IMAGES[raw.url] || IMAGES[raw.png] || null;
          if (generated) {
            // White texture tinted by the RawImage colour (what Unity does), drawn as a CSS mask.
            skin.style.background = rgba(raw.color);
            skin.style.webkitMaskImage = skin.style.maskImage = `url(${generated})`;
            skin.style.webkitMaskSize = skin.style.maskSize = '100% 100%';
          } else if (src) { const im = document.createElement('img'); im.src = src; im.className = 'raw'; skin.appendChild(im); }
          else if (raw.color) skin.style.background = rgba(raw.color);
          skin.style.opacity = String(opacityFor(el, raw.fadeIn || 0));
        }
        d.appendChild(skin);
      }
      if (btn) {
        d.classList.add('btn');
        if (btn.command) d.dataset.command = btn.command;
      }

      if (txt || inp) {
        const t = txt || inp;
        const [ff, fw] = fontFor(t.font);
        const al = ALIGN[t.align || 'UpperLeft'] || ALIGN.UpperLeft;
        const box = document.createElement('div');
        box.className = 'txt';
        box.style.justifyContent = al[1]; box.style.alignItems = al[0];
        const inner = document.createElement('div');
        inner.className = 'inner';
        inner.style.textAlign = al[2];
        inner.style.fontFamily = ff; inner.style.fontWeight = String(fw);
        inner.style.fontSize = ((t.fontSize || 14) * S) + 'px';
        inner.style.color = rgba(t.color);
        if (inp && (inp.lineType || 'SingleLine') === 'SingleLine') { inner.style.whiteSpace = 'pre'; }
        let text = txt ? (t.text ?? 'Text') : (t.text || '');
        if (countdown && text.includes('%TIME_LEFT%')) {
          const start = countdown.startTime || 0, end = countdown.endTime || 0, step = countdown.step || 1;
          let left = start;
          if (anim && el.__id != null && anim.appearedAt[el.__id] != null) left = Math.max(end, start - Math.floor(anim.t - anim.appearedAt[el.__id]) * step);
          text = text.split('%TIME_LEFT%').join(String(left));
        }
        inner.innerHTML = richText(text);
        if (outline) {
          const [dx, dy] = vec(outline.distance, [1, -1]).map(v => v * S * 0.8);
          const oc = rgba(outline.color, '0 0 0 0.5');
          inner.style.textShadow = `${dx}px ${-dy}px 0 ${oc}, ${-dx}px ${-dy}px 0 ${oc}, ${dx}px ${dy}px 0 ${oc}, ${-dx}px ${dy}px 0 ${oc}`;
        }
        box.style.opacity = String(opacityFor(el, txt ? (txt.fadeIn || 0) : 0));
        box.appendChild(inner); d.appendChild(box);
        if ((t.verticalOverflow || 'Truncate') === 'Truncate') box.style.overflow = 'hidden';
      }

      parent.content.appendChild(d);
      const node = { div: d, w, h, content: d, cw: w, ch: h };

      if (scroll) {
        d.style.overflow = 'hidden';
        const ct = scroll.contentTransform || {};
        const cmin = vec(ct.anchormin, [0, 0]), cmax = vec(ct.anchormax, [1, 1]);
        const comin = vec(ct.offsetmin, [0, 0]), comax = vec(ct.offsetmax, [0, 0]);
        const cx0 = cmin[0] * w + comin[0] * S, cx1 = cmax[0] * w + comax[0] * S;
        const cy0 = cmin[1] * h + comin[1] * S, cy1 = cmax[1] * h + comax[1] * S;
        const c = document.createElement('div');
        c.className = 'el';
        c.style.left = cx0 + 'px'; c.style.top = (h - cy1) + 'px';
        c.style.width = (cx1 - cx0) + 'px'; c.style.height = (cy1 - cy0) + 'px';
        d.appendChild(c);
        node.content = c; node.cw = cx1 - cx0; node.ch = cy1 - cy0;
        const vs = scroll.verticalScrollbar;
        if (vs && scroll.vertical && (!vs.autoHide || node.ch > h + 1)) {
          const size = (vs.size || 20) * S;
          const track = document.createElement('div');
          track.className = 'el'; track.style.right = '0px'; track.style.top = '0px'; track.style.width = size + 'px'; track.style.height = h + 'px';
          track.style.background = rgba(vs.trackColor, '0.1 0.1 0.1 0.5'); track.style.zIndex = '5';
          const handle = document.createElement('div');
          handle.className = 'el'; handle.style.left = '0px'; handle.style.top = '0px'; handle.style.width = size + 'px';
          handle.style.height = Math.max(size, h * Math.min(1, h / Math.max(1, node.ch))) + 'px';
          handle.style.background = rgba(vs.handleColor, '0.5 0.5 0.5 1');
          track.appendChild(handle); d.appendChild(track);
        }
      }
      if (el.name) nodes[el.name] = node;
    }
  };
})();
