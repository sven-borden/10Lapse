// The shoot the reel is about: a mountain lake from 17:42 to 19:42, drawn by code (there are no real photos).
// World units: x in [0, 1.5], y in [0, 1], the 3:2 frame of the camera's photos.
// T in [0, 1] is the time of the shoot: 0 is photo 1 (17:42:00), 1 is photo 1,440 (19:41:55).
'use strict';

const WORLD = (() => {
  const YW = 0.64;                 // waterline
  const SEQ = 7200;                // seconds the shoot lasts
  const nz = noise1(41), nz2 = noise1(42), nz3 = noise1(43);

  // ------------------------------------------------------------ sky and light by time of day
  const ZEN = [[0, '#4C8BCC'], [0.2, '#6A98C8'], [0.34, '#8590B8'], [0.44, '#76699E'], [0.54, '#454583'], [0.66, '#232C64'], [0.8, '#10173C'], [1, '#050817']];
  const MID = [[0, '#8EBEE6'], [0.2, '#A9C4DA'], [0.34, '#D9B8A2'], [0.44, '#CB8796'], [0.54, '#7A64A0'], [0.66, '#394283'], [0.8, '#1A2350'], [1, '#0A1027']];
  const HOR = [[0, '#D9EAF2'], [0.2, '#F4DDAE'], [0.34, '#F7B46E'], [0.42, '#F28C58'], [0.5, '#DA6878'], [0.58, '#8C68A0'], [0.66, '#4A4F8E'], [0.8, '#232D60'], [1, '#111830']];
  const SNOW_L = [[0, '#F6F8FA'], [0.22, '#FFF1D8'], [0.36, '#FFD2A6'], [0.46, '#F7B2B6'], [0.54, '#B9A6CC'], [0.66, '#7C82B0'], [0.82, '#3E4872'], [1, '#2E3760']];
  const SNOW_S = [[0, '#B9CADC'], [0.22, '#C0C2D8'], [0.36, '#A9A2C5'], [0.46, '#9087B6'], [0.54, '#6E6F9E'], [0.66, '#4F5689'], [0.82, '#283158'], [1, '#1E2647']];
  const ROCK_L = [[0, '#8C9AAE'], [0.22, '#A08E8A'], [0.36, '#9B7678'], [0.46, '#7D6482'], [0.54, '#56527A'], [0.66, '#363B66'], [0.82, '#171D3B'], [1, '#10152D']];
  const ROCK_S = [[0, '#66778F'], [0.22, '#6A6A88'], [0.36, '#5E5579'], [0.46, '#4D4870'], [0.54, '#3C3D66'], [0.66, '#2A3058'], [0.82, '#121733'], [1, '#0C1027']];
  const MIDL = [[0, '#3E5A52'], [0.22, '#3E4B4C'], [0.36, '#3C3B4E'], [0.5, '#2F2E48'], [0.66, '#1D2140'], [0.82, '#0C1024'], [1, '#080B1A']];
  const MIDR = [[0, '#55715E'], [0.22, '#6E6A55'], [0.34, '#6E5552'], [0.44, '#4A3E52'], [0.56, '#2C2B48'], [0.7, '#161A36'], [0.85, '#0A0E21'], [1, '#070A18']];
  const FORE = [[0, '#1B2622'], [0.3, '#1E1F25'], [0.55, '#15172A'], [0.8, '#070912'], [1, '#04050B']];
  const WATER = [[0, '#2D5872'], [0.22, '#3C5468'], [0.36, '#4B4560'], [0.5, '#3B3459'], [0.66, '#1B2350'], [0.82, '#0B1131'], [1, '#060A1E']];
  const CLOUD_L = [[0, '#FFFFFF'], [0.22, '#FFF2DA'], [0.34, '#FFD3A2'], [0.42, '#FFB28A'], [0.5, '#F49AA6'], [0.58, '#A792C2'], [0.7, '#56588E'], [0.85, '#2A3160'], [1, '#1A2048']];
  const CLOUD_S = [[0, '#C7D5E4'], [0.22, '#CBBFC0'], [0.34, '#A7889A'], [0.42, '#97738F'], [0.5, '#7B6390'], [0.58, '#5B5486'], [0.7, '#363C6E'], [0.85, '#1C2350'], [1, '#141A3C']];

  function light(T) {
    const sunT = clamp(T / 0.42);
    return {
      zen: rampc(ZEN, T), mid: rampc(MID, T), hor: rampc(HOR, T),
      snowL: rampc(SNOW_L, T), snowS: rampc(SNOW_S, T), rockL: rampc(ROCK_L, T), rockS: rampc(ROCK_S, T),
      midL: rampc(MIDL, T), midR: rampc(MIDR, T), fore: rampc(FORE, T), water: rampc(WATER, T),
      cloudL: rampc(CLOUD_L, T), cloudS: rampc(CLOUD_S, T),
      sunX: lerp(0.71, 0.585, sunT), sunY: lerp(0.135, 0.47, Math.pow(sunT, 1.08)),
      sunCol: mixc('#FFF9EA', '#FFA868', clamp((T - 0.08) / 0.32)),
      glowCol: mixc('#FFF0C8', '#FF8E5C', clamp((T - 0.05) / 0.35)),
      sunO: clamp((0.47 - T) / 0.05),
      glowO: clamp(1.25 - Math.abs(T - 0.36) / 0.2) * 0.85 + 0.15 * clamp(1 - T / 0.3),
      cloudO: 1 - clamp((T - 0.7) / 0.16),
      stars: clamp((T - 0.62) / 0.24),
      milky: clamp((T - 0.78) / 0.16),
      lightsOn: clamp((T - 0.52) / 0.1),
      night: clamp((T - 0.6) / 0.3),
    };
  }

  // ------------------------------------------------------------ the land
  // far range: pyramids with fractal detail; peaks [x, top y, slope]
  const PEAKS = [[0.06, 0.36, 1.1], [0.29, 0.30, 0.95], [0.53, 0.205, 0.92], [0.77, 0.315, 1.0], [0.97, 0.265, 0.95], [1.2, 0.345, 1.05], [1.42, 0.285, 1.0]];
  function farRidge(x) {
    let y = 0.62;
    for (const [px, py, s] of PEAKS) y = Math.min(y, py + s * Math.abs(x - px) * (1 + 0.25 * Math.abs(x - px)));
    y += 0.016 * fbm(nz, x * 9, 4) + 0.006 * fbm(nz2, x * 37, 3);
    return Math.min(y, 0.62);
  }
  const SNOWLINE = x => 0.385 + 0.03 * fbm(nz3, x * 6, 3);

  // left mid range (forested), coming down to the water at x 0.6
  function leftRidge(x) {
    const u = clamp(x / 0.62);
    let y = lerp(0.215, YW + 0.004, Math.pow(u, 1.6));
    y += 0.012 * fbm(nz2, x * 12 + 3, 3) * (1 - u * 0.6);
    return y;
  }
  // right mid range with the pass road, coming down to the water at x 0.93
  function rightRidge(x) {
    const u = clamp((1.52 - x) / 0.6);
    let y = lerp(0.31, YW + 0.003, Math.pow(u, 1.5));
    y += 0.011 * fbm(nz3, x * 13 + 7, 3) * (1 - u * 0.5);
    return y;
  }
  // pines along a ridge: small spikes, seeded
  function pineRidge(fn, x0, x1, seed, dens, hmax) {
    const r = rng(seed), pts = [];
    const n = Math.round((x1 - x0) * dens);
    for (let i = 0; i <= n; i++) {
      const x = x0 + (x1 - x0) * i / n;
      const y = fn(x);
      const h = r() < 0.55 ? hmax * (0.4 + r() * 0.6) : 0;
      if (h > 0) {
        const w = h * (0.32 + r() * 0.12);
        pts.push([x - w, y + h * 0.05], [x - w * 0.55, y - h * 0.45], [x - w * 0.25, y - h * 0.42], [x, y - h], [x + w * 0.25, y - h * 0.42], [x + w * 0.55, y - h * 0.45], [x + w, y + h * 0.05]);
      } else pts.push([x, y]);
    }
    return pts;
  }
  const LEFT = pineRidge(leftRidge, -0.02, 0.64, 7, 260, 0.016);
  const RIGHT = pineRidge(rightRidge, 0.9, 1.54, 9, 260, 0.014);

  // the pass road on the right range: a lakeside road and a serpentine up to the ridge
  function chaikin(pts, it) {
    let p = pts;
    for (let k = 0; k < it; k++) {
      const q = [p[0]];
      for (let i = 0; i < p.length - 1; i++) {
        const a = p[i], b = p[i + 1];
        q.push([a[0] * 0.75 + b[0] * 0.25, a[1] * 0.75 + b[1] * 0.25], [a[0] * 0.25 + b[0] * 0.75, a[1] * 0.25 + b[1] * 0.75]);
      }
      q.push(p[p.length - 1]);
      p = q;
    }
    return p;
  }
  function measure(pts) {
    const L = [0];
    for (let i = 1; i < pts.length; i++) L.push(L[i - 1] + Math.hypot(pts[i][0] - pts[i - 1][0], pts[i][1] - pts[i - 1][1]));
    return { pts, L, len: L[L.length - 1] };
  }
  function at(path, s) {
    const { pts, L } = path;
    s = clamp(s, 0, path.len);
    let lo = 0, hi = L.length - 1;
    while (hi - lo > 1) { const m = (lo + hi) >> 1; if (L[m] <= s) lo = m; else hi = m; }
    const u = (s - L[lo]) / Math.max(1e-9, L[hi] - L[lo]);
    return [lerp(pts[lo][0], pts[hi][0], u), lerp(pts[lo][1], pts[hi][1], u)];
  }
  const ROAD = measure(chaikin([
    [0.905, 0.6475], [1.02, 0.651], [1.19, 0.6575], [1.4, 0.629], [1.43, 0.6215], [1.405, 0.615], [1.16, 0.6], [1.135, 0.592],
    [1.16, 0.585], [1.385, 0.566], [1.41, 0.558], [1.385, 0.552], [1.19, 0.536], [1.165, 0.528], [1.19, 0.521], [1.37, 0.502],
    [1.39, 0.494], [1.365, 0.488], [1.25, 0.474], [1.245, 0.452],
  ], 4));
  const SHORE = measure([[0.86, 0.6455], [1.19, 0.6575], [1.56, 0.675]]);
  const LAMPS = (() => { const a = []; for (let s = 0.02; s < SHORE.len; s += 0.033) a.push({ s, on: 0.56 + ((s * 97) % 1) * 0.06 }); return a; })();
  // Cars: arc-length position at real time tau (s). Half climb (taillights), half descend (headlights).
  const CARS = (() => {
    const r = rng(21), a = [];
    for (let i = 0; i < 9; i++) a.push({ s0: r() * ROAD.len, v: (0.0052 + r() * 0.0026) * (i % 2 ? -1 : 1), path: ROAD });
    for (let i = 0; i < 4; i++) a.push({ s0: r() * SHORE.len, v: (0.007 + r() * 0.003) * (i % 2 ? -1 : 1), path: SHORE });
    return a;
  })();
  const carS = (c, tau) => { const L = c.path.len; return ((c.s0 + c.v * tau) % L + L) % L; };

  // the village at the foot of the right range
  const VILLAGE = (() => {
    const r = rng(31), a = [];
    for (const row of [0, 1]) {
      let x = 0.89 + row * 0.012;
      while (x < (row ? 1.04 : 1.075)) {
        const w = 0.0055 + r() * 0.0065, h = 0.005 + r() * 0.006;
        const base = YW + 0.0035 + (x > 1.0 ? (x - 1.0) * 0.06 : 0) - row * 0.0075 - r() * 0.0015;
        const wins = [];
        const nwin = 1 + (r() * 2.6 | 0);
        for (let k = 0; k < nwin; k++) wins.push({ dx: (k + 0.5) / nwin * w - 0.0007, dy: h * (0.35 + r() * 0.3), on: 0.53 + r() * 0.2, warm: r() });
        a.push({ x, w, h, base, roof: 0.0025 + r() * 0.0028, wall: r(), roofc: r(), tree: r() < 0.3, wins, row });
        x += w + 0.0015 + r() * 0.007;
      }
    }
    return a.sort((p, q) => q.row - p.row);
  })();
  const CHURCH = { x: 0.958, w: 0.0075, h: 0.016, spire: 0.026, base: YW + 0.004 };

  // ------------------------------------------------------------ sky details
  const POLE = [1.9, -0.75];
  const OMEGA = TAU / 86164; // sidereal rotation, rad per second
  const STARS = (() => {
    const r = rng(3), a = [];
    for (let i = 0; i < 520; i++) {
      const b = Math.pow(r(), 2.4);
      a.push({ x: r() * 1.9 - 0.2, y: r() * 0.72 - 0.08, s: 0.45 + b * 1.9, b: 0.3 + 0.7 * Math.sqrt(b), tw: r() * TAU, warm: r() < 0.18 });
    }
    // the Milky Way: a dense band of faint stars
    for (let i = 0; i < 900; i++) {
      const u = r(), g = (r() + r() + r() - 1.5) * 0.09;
      const bx = lerp(-0.1, 1.6, u), by = lerp(0.72, -0.12, u);
      a.push({ x: bx + g * 0.55, y: by + g, s: 0.45 + r() * 0.6, b: 0.25 + r() * 0.4, tw: r() * TAU, mw: true });
    }
    return a;
  })();
  const MW = (() => { const r = rng(5), a = []; for (let i = 0; i < 26; i++) { const u = i / 25; a.push({ x: lerp(-0.1, 1.6, u) + (r() - 0.5) * 0.05, y: lerp(0.72, -0.12, u) + (r() - 0.5) * 0.05, rx: 0.08 + r() * 0.1, a: 0.5 + r() * 0.5 }); } return a; })();
  function rot(x, y, th) {
    const dx = x - POLE[0], dy = y - POLE[1], c = Math.cos(th), s = Math.sin(th);
    return [POLE[0] + dx * c - dy * s, POLE[1] + dx * s + dy * c];
  }

  const CLOUDS = (() => {
    const r = rng(11), a = [];
    for (let i = 0; i < 12; i++) {
      const puffs = [], n = 3 + (r() * 4 | 0), len = 0.16 + r() * 0.3;
      for (let j = 0; j < n; j++) {
        const u = n > 1 ? j / (n - 1) : 0.5;
        puffs.push({ dx: (u - 0.5) * len * 0.8 + (r() - 0.5) * 0.04, dy: (r() - 0.5) * 0.008 - Math.sin(u * Math.PI) * 0.004, rx: 0.06 + r() * 0.1, ry: 0.006 + r() * 0.008 });
      }
      a.push({ x: r() * 2.0 - 0.3, y: 0.04 + Math.pow(r(), 1.2) * 0.27, sp: 0.55 + r() * 0.5, puffs, a: 0.45 + r() * 0.45 });
    }
    return a;
  })();

  // ------------------------------------------------------------ drawing
  function poly(ctx, pts, close) {
    ctx.beginPath();
    ctx.moveTo(pts[0][0], pts[0][1]);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0], pts[i][1]);
    if (close) ctx.closePath();
  }
  function blob(ctx, x, y, rx, ry, col, al) {
    if (al <= 0.003) return;
    ctx.save();
    ctx.translate(x, y);
    ctx.scale(1, ry / rx);
    const g = ctx.createRadialGradient(0, 0, 0, 0, 0, rx);
    g.addColorStop(0, rgba(col, al));
    g.addColorStop(0.5, rgba(col, al * 0.62));
    g.addColorStop(1, rgba(col, 0));
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(0, 0, rx, 0, TAU); ctx.fill();
    ctx.restore();
  }

  function puff(ctx, x, y, rx, ry, col, al) {
    if (al <= 0.003) return;
    ctx.save();
    ctx.translate(x, y);
    ctx.scale(1, ry / rx);
    const g = ctx.createRadialGradient(0, 0, 0, 0, 0, rx);
    g.addColorStop(0, rgba(col, al)); g.addColorStop(0.62, rgba(col, al * 0.94)); g.addColorStop(0.86, rgba(col, al * 0.45)); g.addColorStop(1, rgba(col, 0));
    ctx.fillStyle = g;
    ctx.beginPath(); ctx.arc(0, 0, rx, 0, TAU); ctx.fill();
    ctx.restore();
  }
  function drawSky(ctx, k, T, o, px) {
    const g = ctx.createLinearGradient(0, -0.1, 0, YW);
    g.addColorStop(0, rgba(k.zen));
    g.addColorStop(0.42, rgba(k.mid));
    g.addColorStop(0.88, rgba(k.hor));
    g.addColorStop(1, rgba(mixc(k.hor, k.glowCol, 0.25 * k.glowO)));
    ctx.fillStyle = g;
    ctx.fillRect(-0.2, -0.2, 1.9, YW + 0.2);
    // stars, rotating about the pole; a blend draws each as the arc it travels
    if (k.stars > 0.002) {
      const tau = o.tau;
      const span = o.span || 0;
      if (k.milky > 0) {
        for (const m of MW) {
          const [x, y] = rot(m.x, m.y, OMEGA * tau);
          blob(ctx, x, y, m.rx, m.rx * 0.55, '#A3ADDC', 0.11 * k.milky * m.a);
        }
      }
      for (const s of STARS) {
        if (o.small && s.s < 1.2) continue;
        const a0 = k.stars * s.b * (s.mw ? k.milky : 1);
        if (a0 < 0.02) continue;
        const r = Math.max(0.6, s.s) * px;
        if (span > 0) {
          const [x0, y0] = rot(s.x, s.y, OMEGA * tau);
          const [x1, y1] = rot(s.x, s.y, OMEGA * (tau + span));
          ctx.strokeStyle = s.warm ? `rgba(255,226,196,${(a0 * 0.75).toFixed(3)})` : `rgba(236,240,255,${(a0 * 0.75).toFixed(3)})`;
          ctx.lineWidth = r * 1.6; ctx.lineCap = 'round';
          ctx.beginPath(); ctx.moveTo(x0, y0); ctx.lineTo(x1, y1); ctx.stroke();
        } else {
          const [x, y] = rot(s.x, s.y, OMEGA * tau);
          const tw = 0.75 + 0.25 * Math.sin(s.tw + (o.twinkle || 0) * 2.3);
          ctx.fillStyle = s.warm ? `rgba(255,228,200,${(a0 * tw).toFixed(3)})` : `rgba(240,244,255,${(a0 * tw).toFixed(3)})`;
          ctx.beginPath(); ctx.arc(x, y, r, 0, TAU); ctx.fill();
        }
      }
    }
    // the evening crescent, low in the west after sunset
    const mT = clamp((T - 0.5) / 0.4);
    if (mT > 0 && mT < 1) {
      const mx = lerp(0.36, 0.3, mT), my = lerp(0.2, 0.36, mT), mr = 0.0105;
      const ma = clamp((T - 0.5) / 0.08) * (1 - clamp((T - 0.86) / 0.05));
      if (ma > 0.01) {
        blob(ctx, mx, my, mr * 7, mr * 7, '#B8C2F0', 0.08 * ma);
        ctx.save();
        ctx.beginPath(); ctx.arc(mx, my, mr, 0, TAU); ctx.clip();
        ctx.fillStyle = `rgba(255,246,222,${(0.95 * ma).toFixed(3)})`;
        ctx.fillRect(mx - mr, my - mr, mr * 2, mr * 2);
        ctx.fillStyle = rgba(mixc(k.zen, k.mid, 0.5));
        ctx.beginPath(); ctx.arc(mx + mr * 0.42, my - mr * 0.16, mr * 0.98, 0, TAU); ctx.fill();
        ctx.restore();
      }
    }
    // sun and glow
    const sx = k.sunX, sy = k.sunY;
    if (k.glowO > 0.01) {
      let hg = ctx.createRadialGradient(sx, Math.min(sy, YW - 0.12), 0, sx, Math.min(sy, YW - 0.12), 0.75);
      hg.addColorStop(0, rgba(k.glowCol, 0.42 * k.glowO)); hg.addColorStop(0.25, rgba(k.glowCol, 0.16 * k.glowO)); hg.addColorStop(1, rgba(k.glowCol, 0));
      ctx.fillStyle = hg; ctx.fillRect(-0.2, -0.2, 1.9, YW + 0.2);
    }
    if (k.sunO > 0) {
      // crepuscular rays
      if (!o.small) {
        const ra = 0.022 * clamp(1 - Math.abs(T - 0.3) / 0.12);
        if (ra > 0.003) {
          ctx.save();
          ctx.globalCompositeOperation = 'lighter';
          for (let i = 0; i < 11; i++) {
            const an = -2.6 + i * 0.47 + 0.05 * Math.sin(i * 3.1);
            const g2 = ctx.createRadialGradient(sx, sy, 0, sx, sy, 0.9);
            g2.addColorStop(0, rgba(k.glowCol, ra)); g2.addColorStop(1, rgba(k.glowCol, 0));
            ctx.fillStyle = g2;
            ctx.beginPath(); ctx.moveTo(sx, sy);
            ctx.lineTo(sx + Math.cos(an - 0.035) * 1.2, sy + Math.sin(an - 0.035) * 1.2);
            ctx.lineTo(sx + Math.cos(an + 0.035) * 1.2, sy + Math.sin(an + 0.035) * 1.2);
            ctx.closePath(); ctx.fill();
          }
          ctx.restore();
        }
      }
      const R = 0.021;
      const sg = ctx.createRadialGradient(sx, sy, 0, sx, sy, R * 3.2);
      sg.addColorStop(0, rgba(mixc(k.sunCol, '#FFFFFF', 0.6), k.sunO));
      sg.addColorStop(0.3, rgba(k.sunCol, k.sunO));
      sg.addColorStop(0.33, rgba(k.glowCol, 0.55 * k.sunO));
      sg.addColorStop(1, rgba(k.glowCol, 0));
      ctx.fillStyle = sg; ctx.beginPath(); ctx.arc(sx, sy, R * 3.2, 0, TAU); ctx.fill();
    }
  }

  function drawClouds(ctx, k, T, o) {
    const al = k.cloudO;
    if (al <= 0.01) return;
    const under = Math.exp(-Math.pow((T - 0.4) / 0.12, 2));
    const drift = o.cloudX != null ? o.cloudX : T * 1.25;
    for (const c of CLOUDS) {
      let cx = c.x + c.sp * drift;
      cx = ((cx + 0.5) % 2.3 + 2.3) % 2.3 - 0.5;
      const cy = c.y;
      for (const p of c.puffs) puff(ctx, cx + p.dx, cy + p.dy + p.ry * 0.25, p.rx * 1.04, p.ry * 1.06, k.cloudS, c.a * al);
      for (const p of c.puffs) puff(ctx, cx + p.dx - p.rx * 0.1, cy + p.dy - p.ry * 0.3, p.rx * 0.8, p.ry * 0.76, k.cloudL, c.a * al * 0.92);
      if (under > 0.02) for (const p of c.puffs) puff(ctx, cx + p.dx, cy + p.dy + p.ry * 0.5, p.rx * 0.72, p.ry * 0.4, k.glowCol, c.a * al * under * 0.75);
    }
  }

  // Shaded mountain: the base fill, then each peak's flank turned away from the sun as one face, bounded by a jagged spine.
  const SNOWB = x => { const r = farRidge(x); const d = clamp(0.022 + 0.36 * (0.43 - r), 0.012, 0.12) + 0.01 * (SNOWLINE(x) - 0.385) / 0.03; const c = Math.max(0, fbm(nz, x * 23, 2)); return r + d + 0.05 * c * c + 0.005 * fbm(nz2, x * 140, 2); };
  const FACES = PEAKS.map(([px], i) => {
    let sx = px, best = farRidge(px);
    for (let x = px - 0.03; x <= px + 0.03; x += 0.0005) { const y = farRidge(x); if (y < best) { best = y; sx = x; } }
    let xr = sx;
    const lim = i + 1 < PEAKS.length ? (PEAKS[i + 1][0] + sx) / 2 + 0.04 : 1.6;
    while (xr < lim && farRidge(xr + 0.002) >= farRidge(xr) - 0.0004) xr += 0.002;
    const ridge = [];
    for (let x = sx; x <= xr; x += 0.0015) ridge.push([x, farRidge(x)]);
    ridge.push([xr, farRidge(xr)]);
    const spine = [];
    const r = rng(100 + i);
    for (let k = 12; k >= 0; k--) {
      const u = k / 12, y = lerp(best, YW, u);
      spine.push([sx + u * 0.035 + (k && k < 12 ? (r() - 0.5) * 0.012 * u : 0), y]);
    }
    // below the saddle the face carries on along its flank, in front of the next peak
    const slope = Math.max(0.3, (farRidge(xr) - best) / Math.max(0.01, xr - sx));
    return ridge.concat([[xr + (YW + 0.01 - farRidge(xr)) / slope, YW + 0.01]], spine);
  });
  function drawFar(ctx, k, T, o, px) {
    const x0 = Math.max(-0.02, o.vx0 - 0.01), x1 = Math.min(1.54, o.vx1 + 0.01);
    const step = Math.max(px * 2.5, 0.0008);
    const ridge = [], snow = [];
    for (let x = x0; x <= x1 + step; x += step) { ridge.push([x, farRidge(x)]); snow.push([x, SNOWB(x)]); }
    const body = ridge.concat([[x1 + step, YW + 0.01], [x0, YW + 0.01]]);
    // rock
    const g = ctx.createLinearGradient(0, 0.2, 0, YW);
    g.addColorStop(0, rgba(k.rockL)); g.addColorStop(1, rgba(mixc(k.rockL, k.hor, 0.35)));
    ctx.fillStyle = g; poly(ctx, body, true); ctx.fill();
    ctx.save();
    poly(ctx, body, true); ctx.clip();
    for (const f of FACES) {
      const top = f[0][1];
      const sg = ctx.createLinearGradient(0, top, 0, top + 0.26);
      sg.addColorStop(0, rgba(k.rockS)); sg.addColorStop(1, rgba(mixc(k.rockS, k.rockL, 0.85), 0));
      ctx.fillStyle = sg; poly(ctx, f, true); ctx.fill();
    }
    // snow: from the ridge down to a ragged edge
    ctx.beginPath();
    ctx.moveTo(ridge[0][0], ridge[0][1]);
    for (const [x, y] of ridge) ctx.lineTo(x, y - px);
    for (let i = snow.length - 1; i >= 0; i--) ctx.lineTo(snow[i][0], snow[i][1]);
    ctx.closePath();
    ctx.clip();
    ctx.fillStyle = rgba(k.snowL); ctx.fillRect(x0, 0.1, x1 - x0 + step, 0.5);
    ctx.fillStyle = rgba(k.snowS);
    for (const f of FACES) { poly(ctx, f, true); ctx.fill(); }
    ctx.restore();
    // haze toward the base
    const hz = ctx.createLinearGradient(0, 0.36, 0, YW);
    hz.addColorStop(0, rgba(k.hor, 0)); hz.addColorStop(1, rgba(k.hor, 0.55 - 0.25 * k.night));
    ctx.fillStyle = hz; poly(ctx, body, true); ctx.fill();
  }

  const FOOT = pineRidge(x => 0.598 + 0.018 * fbm(nz3, x * 7 + 11, 3) - 0.028 * Math.exp(-Math.pow((x - 0.78) / 0.12, 2)), 0.42, 1.16, 13, 320, 0.007);
  function drawFoot(ctx, k, T, o, px) {
    const pts = FOOT.concat([[1.16, YW + 0.01], [0.42, YW + 0.01]]);
    const g = ctx.createLinearGradient(0, 0.55, 0, YW);
    g.addColorStop(0, rgba(mixc(k.midL, k.hor, 0.5))); g.addColorStop(1, rgba(mixc(k.midL, k.hor, 0.62)));
    ctx.fillStyle = g; poly(ctx, pts, true); ctx.fill();
  }
  function drawMids(ctx, k, T, o, px) {
    // left
    const L = LEFT.concat([[0.64, YW + 0.03], [-0.02, YW + 0.03]]);
    let g = ctx.createLinearGradient(0, 0.2, 0, YW);
    g.addColorStop(0, rgba(mixc(k.midL, k.hor, 0.1))); g.addColorStop(1, rgba(mixc(k.midL, k.hor, 0.22)));
    ctx.fillStyle = g; poly(ctx, L, true); ctx.fill();
    // right, with the road
    const R = RIGHT.concat([[1.54, 0.69], [0.9, YW + 0.03]]);
    g = ctx.createLinearGradient(0, 0.3, 0, YW + 0.03);
    g.addColorStop(0, rgba(mixc(k.midR, k.hor, 0.08))); g.addColorStop(1, rgba(mixc(k.midR, k.hor, 0.2)));
    ctx.fillStyle = g; poly(ctx, R, true); ctx.fill();
    // the road, a pale line by day
    const dayRoad = 1 - clamp((T - 0.36) / 0.2);
    if (dayRoad > 0.02) {
      ctx.strokeStyle = rgba(mixc('#C9C2B4', k.midR, 0.35), 0.7 * dayRoad);
      ctx.lineWidth = Math.max(px * 0.9, 0.00072); ctx.lineJoin = 'round';
      poly(ctx, ROAD.pts); ctx.stroke();
      poly(ctx, SHORE.pts); ctx.stroke();
    }
  }

  function drawVillage(ctx, k, T, o, px) {
    const wallDay = mixc('#ECE5D8', k.hor, 0.18);
    const dusk = clamp(T / 0.7);
    for (const hs of VILLAGE) {
      const tint = hs.wall < 0.5 ? '#F1ECE2' : hs.wall < 0.8 ? '#D9C7A8' : '#C9CDD2';
      const wall = mixc(mixc(wallDay, tint, 0.6), mulc(k.midR, 0.85), dusk);
      ctx.fillStyle = rgba(hs.row ? mulc(wall, 0.92) : wall);
      ctx.fillRect(hs.x, hs.base - hs.h, hs.w, hs.h);
      const roof = hs.roofc < 0.5 ? '#6E3F33' : hs.roofc < 0.8 ? '#4C4A52' : '#7A5A3A';
      ctx.fillStyle = rgba(mixc(roof, mulc(k.midR, 0.6), clamp(T / 0.6)));
      ctx.beginPath(); ctx.moveTo(hs.x - 0.0007, hs.base - hs.h); ctx.lineTo(hs.x + hs.w / 2, hs.base - hs.h - hs.roof); ctx.lineTo(hs.x + hs.w + 0.0007, hs.base - hs.h); ctx.closePath(); ctx.fill();
      if (hs.tree) {
        ctx.fillStyle = rgba(mixc(mulc(k.midR, 0.75), k.hor, 0.1));
        const tx = hs.x + hs.w + 0.0022, th = 0.011;
        ctx.beginPath(); ctx.moveTo(tx - 0.0026, hs.base); ctx.lineTo(tx, hs.base - th); ctx.lineTo(tx + 0.0026, hs.base); ctx.closePath(); ctx.fill();
      }
    }
    const c = CHURCH;
    ctx.fillStyle = rgba(mixc(wallDay, mulc(k.midR, 0.85), clamp(T / 0.7)));
    ctx.fillRect(c.x, c.base - c.h, c.w, c.h);
    ctx.beginPath(); ctx.moveTo(c.x, c.base - c.h); ctx.lineTo(c.x + c.w / 2, c.base - c.h - c.spire); ctx.lineTo(c.x + c.w, c.base - c.h); ctx.closePath();
    ctx.fillStyle = rgba(mixc('#5D5A62', mulc(k.midR, 0.7), clamp(T / 0.6))); ctx.fill();
  }

  // every light source, as points in world units with a colour and a strength; used for the land and its reflection
  function lights(k, T, o) {
    const out = [];
    if (k.lightsOn <= 0) return out;
    for (const hs of VILLAGE) for (const w of hs.wins) {
      const a = clamp((T - w.on) / 0.05);
      if (a > 0) out.push({ x: hs.x + w.dx, y: hs.base - w.dy, c: w.warm < 0.75 ? '#FFD08A' : '#FFE9C4', a, r: 0.0011, win: true });
    }
    for (const l of LAMPS) {
      const a = clamp((T - l.on) / 0.04);
      if (a > 0) { const [x, y] = at(SHORE, l.s); out.push({ x, y: y - 0.004, c: '#FFB45A', a, r: 0.0012, lamp: true }); }
    }
    return out;
  }

  function drawLights(ctx, k, T, o, px, L) {
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    for (const p of L) {
      if (p.win) { ctx.fillStyle = rgba(p.c, p.a); ctx.fillRect(p.x, p.y, Math.max(px * 1.2, 0.0016), Math.max(px * 1.4, 0.002)); }
      else {
        blob(ctx, p.x, p.y, 0.008, 0.008, p.c, 0.28 * p.a);
        ctx.fillStyle = rgba(mixc(p.c, '#FFFFFF', 0.5), p.a);
        ctx.beginPath(); ctx.arc(p.x, p.y, Math.max(px * 0.9, p.r), 0, TAU); ctx.fill();
      }
    }
    ctx.restore();
  }

  // Car lights. Each photo is an exposure of `exp` seconds; a blend of n photos 5 s apart concatenates n exposures.
  function exposureAt(T) { return T < 0.5 ? 0 : T < 0.8 ? lerp(0.5, 4, (T - 0.5) / 0.3) : 4; }
  function drawCars(ctx, k, T, o, px, reflect) {
    const on = k.lightsOn;
    if (on <= 0) return;
    const n = o.blend || 1, exp = Math.max(0.05, exposureAt(T));
    const tau0 = o.tau;
    ctx.save();
    ctx.globalCompositeOperation = 'lighter';
    ctx.lineCap = 'round'; ctx.lineJoin = 'round';
    for (let ci = 0; ci < CARS.length; ci++) {
      const c = CARS[ci];
      const head = c.v < 0 === (c.path === ROAD);
      const col = head ? [255, 244, 226] : [255, 70, 52];
      const I = head ? 9 : 3.4;
      const al = clamp(I / n) * on * (reflect ? 0.16 : 1);
      const w = (head ? 0.0024 : 0.0019) * (c.path === SHORE ? 1.25 : 1);
      for (let j = 0; j < n; j++) {
        const ta = tau0 - (n - 1 - j) * INTERVAL, tb = ta + exp;
        const sa = carS(c, ta), sb = carS(c, tb);
        if (Math.abs(sb - sa) > c.path.len * 0.5) continue; // wrapped around
        const lo = Math.min(sa, sb), hi = Math.max(sa, sb);
        ctx.beginPath();
        const steps = Math.max(2, Math.ceil((hi - lo) / 0.004));
        for (let q = 0; q <= steps; q++) {
          let [x, y] = at(c.path, lerp(lo, hi, q / steps));
          if (reflect) y = 2 * YW - y;
          if (q) ctx.lineTo(x, y); else ctx.moveTo(x, y);
        }
        ctx.strokeStyle = `rgba(${col[0]},${col[1]},${col[2]},${(al * 0.35).toFixed(3)})`;
        ctx.lineWidth = w * (reflect ? 5 : 3.2); ctx.stroke();
        ctx.strokeStyle = `rgba(${col[0]},${col[1]},${col[2]},${al.toFixed(3)})`;
        ctx.lineWidth = w; ctx.stroke();
      }
    }
    ctx.restore();
  }

  function drawFore(ctx, k, T, o, px) {
    // the near shore and a stand of pines, bottom left; a rock, bottom right
    const col = k.fore;
    ctx.fillStyle = rgba(col);
    ctx.beginPath();
    ctx.moveTo(-0.02, 0.86);
    for (let x = -0.02; x <= 0.5; x += 0.01) ctx.lineTo(x, 0.885 + 0.07 * Math.pow(x / 0.5, 2) + 0.008 * fbm(nz, x * 40, 2));
    ctx.lineTo(0.52, 1.02); ctx.lineTo(-0.02, 1.02); ctx.closePath(); ctx.fill();
    const r = rng(77);
    const trees = [[0.035, 0.36, 0.07], [0.11, 0.47, 0.058], [0.175, 0.56, 0.05], [0.235, 0.66, 0.04], [-0.01, 0.42, 0.06]];
    for (const [tx, top, tw] of trees) {
      const base = 0.92;
      ctx.beginPath();
      ctx.moveTo(tx, top);
      const tiers = 13;
      for (let i = 1; i <= tiers; i++) {
        const u = i / tiers, y = lerp(top, base - 0.03, u), w = tw * (0.15 + 0.85 * u) * (0.75 + 0.25 * r());
        ctx.lineTo(tx + w, y + 0.012); ctx.lineTo(tx + w * 0.35, y + 0.006);
      }
      ctx.lineTo(tx + 0.004, base); ctx.lineTo(tx - 0.004, base);
      for (let i = tiers; i >= 1; i--) {
        const u = i / tiers, y = lerp(top, base - 0.03, u), w = tw * (0.15 + 0.85 * u) * (0.75 + 0.25 * r());
        ctx.lineTo(tx - w * 0.35, y + 0.006); ctx.lineTo(tx - w, y + 0.012);
      }
      ctx.closePath(); ctx.fill();
    }
    ctx.beginPath();
    ctx.moveTo(1.24, 1.02); ctx.quadraticCurveTo(1.3, 0.905, 1.4, 0.9); ctx.quadraticCurveTo(1.47, 0.89, 1.54, 0.875); ctx.lineTo(1.54, 1.02); ctx.closePath(); ctx.fill();
  }

  // ------------------------------------------------------------ compose
  const cache = {};
  function buf(name, w, h) {
    let c = cache[name];
    if (!c || c.width !== w || c.height !== h) { c = cache[name] = offscreen(w, h); }
    return c;
  }

  // view: {x, y, w, h} in world units, with the canvas's aspect. o: tau (real s), blend (photos), small, cloudX, ripple (s), twinkle
  function draw(ctx, cw, ch, T, view, o = {}) {
    view = view || { x: 0, y: 0, w: 1.5, h: 1 };
    o = Object.assign({ tau: T * SEQ }, o);
    o.span = o.blend > 1 ? (o.blend - 1) * INTERVAL + exposureAt(T) : (T > 0.5 ? exposureAt(T) : 0);
    if (o.blend > 1) o.tau -= (o.blend - 1) * INTERVAL;
    const k = light(T);
    o.vx0 = view.x; o.vx1 = view.x + view.w;
    const sc = cw / view.w, px = 1 / sc;
    const wl = (YW - view.y) * sc;                      // waterline in canvas px
    const topW = Math.min(view.y, 2 * YW - (view.y + view.h)); // the highest world y any reflected row needs
    const uh = Math.max(2, Math.ceil((YW - topW) * sc) + 2);
    const U = buf(o.small ? 'Us' : 'U', cw, uh);
    const u = U.getContext('2d');
    u.setTransform(sc, 0, 0, sc, -view.x * sc, -topW * sc);
    u.clearRect(view.x, topW, view.w, YW - topW + 0.01);
    drawSky(u, k, T, o, px);
    drawClouds(u, k, T, o);
    drawFar(u, k, T, o, px);
    drawFoot(u, k, T, o, px);
    drawMids(u, k, T, o, px);
    drawVillage(u, k, T, o, px);
    const Ls = lights(k, T, o);
    drawLights(u, k, T, o, px, Ls);
    if (o.cars !== false) drawCars(u, k, T, o, px, false);
    u.setTransform(1, 0, 0, 1, 0, 0);

    ctx.save();
    // above the water
    const uy0 = (view.y - topW) * sc;
    if (wl > 0) ctx.drawImage(U, 0, uy0, cw, Math.min(wl, ch), 0, 0, cw, Math.min(wl, ch));
    // the reflection, at half resolution: mirrored rows, broken by ripples that a blend calms
    if (wl < ch) {
      const rh = Math.ceil((ch - Math.max(0, wl)) / 2), rw = Math.ceil(cw / 2);
      const Rb = buf(o.small ? 'Rs' : 'R', rw, rh);
      const r = Rb.getContext('2d');
      r.setTransform(1, 0, 0, 1, 0, 0);
      r.clearRect(0, 0, rw, rh);
      const calm = 1 / Math.sqrt(o.blend || 1);
      const rip = (o.ripple != null ? o.ripple : o.tau) * 1.7;
      const y0 = Math.max(0, wl);
      const pxs = sc / 1280;                                  // canvas px per px of a 1920-wide full frame
      for (let i = 0; i < rh; i++) {
        const yd = y0 + i * 2 + 1;                            // canvas row
        const depth = (yd - wl) / sc;                         // world units below the waterline
        const sy = (YW - depth - topW) * sc;                  // the mirrored row in U
        if (sy < 0) break;
        const amp = (0.5 + depth * 22) * pxs * calm;
        const ph = 52 / (depth + 0.015);
        const dx = amp * (0.62 * Math.sin(ph + rip) + 0.38 * Math.sin(ph * 2.3 - rip * 1.7 + 1.3));
        r.drawImage(U, 0, sy, cw, 2, dx / 2 - 1, i, rw + 2, 1);
      }
      ctx.drawImage(Rb, 0, 0, rw, rh, 0, y0, cw, rh * 2);
      ctx.setTransform(sc, 0, 0, sc, -view.x * sc, -view.y * sc);
      // water body: darken and tint toward the lake colour, deeper toward the viewer
      const wg = ctx.createLinearGradient(0, YW, 0, 1.0);
      wg.addColorStop(0, rgba(k.water, 0.28));
      wg.addColorStop(1, rgba(mulc(k.water, 0.7), 0.6));
      ctx.fillStyle = wg; ctx.fillRect(view.x - 0.01, YW, view.w + 0.02, 1.05 - YW);
      ctx.fillStyle = rgba(mixc(k.hor, '#FFFFFF', 0.35), 0.4 - 0.25 * k.night);
      ctx.fillRect(view.x - 0.01, YW - px * 0.3, view.w + 0.02, Math.max(px * 1.2, 0.0011));
      // light reflections: vertical streaks broken by the ripples; a blend smooths them
      ctx.globalCompositeOperation = 'lighter';
      for (let li = 0; li < Ls.length; li++) {
        const p = Ls[li];
        const len = p.lamp ? 0.07 : 0.03, segs = 16, sh = len / segs;
        for (let q = 0; q < segs; q++) {
          const u = q / segs, yy = 2 * YW - p.y + 0.0015 + u * len;
          const wob = Math.sin(li * 12.9898 + q * 1.9 + rip * 0.8) * 0.0018 * calm * (0.4 + u);
          const brk = calm > 0.5 ? 0.55 + 0.45 * Math.sin(li * 7.1 + q * 2.6 + rip) : 0.8;
          ctx.fillStyle = rgba(p.c, p.a * (p.lamp ? 0.42 : 0.3) * (1 - u) * brk);
          ctx.fillRect(p.x + wob - 0.001 - u * 0.0008, yy, 0.002 + u * 0.0016, sh * 1.05);
        }
      }
      ctx.globalCompositeOperation = 'source-over';
      if (o.cars !== false) drawCars(ctx, k, T, o, px, true);
      ctx.setTransform(1, 0, 0, 1, 0, 0);
    }
    // foreground
    ctx.setTransform(sc, 0, 0, sc, -view.x * sc, -view.y * sc);
    drawFore(ctx, k, T, o, px);
    ctx.restore();
  }

  // only the car lights of one photo (or a blend), on a transparent canvas
  function cars(ctx, cw, ch, T, view, o = {}) {
    o = Object.assign({ tau: T * SEQ }, o);
    if (o.blend > 1) o.tau -= (o.blend - 1) * INTERVAL;
    const k = light(T);
    const sc = cw / view.w, px = 1 / sc;
    ctx.save();
    ctx.setTransform(sc, 0, 0, sc, -view.x * sc, -view.y * sc);
    drawCars(ctx, k, T, o, px, false);
    drawCars(ctx, k, T, o, px, true);
    ctx.restore();
  }

  // an average colour per time, for the five frames and the time-slice strip
  function tone(T) { const k = light(T); return mixc(mixc(k.mid, k.hor, 0.5), k.water, 0.25); }

  return { draw, cars, light, tone, YW, SEQ, ROAD, at, exposureAt };
})();
