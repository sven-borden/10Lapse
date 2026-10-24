// Shared helpers for reel.html: timing curves, colour, seeded noise and small DOM utilities.
'use strict';

const W = 1920, H = 1080;
const TAU = Math.PI * 2;

const clamp = (x, a = 0, b = 1) => (x < a ? a : x > b ? b : x);
const lerp = (a, b, u) => a + (b - a) * u;
const prog = (t, a, b) => clamp((t - a) / (b - a));
const smooth = u => u * u * (3 - 2 * u);
// A window that rises over [a, a + rin], holds, and falls over [b - rout, b].
const win = (t, a, b, rin = 0.3, rout = 0.3) => Math.min(prog(t, a, a + rin), 1 - prog(t, b - rout, b));

// Cubic bezier timing curves, solved by Newton then bisection.
function bez(x1, y1, x2, y2) {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
  const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
  const sx = u => ((ax * u + bx) * u + cx) * u, sy = u => ((ay * u + by) * u + cy) * u;
  const dx = u => (3 * ax * u + 2 * bx) * u + cx;
  return x => {
    if (x <= 0) return 0;
    if (x >= 1) return 1;
    let u = x;
    for (let i = 0; i < 8; i++) {
      const e = sx(u) - x;
      if (Math.abs(e) < 1e-7) return sy(u);
      const d = dx(u);
      if (Math.abs(d) < 1e-6) break;
      u -= e / d;
    }
    let lo = 0, hi = 1;
    u = x;
    for (let i = 0; i < 40; i++) { if (sx(u) < x) lo = u; else hi = u; u = (lo + hi) / 2; }
    return sy(u);
  };
}
// DESIGN.md's motion tokens (Enter, Exit, Move), plus the reel's own camera curves.
const E = {
  enter: bez(0.16, 1, 0.3, 1),
  exit: bez(0.7, 0, 0.84, 0),
  move: bez(0.65, 0, 0.35, 1),
  whip: bez(0.8, 0, 0.1, 1),
  glide: bez(0.33, 0, 0.15, 1),
  lin: u => u,
  sine: u => -(Math.cos(Math.PI * u) - 1) / 2,
  inQuad: u => u * u,
  inCubic: u => u * u * u,
  outCubic: u => 1 - Math.pow(1 - u, 3),
  expoOut: u => (u >= 1 ? 1 : 1 - Math.pow(2, -10 * u)),
  expoIn: u => (u <= 0 ? 0 : Math.pow(2, 10 * u - 10)),
  expoInOut: u => (u <= 0 ? 0 : u >= 1 ? 1 : u < 0.5 ? Math.pow(2, 20 * u - 10) / 2 : (2 - Math.pow(2, -20 * u + 10)) / 2),
};
// eased progress of t through [a, b]
const ep = (t, a, b, f = E.move) => f(prog(t, a, b));

// mulberry32
function rng(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6D2B79F5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
// 1D value noise and fractal sums, seeded and periodic over 4096 cells.
function noise1(seed) {
  const r = rng(seed), v = new Float32Array(4096);
  for (let i = 0; i < 4096; i++) v[i] = r() * 2 - 1;
  return x => {
    const i = Math.floor(x), f = x - i, a = v[i & 4095], b = v[(i + 1) & 4095];
    return a + (b - a) * smooth(f);
  };
}
function fbm(n, x, oct = 4, lac = 2.03, gain = 0.5) {
  let s = 0, amp = 1, f = 1, norm = 0;
  for (let o = 0; o < oct; o++) { s += amp * n(x * f + o * 17.31); norm += amp; amp *= gain; f *= lac; }
  return s / norm;
}

// colour
function hx(c) { return [parseInt(c.slice(1, 3), 16), parseInt(c.slice(3, 5), 16), parseInt(c.slice(5, 7), 16)]; }
const C = c => (typeof c === 'string' ? hx(c) : c);
function mixc(a, b, u) { a = C(a); b = C(b); return [a[0] + (b[0] - a[0]) * u, a[1] + (b[1] - a[1]) * u, a[2] + (b[2] - a[2]) * u]; }
function mulc(a, k) { a = C(a); return [a[0] * k, a[1] * k, a[2] * k]; }
function rgba(c, al = 1) {
  c = C(c);
  return `rgba(${Math.round(clamp(c[0], 0, 255))},${Math.round(clamp(c[1], 0, 255))},${Math.round(clamp(c[2], 0, 255))},${+clamp(al).toFixed(4)})`;
}
function rampc(stops, t) {
  t = clamp(t);
  for (let i = 0; i < stops.length - 1; i++) {
    const a = stops[i][0], b = stops[i + 1][0];
    if (t <= b) return mixc(stops[i][1], stops[i + 1][1], (t - a) / (b - a));
  }
  return C(stops[stops.length - 1][1]);
}
function rampn(stops, t) {
  t = clamp(t);
  for (let i = 0; i < stops.length - 1; i++) {
    const a = stops[i][0], b = stops[i + 1][0];
    if (t <= b) return lerp(stops[i][1], stops[i + 1][1], (t - a) / (b - a));
  }
  return stops[stops.length - 1][1];
}

// DOM
const stage = () => document.getElementById('stage');
function el(tag, css, parent, html) {
  const e = document.createElement(tag);
  if (css) e.style.cssText = css;
  if (html != null) e.innerHTML = html;
  (parent || stage()).appendChild(e);
  return e;
}
function sv(tag, attrs, parent) {
  const e = document.createElementNS('http://www.w3.org/2000/svg', tag);
  for (const k in attrs) e.setAttribute(k, attrs[k]);
  if (parent) parent.appendChild(e);
  return e;
}
function canvas(w, h, css, parent) { const c = el('canvas', css || '', parent); c.width = w; c.height = h; return c; }
function offscreen(w, h) { const c = document.createElement('canvas'); c.width = Math.max(1, Math.round(w)); c.height = Math.max(1, Math.round(h)); return c; }
function refs(root) { const r = {}; root.querySelectorAll('[data-r]').forEach(n => { r[n.dataset.r] = n; }); return r; }
function show(node, on) { node.style.display = on ? '' : 'none'; }

// numbers
const pad4 = n => String(n).padStart(4, '0');
const thousands = n => String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ',');
// The shoot: one photo every 5 seconds from 17:42:00, 1,440 photos.
const SHOTS = 1440, INTERVAL = 5, START_S = 17 * 3600 + 42 * 60;
function clockAt(i) {
  const s = START_S + INTERVAL * i;
  return `${Math.floor(s / 3600)}:${String(Math.floor(s % 3600 / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
}
