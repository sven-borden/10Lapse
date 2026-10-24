// The reel's timeline: every frame is a pure function of t (seconds). render.mjs calls window.renderAt(t).
// 59 s at 120 BPM: a one-second pickup, then a bar every 2 s starting on the odd seconds, so the cuts land on odd
// seconds and the frame the site's contact sheet takes every even second sits in the middle of a shot.
'use strict';

const ST = stage();
const V169 = { x: 0, y: 0.078, w: 1.5, h: 0.84375 };
const V32 = { x: 0, y: 0, w: 1.5, h: 1 };
const PAPER = '#F3F1EC', INK = '#1C1B19', INK2 = '#5D5A55', DINK = '#F4F2EE', DINK2 = '#C3BFB8';
const AMBER = '#E8962E', AMBER_D = '#F0A54A', AMBER_I = '#B5620D';
const ICONC = ['#8EC0EA', '#F3C46B', '#EE8F4F', '#B4567A', '#2B3A75'];
const DUR = 59;

// ------------------------------------------------------------ the photos: 160 renders across the evening
const NT = 160;
const TEX = [];
for (let j = 0; j < NT; j++) {
  const c = offscreen(300, 200), x = c.getContext('2d');
  WORLD.draw(x, 300, 200, j / (NT - 1), V32, { small: true, ripple: j * 5 });
  x.globalCompositeOperation = 'destination-in';
  x.beginPath(); x.roundRect(0, 0, 300, 200, 5); x.fill();
  TEX.push(c);
}
const texAt = T => TEX[Math.round(clamp(T) * (NT - 1))];

// the world drawn into any rectangle of a context
const SCR = offscreen(1920, 1280), SCX = SCR.getContext('2d');
function worldInto(ctx, x, y, w, h, T, view, o, radius) {
  const cw = Math.max(2, Math.round(w)), ch = Math.max(2, Math.round(h));
  if (cw > SCR.width || ch > SCR.height) { SCR.width = Math.max(SCR.width, cw); SCR.height = Math.max(SCR.height, ch); }
  SCX.setTransform(1, 0, 0, 1, 0, 0);
  SCX.clearRect(0, 0, cw + 4, ch + 4);
  WORLD.draw(SCX, cw, ch, T, view, o || {});
  ctx.save();
  if (radius) { ctx.beginPath(); ctx.roundRect(x, y, w, h, radius); ctx.clip(); }
  ctx.drawImage(SCR, 0, 0, cw, ch, x, y, w, h);
  ctx.restore();
}
// a view of the 3:2 photo with the aspect of w x h, centred on (cx, cy), zoomed by z (1 = as wide as the photo, or as tall)
function viewFor(w, h, cx = 0.75, cy = 0.5, z = 1) {
  const a = w / h;
  let vw = 1.5, vh = 1.5 / a;
  if (vh > 1) { vh = 1; vw = a; }
  vw /= z; vh /= z;
  return { x: clamp(cx - vw / 2, 0, 1.5 - vw), y: clamp(cy - vh / 2, 0, 1 - vh), w: vw, h: vh };
}

// position over u in [0, 1] for a speed that ramps up over `acc`, holds, and ramps down over `dec`
function travel(acc, dec) {
  const N = 4000, P = new Float64Array(N + 1);
  let s = 0;
  for (let i = 0; i <= N; i++) { P[i] = s; const u = (i + 0.5) / N; s += Math.min(smooth(clamp(u / acc)), smooth(clamp((1 - u) / dec))) / N; }
  for (let i = 0; i <= N; i++) P[i] /= s;
  return u => { const x = clamp(u) * N, i = Math.floor(x); return i >= N ? 1 : P[i] + (P[i + 1] - P[i]) * (x - i); };
}

// ------------------------------------------------------------ type: masked word reveals
function words(parent, text, css) {
  const out = [];
  text.split(' ').forEach((wd, k, arr) => {
    const o = el('span', '', parent); o.className = 'mw';
    const i = el('span', css || '', o, wd); i.className = 'mi';
    out.push(i);
    if (k < arr.length - 1) parent.appendChild(document.createTextNode(' '));
  });
  return out;
}
function chars(parent, text, css) {
  const out = [];
  for (const ch of text) {
    const o = el('span', '', parent); o.className = 'mw';
    const i = el('span', css || '', o, ch === ' ' ? '&nbsp;' : ch); i.className = 'mi';
    out.push(i);
  }
  return out;
}
// in over 0.7 s from tin (Enter, staggered), out over 0.34 s from tout (Exit)
function reveal(nodes, t, tin, tout, stag = 0.045, dur = 0.7, dist = 112) {
  nodes.forEach((n, k) => {
    const u = E.enter(prog(t, tin + k * stag, tin + k * stag + dur));
    const o = tout == null ? 0 : E.exit(prog(t, tout + k * stag * 0.5, tout + k * stag * 0.5 + 0.34));
    n.style.transform = `translate3d(0,${((1 - u) * dist - o * dist).toFixed(2)}%,0)`;
    n.style.opacity = (Math.min(1, u * 2.2) * (1 - o)).toFixed(3);
  });
}
// a kicker, a headline and a sub line, set left at (x, y)
function Title(parent, o) {
  const root = el('div', `position:absolute;left:${o.x}px;top:${o.y}px;width:${o.w || 1300}px;color:${o.color};text-align:${o.align || 'left'}`, parent);
  const r = { root, k: [], h: [], s: [] };
  if (o.kicker) { const k = el('div', `color:${o.color2};margin-bottom:10px`, root); k.className = 'kick'; r.k = words(k, o.kicker); }
  const h = el('div', o.headSize ? `font-size:${o.headSize}px;line-height:${Math.round(o.headSize * 1.08)}px` : '', root); h.className = 'head';
  r.h = words(h, o.head);
  if (o.sub) { const s = el('div', `color:${o.color2};margin-top:16px;max-width:${o.subW || 980}px;${o.align === 'center' ? 'margin-left:auto;margin-right:auto' : ''}`, root); s.className = 'subt'; r.s = words(s, o.sub); }
  r.set = (t, tin, tout, subIn) => {
    reveal(r.k, t, tin - 0.1, tout);
    reveal(r.h, t, tin, tout, o.stag || 0.045, o.dur || 0.7);
    reveal(r.s, t, subIn != null ? subIn : tin + 0.35, tout, o.subStag || 0.03, o.subDur || 0.7);
  };
  return r;
}
function scrim(parent, css) { return el('div', `position:absolute;pointer-events:none;${css}`, parent); }

const SCENES = [];
function scene(t0, t1, z, update) {
  const root = el('div', `z-index:${z};display:none`, ST); root.className = 'layer';
  const s = { t0, t1, root, update: null };
  SCENES.push(s);
  return s;
}

// ================================================================ OPEN  the tripod, then 1,440 photos  [0, 7.1)
// One 3D space: photo 1 at z = 0 in front of a tunnel of rings, eight photos to a ring, the last photo at the end.
const OPEN = (() => {
  const s = scene(0, 8.15, 10);
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const F = 980, M = 8, R = 180, DZ = 0.38, RHO = 1.62, CW = 1.0, CH = 2 / 3, Z0 = 3.2;
  const ZF = Z0 + (R - 1) * DZ + 1.8;                 // the last photo, just past the last ring
  const D_FULL = F * CW / W;                           // distance at which a photo is as wide as the frame
  const D_CARD = F * CW / 620, D_END = F * CW / 960;
  const CARDS = [];
  for (let r = 0; r < R; r++) for (let m = 0; m < M; m++) {
    const p = Math.min(1439, 1 + Math.round((r * M + m) / (R * M - 1) * 1438));
    const th = (m + 0.5) * TAU / M + r * 0.13;
    CARDS.push({ X: RHO * Math.cos(th) * 1.15, Y: RHO * Math.sin(th) * 0.86, Z: Z0 + r * DZ + (m % 2) * DZ * 0.5, p, tex: texAt(p / 1439) });
  }
  const FLY = travel(0.3, 0.42);
  // the shutter: quarter notes, then eighths into the downbeat at 3
  const SHOT = [0.5, 1.0, 1.5, 2.0, 2.25, 2.5, 2.75];
  const shotsAt = t => { let n = 0; for (const x of SHOT) if (t >= x) n++; return n; };
  const lastShot = t => { let v = -9; for (const x of SHOT) if (t >= x) v = x; return v; };

  // camera distance to photo 1, then the flight
  function camZ(t) {
    if (t < 3.0) return -D_FULL / (1 + 0.035 * E.sine(prog(t, 0, 3)));
    if (t < 3.75) return -lerp(D_FULL / 1.035, D_CARD, E.move(prog(t, 3.0, 3.75)));
    return lerp(-D_CARD, ZF - D_END, FLY(prog(t, 3.75, 6.75)));
  }

  // HUD: viewfinder corners, interval, counter and clock
  const hud = el('div', 'position:absolute;inset:0;color:#fff', s.root);
  const hsv = sv('svg', { width: W, height: H, style: 'position:absolute;left:0;top:0;overflow:visible' }, hud);
  const corners = [0, 1, 2, 3].map(() => sv('path', { fill: 'none', stroke: '#FFFFFF', 'stroke-width': 2.5, 'stroke-linecap': 'round', 'stroke-opacity': 0.92 }, hsv));
  const hudL = el('div', 'position:absolute;left:104px;top:86px;font:600 22px/28px var(--txt);letter-spacing:0.01em', hud, 'Interval 5 s');
  const hudR = el('div', 'position:absolute;right:104px;top:86px;font:600 22px/28px var(--txt);display:flex;gap:12px;align-items:center', hud);
  const dot = el('span', `width:12px;height:12px;border-radius:6px;background:${AMBER_D}`, hudR);
  const cnt = el('span', 'font-variant-numeric:tabular-nums', hudR, 'IMG_0001.JPG');
  const hudC = el('div', 'position:absolute;right:104px;bottom:86px;font:600 22px/28px var(--txt);font-variant-numeric:tabular-nums', hud, '17:42:00');
  const flash = el('div', 'position:absolute;inset:0;background:#FFFDF6;opacity:0', s.root);
  const sc = scrim(s.root, 'left:0;bottom:0;width:1920px;height:520px;background:linear-gradient(0deg,rgba(6,10,22,0.62),rgba(6,10,22,0))');
  const t1 = Title(s.root, { x: 104, y: 802, head: 'One photo every 5 seconds.', color: '#FFFFFF', headSize: 96, dur: 0.55, stag: 0.03 });
  s.root.appendChild(hud);
  // the count during the flight
  const cntBox = el('div', `position:absolute;left:0;top:0;width:1920px;color:${INK};text-align:center;transform-origin:960px 0`, s.root);
  const cntRow = el('div', 'display:flex;align-items:baseline;justify-content:center;gap:24px', cntBox);
  const big = el('div', 'font:600 150px/150px var(--disp);letter-spacing:-0.03em;font-variant-numeric:tabular-nums', cntRow, '1');
  const bigL = el('div', 'font:600 60px/60px var(--disp);letter-spacing:-0.02em', cntRow, 'photos');
  const clk = el('div', `font:400 30px/40px var(--txt);color:${INK2};font-variant-numeric:tabular-nums;margin-top:12px`, cntBox, '17:42:00');

  s.update = t => {
    const zc = camZ(t);
    const n = shotsAt(t);
    const Tlive = 0.002 + n * INTERVAL / 7200;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = PAPER; ctx.fillRect(0, 0, W, H);
    // the tunnel
    const items = [];
    for (const c of CARDS) {
      const d = c.Z - zc;
      if (d < 0.12 || d > 30) continue;
      items.push([d, c]);
    }
    const dF = ZF - zc;
    items.sort((a, b) => b[0] - a[0]);
    const reveal0 = prog(t, 3.0, 3.6);
    const roll = 0.11 * Math.sin(Math.PI * E.sine(prog(t, 3.9, 6.6)));
    ctx.save();
    ctx.translate(W / 2, H / 2); ctx.rotate(roll); ctx.translate(-W / 2, -H / 2);
    for (const [d, c] of items) {
      // the last rings flare out like the mouth of a horn, so the tunnel opens onto the paper
      const fl = 1 + 2.6 * smooth(clamp((c.Z - (ZF - 13)) / 11));
      let a = Math.pow(clamp((28 - d) / 16), 1.4) * clamp((d - 0.12) / 0.4) * reveal0;
      if (a <= 0.004) continue;
      const w = F * CW / d, h = F * CH / d;
      const x = W / 2 + F * c.X * fl / d - w / 2, y = H / 2 + F * c.Y * fl / d - h / 2;
      if (x > W || y > H || x + w < 0 || y + h < 0) continue;
      ctx.globalAlpha = a;
      ctx.drawImage(c.tex, x, y, w, h);
    }
    ctx.restore();
    // light at the end of the tunnel: keeps the count readable
    const gA = prog(t, 3.5, 4.0) * (1 - prog(t, 6.0, 6.5));
    if (gA > 0) {
      const gg = ctx.createRadialGradient(W / 2, H / 2, 0, W / 2, H / 2, 360);
      gg.addColorStop(0, `rgba(243,241,236,${(0.86 * gA).toFixed(3)})`); gg.addColorStop(0.5, `rgba(243,241,236,${(0.6 * gA).toFixed(3)})`); gg.addColorStop(1, 'rgba(243,241,236,0)');
      ctx.fillStyle = gg; ctx.fillRect(W / 2 - 360, H / 2 - 360, 720, 720);
    }
    // the last photo, at the end of the tunnel
    if (t > 5.95 && dF > 0.1 && t < 6.96) {
      const w = F * CW / dF, h = F * CH / dF;
      const a = E.sine(prog(t, 5.98, 6.3));
      ctx.save(); ctx.globalAlpha = a;
      if (w > 260) worldInto(ctx, W / 2 - w / 2, H / 2 - h / 2, w, h, 1, V32, { ripple: 7195 }, Math.max(3, w * 0.006));
      else ctx.drawImage(TEX[NT - 1], W / 2 - w / 2, H / 2 - h / 2, w, h);
      ctx.restore();
    }
    // photo 1: the live view, then a photo in the tunnel
    const d0 = -zc;
    if (d0 > 0.12) {
      const w = F * CW / d0, h = F * CH / d0;
      const a = clamp((d0 - 0.12) / 0.35);
      ctx.save(); ctx.globalAlpha = a;
      const live = t < 3.0;
      worldInto(ctx, W / 2 - w / 2, H / 2 - h / 2, w, h, live ? Tlive : 0.012, V32,
        { cloudX: (live ? Tlive : 0.012) * 1.25 + Math.min(t, 3) * 0.0012, ripple: Math.min(t, 3.0) * 1.0, twinkle: t }, t < 3 ? 0 : Math.max(3, w * 0.006));
      ctx.restore();
    }
    // HUD and the opening line
    const ls = lastShot(t), since = t - ls;
    const hudA = 1 - prog(t, 2.95, 3.25);
    hud.style.opacity = hudA;
    sc.style.opacity = 1 - prog(t, 2.9, 3.3);
    const m = 72 + 10 * Math.exp(-since / 0.07) * (since >= 0 ? 1 : 0), arm = 64;
    const P = [[m, m, 1, 1], [W - m, m, -1, 1], [W - m, H - m, -1, -1], [m, H - m, 1, -1]];
    corners.forEach((c, i) => { const [x, y, sx, sy] = P[i]; c.setAttribute('d', `M${x} ${y + sy * arm}V${y}H${x + sx * arm}`); });
    flash.style.opacity = since >= 0 && since < 0.3 && t < 3 ? (0.06 * Math.exp(-since / 0.045)).toFixed(3) : 0;   // under WCAG 2.3.1's 10 % change
    dot.style.opacity = since >= 0 && since < 0.16 ? 1 : 0.28;
    cnt.textContent = `IMG_${pad4(1 + n)}.JPG`;
    hudC.textContent = clockAt(n);
    t1.set(t, 0.05, 2.92);
    // the count
    // the count sits in the light at the end of the tunnel, then drops under the last photo as it arrives
    const cA = prog(t, 4.2, 4.5) * (1 - prog(t, 7.9, 8.1));
    const down = E.move(prog(t, 5.72, 6.25));
    const sc2 = lerp(1, 0.62, down);
    cntBox.style.opacity = cA;
    cntBox.style.transform = `translateY(${lerp(395, 878, down) + (1 - E.enter(prog(t, 4.2, 4.75))) * 40 - E.exit(prog(t, 7.9, 8.12)) * 24}px) scale(${sc2})`;
    clk.style.opacity = 1 - prog(t, 5.7, 5.95);
    const p = clamp(Math.round(1 + 1438 * FLY(prog(t, 3.75, 6.75))), 1, 1439);
    const pp = t >= 6.62 ? 1440 : p;
    big.textContent = thousands(pp);
    bigL.textContent = pp === 1 ? 'photo' : 'photos';
    clk.textContent = clockAt(pp - 1);
  };
  s.camZ = camZ; s.D_END = D_END;
  return s;
})();

// ================================================================ TITLE  the last photo becomes five frames, the icon  [6.95, 13.3)
const LOCK = { icon: 220, gap: 52, size: 150, cy: 470 };
const WIN = { x: 240, y: 90 };     // the window's place in S4, where the icon lands
const TITLE = (() => {
  const s = scene(6.95, 13.45, 20);
  s.root.style.background = 'transparent';
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const TK = [0.0, 0.26, 0.4, 0.58, 1.0];
  const CARD = { w: 960, h: 640 };
  const IMG = TK.map(T => { const c = offscreen(CARD.w, CARD.h); WORLD.draw(c.getContext('2d'), CARD.w, CARD.h, T, V32, { ripple: T * 7200 }); return c; });
  const FLIP = [7.0, 7.125, 7.25, 7.375];
  const word = el('div', `position:absolute;left:0;top:0;color:${INK};white-space:nowrap`, s.root);
  word.className = 'head';
  word.style.cssText += `;font-size:${LOCK.size}px;line-height:${LOCK.size * 1.1}px;letter-spacing:-0.02em`;
  const wl = chars(word, '10Lapse');
  const tag = el('div', `position:absolute;left:0;width:1920px;top:${LOCK.cy + 200}px;text-align:center;color:${INK2};font:400 40px/52px var(--txt)`, s.root);
  const tw = words(tag, 'Turn a folder of photos into a timelapse.');
  let wordW = 0;
  s.layout = () => { wordW = word.getBoundingClientRect().width; };
  const lockX = () => (W - (LOCK.icon + LOCK.gap + wordW)) / 2;   // left of the icon in the lockup

  // geometry of the five frames at time t: the card, then the icon tile
  function bands(t) {
    const u = E.move(prog(t, 8.0, 8.75));
    const fly = E.move(prog(t, 12.8, 13.4));
    const side = E.move(prog(t, 8.55, 9.3));
    const size = lerp(LOCK.icon, 16, fly);
    let cx = lerp(W / 2, lockX() + LOCK.icon / 2, side), cy = lerp(H / 2, LOCK.cy, side);
    cx = lerp(cx, WIN.x + 16 + 8, fly); cy = lerp(cy, WIN.y + 12 + 8, fly);
    const w = lerp(CARD.w, size, u), h = lerp(CARD.h, size, u);
    const gap = lerp(0, size * 0.75 / 40, u);
    const bw = (w - 4 * gap) / 5;
    return { x: cx - w / 2, y: cy - h / 2, w, h, gap, bw, rad: lerp(Math.max(3, CARD.w * 0.006), size * 10 / 40, u), u };
  }

  s.update = t => {
    if (!wordW) s.layout();
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const g = bands(t);
    const flat = E.move(prog(t, 8.05, 8.6));
    ctx.save();
    ctx.beginPath(); ctx.roundRect(g.x, g.y, g.w, g.h, g.rad); ctx.clip();
    for (let k = 0; k < 5; k++) {
      const bx = g.x + k * (g.bw + g.gap);
      // flip: the band turns edge on and shows its time of day on the way back
      let sx = 1, img = IMG[4];
      if (k < 4) {
        const fu = prog(t, FLIP[k], FLIP[k] + 0.2);
        sx = Math.abs(Math.cos(Math.PI * fu));
        img = fu < 0.5 ? IMG[4] : IMG[k];
      }
      const cx = bx + g.bw / 2, ww = g.bw * sx;
      ctx.save();
      ctx.beginPath(); ctx.rect(cx - ww / 2, g.y, ww, g.h); ctx.clip();
      ctx.drawImage(img, k * CARD.w / 5, 0, CARD.w / 5, CARD.h, cx - ww / 2, g.y, ww, g.h);
      if (flat > 0) { ctx.globalAlpha = flat; ctx.fillStyle = ICONC[k]; ctx.fillRect(cx - ww / 2, g.y, ww, g.h); ctx.globalAlpha = 1; }
      // edge shading while the band turns
      if (sx < 0.999) { ctx.fillStyle = `rgba(0,0,0,${(0.35 * (1 - sx)).toFixed(3)})`; ctx.fillRect(cx - ww / 2, g.y, ww, g.h); }
      ctx.restore();
    }
    // the icon's sheen over the top half
    if (flat > 0) {
      const sg = ctx.createLinearGradient(0, g.y, 0, g.y + g.h);
      sg.addColorStop(0, `rgba(255,255,255,${(0.22 * flat).toFixed(3)})`); sg.addColorStop(0.5, 'rgba(255,255,255,0)');
      ctx.fillStyle = sg; ctx.fillRect(g.x, g.y, g.w, g.h);
    }
    ctx.restore();
    // a soft shadow under the card while it is a photo
    word.style.transform = `translate(${g.x + g.w + LOCK.gap}px, ${LOCK.cy - LOCK.size * 0.62}px)`;
    reveal(wl, t, 8.85, 12.75, 0.04, 0.7, 115);
    reveal(tw, t, 9.0, 12.8, 0.02, 0.5);
  };
  return s;
})();

