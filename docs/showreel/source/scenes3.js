// Scenes 8 to 12: hold, boomerang and music; the export; the premiere; privacy; the end card.
'use strict';

const NIGHT = '#0A0E1C';
// the soundtrack's peaks, 100 per second (wave.js, written by music.py); a stand-in until it exists
const PEAKS = (() => {
  if (window.WAVE && WAVE.peaks) return WAVE.peaks;
  const a = [], r = rng(8);
  for (let i = 0; i < DUR * 100; i++) { const t = i / 100, beat = Math.exp(-((t - 1) % 0.5) * 9); a.push(clamp(0.25 + 0.5 * beat * (0.6 + 0.4 * r()) + 0.15 * r())); }
  return a;
})();

// a filmstrip of the evening, forward: 24 photos side by side at 120 x 80
const STRIP = (() => {
  const c = offscreen(24 * 120, 80), x = c.getContext('2d');
  for (let i = 0; i < 24; i++) x.drawImage(texAt(i / 23), i * 120 + 2, 2, 116, 76);
  return c;
})();

// ================================================================ TIMING  hold, boomerang, music  [35.0, 41.45)
const TIMING = (() => {
  const s = scene(35.0, 41.45, 70);
  s.root.style.background = `radial-gradient(ellipse 80% 70% at 50% 55%,#141A33 0%,${NIGHT} 70%)`;
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const X0 = 160, X1 = 1760, TY = 536, TH = 112;
  const head = el('div', `position:absolute;left:120px;top:120px;color:${DINK}`, s.root);
  const kick = el('div', `position:relative;height:32px;color:${DSUB};margin-bottom:10px`, head); kick.className = 'kick';
  const kw1 = el('span', 'position:absolute;left:0;top:0', kick); const kw = words(kw1, 'Timing');
  const kw2 = el('span', 'position:absolute;left:0;top:0', kick); const kwm = words(kw2, 'Music');
  // the blended night frame of the last scene, as it was when the scene ended
  const NB = offscreen(W, H);
  WORLD.draw(NB.getContext('2d'), W, H, 0.93, V169, { blend: 16, tau: 0.93 * 7200 + 75 + (35.0 - 31.1) * 9, ripple: 0 });
  const hl = el('div', '', head); hl.className = 'head';
  const H1 = words(hl, 'Hold.'); hl.appendChild(document.createTextNode(' '));
  const H2 = words(hl, 'Boomerang.'); hl.appendChild(document.createTextNode(' '));
  const H3 = words(hl, 'Music.');
  const lenBox = el('div', `position:absolute;left:${X1 - 400}px;width:400px;top:${TY - 150}px;text-align:right;color:${DINK}`, s.root);
  el('div', `font:400 20px/26px var(--txt);color:${DSUB}`, lenBox, 'Video length');
  const lenV = el('div', 'font:600 44px/52px var(--disp);letter-spacing:-0.02em;font-variant-numeric:tabular-nums', lenBox, '48.0 s');
  const L = (txt) => el('div', `position:absolute;left:0;top:0;font:600 21px/28px var(--txt);color:${DSUB};white-space:nowrap`, s.root, txt);
  const lHoldA = L('Hold at start · 3 s'), lHoldB = L('Hold at end · 3 s'), lFwd = L('1,440 photos'), lBack = L('Boomerang · back'), lMus = L('Music · fades in and out');
  const segW = el('div', `position:absolute;left:120px;top:292px`, s.root);
  UI.theme(segW, 1);
  const seg = UI.segmented(segW, ['Forward', 'Boomerang'], 340, 40);
  seg.items.forEach(i => { i.style.fontSize = '18px'; });
  HEAVY_MORE.push([34.95, 35.9, 8], [35.8, 36.3, 6], [36.95, 37.6, 6], [38.9, 39.4, 6]);

  function lay(t) {
    const hold = E.move(prog(t, 35.8, 36.3));
    const boom = E.move(prog(t, 37.0, 37.55)) * (1 - E.move(prog(t, 39.0, 39.5)));
    const total = 48 + 6 * hold + 48 * boom;
    const k = (X1 - X0) / total;
    const hA = 3 * hold * k, fw = 48 * k, bk = 48 * boom * k;
    const x = X0;
    return { hold, boom, total, k, hs: { x, w: hA }, fw: { x: x + hA, w: fw }, bk: { x: x + hA + fw, w: bk }, he: { x: x + hA + fw + bk, w: hA } };
  }
  s.update = t => {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const g = lay(t);
    // the strip arrives: the blended night photo shrinks into the last frame, the others slide in from the left
    const arr = E.enter(prog(t, 35.0, 35.75));
    const arrive = prog(t, 35.0, 35.7);
    ctx.save();
    ctx.beginPath(); ctx.roundRect(X0 - 2, TY - 2, X1 - X0 + 4, TH + 4, 6); ctx.clip();
    // hold blocks: the first frame stretched, hatched
    const holdBlock = (b, T, al) => {
      if (b.w < 1) return;
      ctx.globalAlpha = al;
      ctx.drawImage(texAt(T), 20, 20, 260, 160, b.x + 2, TY, Math.max(0, b.w - 4), TH);
      ctx.fillStyle = 'rgba(10,14,28,0.45)'; ctx.fillRect(b.x + 2, TY, Math.max(0, b.w - 4), TH);
      ctx.strokeStyle = 'rgba(255,255,255,0.22)'; ctx.lineWidth = 2;
      ctx.save(); ctx.beginPath(); ctx.rect(b.x + 2, TY, b.w - 4, TH); ctx.clip();
      for (let x = b.x - TH; x < b.x + b.w; x += 14) { ctx.beginPath(); ctx.moveTo(x, TY + TH); ctx.lineTo(x + TH, TY); ctx.stroke(); }
      ctx.restore();
      ctx.globalAlpha = 1;
    };
    holdBlock(g.hs, 0, g.hold);
    // forward photos
    const n = 24;
    for (let i = 0; i < n; i++) {
      const u = clamp(arrive * 1.6 - (n - 1 - i) / n * 0.6);
      const e = E.enter(u);
      const cw = g.fw.w / n;
      const x = g.fw.x + i * cw - (1 - e) * 900;
      ctx.globalAlpha = clamp(u * 3);
      ctx.drawImage(STRIP, i * 120, 0, 120, 80, x, TY, cw, TH);
    }
    ctx.globalAlpha = 1;
    // boomerang: the same photos, back
    if (g.bk.w > 1) {
      const cw = g.bk.w / n;
      for (let i = 0; i < n; i++) ctx.drawImage(STRIP, (n - 1 - i) * 120, 0, 120, 80, g.bk.x + i * cw, TY, cw, TH);
    }
    holdBlock(g.he, 0, g.hold);
    ctx.restore();
    // the night photo of the last scene, landing in the last frame of the strip
    if (t < 35.75) {
      const u = E.move(prog(t, 35.0, 35.7));
      const tx = g.fw.x + g.fw.w * 23 / 24, tw = g.fw.w / 24;
      const r = lerpView({ x: 0, y: 0, w: W, h: H }, { x: tx, y: TY, w: tw, h: TH }, u);
      ctx.globalAlpha = 1 - prog(t, 35.55, 35.75);
      ctx.drawImage(NB, r.x, r.y, r.w, r.h);
      ctx.globalAlpha = 1;
    }
    // the arc of the boomerang
    if (g.boom > 0.01) {
      ctx.save();
      ctx.globalAlpha = g.boom;
      ctx.strokeStyle = DINK; ctx.lineWidth = 2.5; ctx.lineCap = 'round';
      const a0 = g.fw.x + g.fw.w * 0.5, a1 = g.bk.x + g.bk.w * 0.5, ay = TY - 46;
      ctx.beginPath(); ctx.moveTo(a0, TY - 14); ctx.bezierCurveTo(a0, ay - 50, a1, ay - 50, a1, TY - 14); ctx.stroke();
      ctx.beginPath(); ctx.moveTo(a1 - 10, TY - 26); ctx.lineTo(a1, TY - 14); ctx.lineTo(a1 + 10, TY - 26); ctx.stroke();
      ctx.restore();
    }
    // music: the soundtrack's own waveform, under the whole video, with square fades at both ends
    const mu = E.move(prog(t, 39.15, 39.9));
    if (mu > 0) {
      const MY = TY + TH + 74, MH = 112;
      const span = (X1 - X0) * mu;
      ctx.save();
      ctx.beginPath(); ctx.rect(X0, MY - MH / 2 - 4, span, MH + 8); ctx.clip();
      const N = 400;
      ctx.fillStyle = 'rgba(244,242,238,0.85)';
      for (let i = 0; i < N; i++) {
        const u = i / N, x = X0 + u * (X1 - X0);
        const pk = PEAKS[Math.min(PEAKS.length - 1, Math.floor(u * PEAKS.length))] || 0;
        const fin = Math.min(1, u / 0.06) ** 2, fout = Math.min(1, (1 - u) / 0.1) ** 2;
        const hgt = Math.max(1.5, pk * MH * fin * fout);
        ctx.fillRect(x, MY - hgt / 2, Math.max(1.5, (X1 - X0) / N - 1.5), hgt);
      }
      // the fade envelopes
      ctx.strokeStyle = AMBER_D; ctx.lineWidth = 2.5;
      ctx.beginPath();
      for (let i = 0; i <= 60; i++) { const u = i / 60 * 0.06; const y = MY - MH / 2 * Math.min(1, u / 0.06) ** 2; i ? ctx.lineTo(X0 + u * (X1 - X0), y) : ctx.moveTo(X0, y); }
      ctx.lineTo(X0 + 0.9 * (X1 - X0), MY - MH / 2);
      for (let i = 0; i <= 60; i++) { const u = 0.9 + i / 60 * 0.1; const y = MY - MH / 2 * Math.min(1, (1 - u) / 0.1) ** 2; ctx.lineTo(X0 + u * (X1 - X0), y); }
      ctx.stroke();
      ctx.restore();
    }
    // the playhead sweeps the video once per step
    const sweeps = [[35.75, 36.95], [37.55, 38.85], [39.9, 41.2]];
    for (const [a, b] of sweeps) {
      if (t < a || t >= b) continue;
      const u = prog(t, a, b);
      const x = X0 + u * (X1 - X0);
      ctx.fillStyle = AMBER_D;
      ctx.fillRect(x - 1, TY - 18, 2.5, TH + 36 + (mu > 0 ? 150 : 0));
      ctx.beginPath(); ctx.moveTo(x - 8, TY - 26); ctx.lineTo(x + 9, TY - 26); ctx.lineTo(x + 0.5, TY - 16); ctx.closePath(); ctx.fill();
    }
    // labels
    const place = (lab, x, y, a) => { lab.style.transform = `translate(${x.toFixed(1)}px,${y}px)`; lab.style.opacity = a.toFixed(3); };
    place(lFwd, g.fw.x + 6, TY - 34, prog(t, 35.4, 35.7) * (1 - g.boom * 0.0));
    place(lHoldA, g.hs.x, TY + TH + 12, g.hold * (1 - prog(t, 39.1, 39.3)));
    place(lHoldB, g.he.x + g.he.w - lHoldB.offsetWidth, TY + TH + 12, g.hold * (1 - prog(t, 39.1, 39.3)));
    place(lBack, g.bk.x + 6, TY - 34, g.boom);
    place(lMus, X0, TY + TH + 140, prog(t, 39.6, 39.9));
    lenV.textContent = `${g.total.toFixed(1)} s`;
    lenBox.style.opacity = prog(t, 35.45, 35.75) * (1 - prog(t, 40.95, 41.25));
    segW.style.opacity = prog(t, 36.8, 37.0) * (1 - prog(t, 39.15, 39.35));
    seg.set(lerp(0, 1, E.move(prog(t, 37.0, 37.2))) * (1 - E.move(prog(t, 39.0, 39.2))), t >= 37.1 && t < 39.1 ? 1 : 0);
    // headline: one word per feature; the new word bright, the earlier ones dimmed
    reveal(kw, t, 35.55, 39.0);
    reveal(kwm, t, 39.1, 41.0);
    reveal(H1, t, 35.75, 41.0);
    reveal(H2, t, 37.0, 41.0);
    reveal(H3, t, 39.1, 41.0);
    H1[0].style.color = t >= 37.0 ? 'rgba(244,242,238,0.42)' : DINK;
    H2[0].style.color = t >= 39.1 ? 'rgba(244,242,238,0.42)' : DINK;
    // the strip leaves to the left, into the export gate
    const out = E.expoIn(prog(t, 41.0, 41.42));
    cv.style.transform = `translateX(${(-1400 * out).toFixed(1)}px)`;
  };
  return s;
})();

// ================================================================ EXPORT  the film gate  [41.0, 45.45)
const EXPORT = (() => {
  const s = scene(41.0, 45.7, 80);
  s.root.style.background = `radial-gradient(ellipse 80% 70% at 50% 55%,#141A33 0%,${NIGHT} 70%)`;
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const box = el('div', `position:absolute;left:120px;top:236px;color:${DINK}`, s.root);
  const k1 = el('div', `color:${DSUB};margin-bottom:6px`, box); k1.className = 'kick';
  const kE = words(k1, 'Exporting');
  const fn = el('div', 'font:600 52px/62px var(--disp);letter-spacing:-0.02em', box);
  const fnW = words(fn, 'Mountain lake.mp4');
  const pct = el('div', 'font:600 260px/250px var(--disp);letter-spacing:-0.04em;font-variant-numeric:tabular-nums;margin-top:26px;margin-left:-10px', box, '0%');
  const bar = el('div', 'position:relative;width:820px;height:6px;border-radius:3px;background:rgba(244,242,238,0.16);margin-top:30px;overflow:hidden', box);
  const fill = el('div', `position:absolute;left:0;top:0;height:6px;width:0;background:${AMBER_D};border-radius:3px`, bar);
  const left = el('div', `font:400 26px/36px var(--txt);color:${DSUB};margin-top:16px;font-variant-numeric:tabular-nums`, box, 'Working out the time left…');
  const specs = el('div', `position:absolute;left:1110px;top:720px;color:${DINK};font:400 30px/48px var(--txt)`, s.root);
  const SPEC = ['4K · 3840 × 2160', 'Encoded on the graphics card', 'MP4 · H.264 or HEVC', 'Or a looping GIF'];
  const specW = SPEC.map(x => { const d = el('div', '', specs); return words(d, x); });
  const GATE = { x: 1180, y: 300, w: 400, h: 225 };
  const FR = 26;                        // frames in the row
  HEAVY_MORE.push([40.95, 41.5, 10], [41.4, 44.4, 4], [44.95, 45.7, 8]);
  s.update = t => {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, W, H);
    const p = clamp((t - 41.35) / 2.85);             // progress, linear
    const P = Math.floor(p * 100);
    // frames slide right to left through the gate; the ones still to export at 35 %
    const fw = 196, gap = 12, step = fw + gap;
    const pos = p * (FR - 1);
    const inn = E.expoOut(prog(t, 41.05, 41.55));
    const grow = E.move(prog(t, 45.0, 45.6));
    ctx.save();
    const gx = GATE.x + GATE.w / 2;
    for (let i = 0; i < FR; i++) {
      const x = gx + (i - pos) * step - fw / 2 + (1 - inn) * 900;
      const fh = fw * 9 / 16, y = GATE.y + GATE.h / 2 - fh / 2;
      if (x > W + 50 || x + fw < 600) continue;
      const done = i <= pos + 0.001;
      const fade = clamp((x - 640) / 160) * clamp((W - 40 - x - fw) / 200);
      ctx.globalAlpha = fade * (done ? 1 : 0.35) * (1 - grow);
      ctx.drawImage(texAt(i / (FR - 1)), 0, 16, 300, 168, x, y, fw, fh);
    }
    ctx.restore();
    // the gate: 2 px amber, the frame inside it full size
    const cur = texAt(clamp(pos / (FR - 1)));
    const R = lerpView(GATE, { x: 0, y: 0, w: W, h: H }, grow);
    ctx.save();
    ctx.globalAlpha = inn;
    // the last frame rendered, then the first frame of the finished file as the premiere begins
    const back = prog(t, 44.8, 45.0);
    const Tg = clamp(Math.round(pos) / (FR - 1));
    if (back < 1) worldInto(ctx, R.x, R.y, R.w, R.h, Tg, viewFor(R.w, R.h), { ripple: Tg * 7200 }, 6 * (1 - grow));
    if (back > 0) { ctx.globalAlpha = inn * back; worldInto(ctx, R.x, R.y, R.w, R.h, 0.0, viewFor(R.w, R.h), { ripple: 0 }, 6 * (1 - grow)); }
    ctx.restore();
    if (grow < 1) {
      ctx.save();
      ctx.globalAlpha = inn * (1 - grow);
      ctx.strokeStyle = AMBER_D; ctx.lineWidth = 3;
      ctx.beginPath(); ctx.roundRect(R.x - 8, R.y - 8, R.w + 16, R.h + 16, 9); ctx.stroke();
      ctx.restore();
    }
    pct.textContent = `${P}%`;
    fill.style.width = `${p * 100}%`;
    const secs = Math.ceil((1 - p) * 9);
    left.textContent = p >= 1 ? 'Finishing the file…' : p < 0.06 ? 'Working out the time left…' : `About ${secs} s left`;
    const a = E.enter(prog(t, 41.1, 41.6)) * (1 - prog(t, 45.0, 45.3));
    box.style.opacity = a;
    reveal(kE, t, 41.15, 45.0);
    reveal(fnW, t, 41.2, 45.0);
    specW.forEach((wd, k) => reveal(wd, t, 41.4 + k * 0.3, 45.0, 0.03, 0.55));
  };
  return s;
})();

// ================================================================ PREMIERE  the evening in four seconds  [44.9, 49.45)
const PREMIERE = (() => {
  const s = scene(45.5, 49.45, 90);
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  scrim(s.root, 'left:0;bottom:0;width:1300px;height:520px;background:radial-gradient(ellipse 70% 90% at 22% 100%,rgba(6,8,18,0.66),rgba(6,8,18,0))');
  const card = el('div', 'position:absolute;left:120px;top:690px;color:#F4F2EE;font-family:var(--txt)', s.root);
  UI.theme(card, 1);
  card.innerHTML = `
    <svg width="64" height="64" viewBox="0 0 48 48"><circle data-r="bg" cx="24" cy="24" r="22" style="fill:var(--successBg)"/><path data-r="ck" d="M15 24.5l6 6 12-13" fill="none" style="stroke:var(--success)" stroke-width="3.4" stroke-linecap="round" stroke-linejoin="round" stroke-dasharray="28" stroke-dashoffset="28"/></svg>
    <div data-r="t" style="font:600 52px/62px var(--disp);letter-spacing:-0.02em;margin-top:18px">Mountain lake.mp4 is ready</div>
    <div data-r="d" style="font:400 28px/38px var(--txt);color:var(--text2);font-variant-numeric:tabular-nums;margin-top:6px">48.0 s · 4K · H.264 · 198 MB</div>`;
  const cr = refs(card);
  HEAVY_MORE.push([45.6, 49.0, 6]);
  s.update = t => {
    // the finished file plays from daylight to night
    const u = prog(t, 45.6, 48.9);
    const T = clamp(lerp(0.0, 1.0, E.sine(u) * 0.35 + u * 0.65));
    const p = Math.floor(T * 1439);
    WORLD.draw(ctx, W, H, T, V169, { ripple: p * 5, cloudX: T * 1.25 });
    cv.style.opacity = prog(t, 45.5, 45.62);
    const a = E.enter(prog(t, 45.85, 46.35));
    card.style.opacity = a;
    card.style.transform = `translateY(${(1 - E.enter(prog(t, 45.85, 46.4))) * 20}px)`;
    cr.ck.setAttribute('stroke-dashoffset', (28 * (1 - E.enter(prog(t, 46.05, 46.55)))).toFixed(2));
  };
  s.cv = cv;
  return s;
})();

// ================================================================ PRIVATE  [48.9, 53.45)
const PRIVATE = (() => {
  const s = scene(49.0, 53.45, 100);
  s.root.style.background = NIGHT;
  const bg = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  WORLD.draw(bg.getContext('2d'), W, H, 1.0, V169, { ripple: 7195 });
  const lk = sv('svg', { width: 96, height: 96, viewBox: '0 0 16 16', style: 'position:absolute;left:912px;top:262px;overflow:visible' }, s.root);
  const body = sv('rect', { x: 3.5, y: 7, width: 9, height: 7, rx: 1.5, fill: 'none', stroke: DINK, 'stroke-width': 0.9, pathLength: 1, 'stroke-dasharray': '1 1' }, lk);
  const shack = sv('path', { d: 'M5.5 7V5a2.5 2.5 0 0 1 5 0v2', fill: 'none', stroke: DINK, 'stroke-width': 0.9, 'stroke-linecap': 'round', pathLength: 1, 'stroke-dasharray': '1 1' }, lk);
  const t1 = Title(s.root, { x: 0, y: 420, w: 1920, align: 'center', head: 'Your photos never leave your PC.', sub: 'No account. No analytics.', color: DINK, color2: DSUB, headSize: 88, dur: 0.6, subDur: 0.55 });
  s.update = t => {
    // the last frame of the premiere, sinking back behind the words
    const d = E.move(prog(t, 49.0, 49.7));
    bg.style.filter = `blur(${(14 * d).toFixed(2)}px) brightness(${(1 - 0.6 * d).toFixed(3)})`;
    bg.style.transform = `scale(${1 + 0.04 * d})`;
    const lu = E.move(prog(t, 49.35, 49.95));
    body.setAttribute('stroke-dashoffset', (1 - lu).toFixed(4));
    shack.setAttribute('stroke-dashoffset', (1 - E.move(prog(t, 49.6, 50.1))).toFixed(4));
    // the shackle closes
    shack.setAttribute('transform', `translate(0 ${(-1.2 * (1 - E.enter(prog(t, 50.05, 50.35)))).toFixed(3)})`);
    lk.style.opacity = prog(t, 49.3, 49.5);
    t1.set(t, 49.3, null, 49.85);
  };
  return s;
})();

// ================================================================ END  five frames, the wordmark  [52.95, 59]
const END = (() => {
  const s = scene(52.95, DUR + 0.01, 110);
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const word = el('div', `position:absolute;left:0;top:0;color:${DINK};white-space:nowrap`, s.root);
  word.className = 'head';
  word.style.cssText += `;font-size:${LOCK.size}px;line-height:${LOCK.size * 1.1}px;letter-spacing:-0.02em`;
  const wl = chars(word, '10Lapse');
  const tag = el('div', `position:absolute;left:0;width:1920px;top:${LOCK.cy + 200}px;text-align:center;color:${DINK2};font:400 40px/52px var(--txt)`, s.root);
  const tw = words(tag, 'Turn a folder of photos into a timelapse.');
  const store = el('div', `position:absolute;left:0;width:1920px;top:${LOCK.cy + 300}px;text-align:center;color:${DINK};font:600 30px/40px var(--txt)`, s.root);
  const sw = words(store, 'On the Microsoft Store for Windows 11');
  const credit = el('div', `position:absolute;left:0;width:1920px;top:1000px;text-align:center;color:rgba(195,191,184,0.8);font:400 22px/30px var(--txt)`, s.root);
  const cw = words(credit, 'Motion reel by Claude');
  let wordW = 0;
  HEAVY_MORE.push([52.95, 54.2, 8]);
  s.update = t => {
    if (!wordW) wordW = word.getBoundingClientRect().width;
    s.root.style.background = t >= 53.42 ? NIGHT : 'transparent';
    const lx = (W - (LOCK.icon + LOCK.gap + wordW)) / 2;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, W, H);
    // five bars sweep up across the frame, then gather into the icon
    const g = E.move(prog(t, 53.42, 54.05));
    const side = E.move(prog(t, 53.9, 54.45));
    const size = LOCK.icon;
    const cx = lerp(W / 2, lx + size / 2, side), cy = LOCK.cy;
    const tile = { x: cx - size / 2, y: cy - size / 2, w: size, h: size };
    const gap = size * 0.75 / 40, bw = (size - 4 * gap) / 5;
    for (let k = 0; k < 5; k++) {
      const up = E.whip(prog(t, 52.98 + k * 0.05, 53.38 + k * 0.05));
      const full = { x: k * W / 5, y: H * (1 - up), w: W / 5 + 1, h: H * up };
      const ic = { x: tile.x + k * (bw + gap), y: tile.y, w: bw, h: size };
      const r = lerpView(full, ic, g);
      ctx.fillStyle = ICONC[k];
      ctx.save();
      ctx.beginPath(); ctx.roundRect(tile.x - (1 - g) * 2000, tile.y - (1 - g) * 2000, tile.w + (1 - g) * 4000, tile.h + (1 - g) * 4000, lerp(0, size * 10 / 40, g)); ctx.clip();
      ctx.fillRect(r.x, r.y, r.w, r.h);
      ctx.restore();
    }
    if (g > 0) {
      const sg = ctx.createLinearGradient(0, tile.y, 0, tile.y + size);
      sg.addColorStop(0, `rgba(255,255,255,${(0.22 * g).toFixed(3)})`); sg.addColorStop(0.5, 'rgba(255,255,255,0)');
      ctx.save(); ctx.beginPath(); ctx.roundRect(tile.x, tile.y, size, size, size * 10 / 40); ctx.clip();
      ctx.fillStyle = sg; ctx.fillRect(tile.x, tile.y, size, size); ctx.restore();
    }
    word.style.transform = `translate(${cx + size / 2 + LOCK.gap}px, ${LOCK.cy - LOCK.size * 0.62}px)`;
    reveal(wl, t, 54.1, null, 0.04, 0.7, 115);
    reveal(tw, t, 54.45, null, 0.02, 0.5);
    reveal(sw, t, 55.1, null, 0.04);
    reveal(cw, t, 55.9, null, 0.04);
  };
  return s;
})();
