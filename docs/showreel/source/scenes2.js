// Scenes 4 to 7: the import, deflicker, crop and pan, frame blending.
'use strict';

const HEAVY_MORE = window.HEAVY_MORE = [];
const lerpView = (a, b, u) => ({ x: lerp(a.x, b.x, u), y: lerp(a.y, b.y, u), w: lerp(a.w, b.w, u), h: lerp(a.h, b.h, u) });
// a keyframed camera: [t, scale, cx, cy]; the layout point (cx, cy) sits at the centre of the frame
function camera(keys, ease = E.move) {
  return t => {
    let k = 0;
    while (k + 1 < keys.length && t >= keys[k + 1][0]) k++;
    if (k + 1 >= keys.length) { const [, s, x, y] = keys[k]; return { s, x, y }; }
    const [a, s0, x0, y0] = keys[k], [b, s1, x1, y1] = keys[k + 1];
    const u = ease(prog(t, a, b));
    return { s: Math.exp(lerp(Math.log(s0), Math.log(s1), u)), x: lerp(x0, x1, u), y: lerp(y0, y1, u) };
  };
}
const camCss = c => `translate(${(960 - c.x * c.s).toFixed(2)}px,${(540 - c.y * c.s).toFixed(2)}px) scale(${c.s.toFixed(5)})`;
const DSUB = 'rgba(244,242,238,0.84)';

// ================================================================ IMPORT  drop a folder; the math on screen  [12.85, 17.45)
// Two copies of the window share one state: the wide one for the drop, a close one on the readout. A dissolve joins
// them, because sweeping the camera across the window's photos and text right after the drop flashes (WCAG 2.3.1).
const IMPORT = (() => {
  const s = scene(13.0, 17.45, 30);
  function view() {
    const cam = el('div', 'position:absolute;left:0;top:0;width:1920px;height:1080px;transform-origin:0 0', s.root);
    const holder = el('div', `position:absolute;left:${WIN.x}px;top:${WIN.y}px;width:1440px;height:900px;transform-origin:24px 20px`, cam);
    UI.theme(holder, 0);
    const w = UI.buildWindow(holder);
    w.gate.style.background = 'transparent';
    w.gateCv.style.transformOrigin = '50% 50%';
    return { cam, holder, w };
  }
  const A = view(), B = view();
  WORLD.draw(A.w.gateCv.getContext('2d'), 1080, 608, 0.0, V169, { ripple: 0 });
  B.w.gateCv.getContext('2d').drawImage(A.w.gateCv, 0, 0);
  const ghost = el('div', 'position:absolute;left:0;top:0;width:0;height:0', A.cam);
  ghost.innerHTML = `
    <div data-r="ph" style="position:absolute;left:0;top:0;transform-origin:0 0">
      <div data-r="g3" style="position:absolute;left:-70px;top:-34px;width:150px;height:100px;border-radius:4px;overflow:hidden;box-shadow:0 10px 30px rgba(30,20,10,0.28);transform:rotate(-8deg)"></div>
      <div data-r="g2" style="position:absolute;left:-56px;top:-28px;width:150px;height:100px;border-radius:4px;overflow:hidden;box-shadow:0 10px 30px rgba(30,20,10,0.28);transform:rotate(5deg)"></div>
      <div data-r="g1" style="position:absolute;left:-46px;top:-22px;width:150px;height:100px;border-radius:4px;overflow:hidden;box-shadow:0 10px 30px rgba(30,20,10,0.28)"></div>
      <div style="position:absolute;left:30px;top:88px;padding:4px 10px;border-radius:4px;background:#FEFDFC;border:1px solid #DCDAD5;color:#1C1B19;font-size:12px;line-height:16px;white-space:nowrap;box-shadow:0 4px 12px rgba(0,0,0,0.16)">+ Add 1,440 photos</div>
    </div>
    <svg style="position:absolute;left:0;top:0;overflow:visible" width="20" height="28" viewBox="0 0 20 28"><path d="M1 1 L1 22 L6.5 16.8 L10.5 25.5 L14 24 L10.2 15.6 L17.5 15.6 Z" fill="#FFFFFF" stroke="#111" stroke-width="1.4" stroke-linejoin="round"/></svg>`;
  const gr = refs(ghost);
  [['g1', 0.0], ['g2', 0.25], ['g3', 0.5]].forEach(([k, T]) => { const c = canvas(300, 200, 'width:150px;height:100px', gr[k]); c.getContext('2d').drawImage(texAt(T), 0, 0, 300, 200); });
  // where things are, in layout px
  const DROP = { x: WIN.x + 360, y: WIN.y + 560 };
  const camAt = camera([[13.0, 1, 960, 540], [13.7, 1, 960, 540], [14.3, 1.5, WIN.x + 380, WIN.y + 430], [17.45, 1.5, WIN.x + 380, WIN.y + 430]]);
  const CLOSE = { s: 2.5, x: WIN.x + 1262, y: WIN.y + 262 };
  B.cam.style.transform = camCss(CLOSE);
  HEAVY_MORE.push([13.5, 14.3, 6], [16.9, 17.55, 14]);

  // the state both windows show
  function apply(w, t) {
    w.icon16.style.opacity = t >= 13.53 ? 1 : 0;
    w.dropBg.style.opacity = prog(t, 14.15, 14.35) * (1 - prog(t, 14.5, 14.7));
    w.first.style.opacity = 1 - prog(t, 14.5, 14.75);
    w.projName.textContent = t >= 14.55 ? 'Mountain lake' : '';
    w.stage.style.opacity = prog(t, 14.5, 14.7);
    const ins = E.enter(prog(t, 14.55, 14.95));
    w.insp.style.opacity = ins; w.insp.style.transform = `translateX(${(1 - ins) * 16}px)`;
    const dv = E.enter(prog(t, 14.55, 15.25));
    w.gateCv.style.filter = dv < 1 ? `blur(${(14 * (1 - dv)).toFixed(2)}px)` : 'none';
    w.gateCv.style.transform = `scale(${1.03 - 0.03 * dv})`;
    w.gateCv.style.opacity = dv;
    w.thumbs.forEach((d, i) => {
      const u = E.enter(prog(t, 14.6 + i * 0.05, 15.0 + i * 0.05));
      d.style.opacity = u; d.style.transform = `translateY(${(1 - u) * 8}px)`;
    });
    w.ring.style.opacity = prog(t, 14.85, 15.0);
    const n = Math.round(1440 * E.sine(prog(t, 14.55, 15.05)));
    const len = n / 30;
    w.readout.textContent = n ? `${thousands(n)} photos → ${len.toFixed(1)} s` : '0 photos';
    w.lenBar.style.width = `${Math.min(1, len / 60) * 100}%`;
    w.frameLabel.textContent = `Frame 0001 of ${pad4(Math.max(1, n))}`;
    w.tc.innerHTML = `0001 / ${pad4(Math.max(1, n))}&nbsp;&nbsp;&nbsp;00:00.00 / 00:${len.toFixed(2).padStart(5, '0')}`;
  }

  s.update = t => {
    const c = camAt(t);
    A.cam.style.transform = camCss(c);
    // a lens that softens while the camera moves: UI text sweeping past reads as stripes (WCAG 2.3.1)
    const c2 = camAt(t + 1 / 60);
    const sp = Math.abs(c2.s - c.s) / c.s * 900 + Math.hypot(c2.x - c.x, c2.y - c.y) * c.s;
    A.cam.style.filter = sp * 0.5 > 0.15 ? `blur(${Math.min(10, sp * 0.5).toFixed(2)}px)` : 'none';
    const whip = 2600 * (E.expoIn(prog(t + 1 / 60, 17.0, 17.42)) - E.expoIn(prog(t, 17.0, 17.42)));
    B.cam.style.filter = whip / 6 > 0.15 ? `blur(${Math.min(22, whip / 6).toFixed(2)}px)` : 'none';
    // the window opens around the icon as it lands in the title bar
    const op = E.enter(prog(t, 13.25, 13.8));
    A.holder.style.opacity = clamp(prog(t, 13.25, 13.55));
    A.holder.style.transform = `scale(${lerp(0.93, 1, op)})`;
    // the drag: three photos and the cursor come in, hover, and drop
    const gu = E.glide(prog(t, 13.75, 14.35));
    const drop = E.enter(prog(t, 14.5, 14.8));
    ghost.style.transform = `translate(${lerp(WIN.x + 1500, DROP.x, gu)}px,${lerp(WIN.y + 980, DROP.y, gu)}px)`;
    ghost.style.opacity = prog(t, 13.75, 13.9) * (1 - prog(t, 14.65, 14.85));
    gr.ph.style.transform = `scale(${lerp(1, 0.25, drop)})`;
    gr.ph.style.opacity = 1 - drop;
    // the wide view dissolves into the readout
    const x = E.sine(prog(t, 15.0, 15.2));
    show(A.cam, x < 1); show(B.cam, x > 0);
    A.cam.style.opacity = 1;
    B.cam.style.opacity = x;
    if (x < 1) apply(A.w, t);
    if (x > 0) apply(B.w, t);
    // whip out
    s.root.style.transform = `translateX(${(-2600 * E.expoIn(prog(t, 17.0, 17.42))).toFixed(1)}px)`;
  };
  return s;
})();

// ================================================================ DEFLICKER  [17.0, 23.45)
// The sunset plays at 40 photos a second; the photographer opened up a third of a stop every 20 photos.
const DEFLICKER = (() => {
  const s = scene(17.0, 23.45, 55);
  const slide = el('div', 'position:absolute;inset:0', s.root);
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', slide);
  const ctx = cv.getContext('2d');
  scrim(slide, 'left:0;top:0;width:1300px;height:560px;background:radial-gradient(ellipse 70% 80% at 18% 22%,rgba(14,10,24,0.5),rgba(14,10,24,0))').className = 'scr';
  scrim(slide, 'left:0;bottom:0;width:1920px;height:420px;background:linear-gradient(0deg,rgba(12,9,22,0.74),rgba(12,9,22,0))').className = 'scr';
  const title = Title(slide, { x: 120, y: 112, kicker: 'Look', head: 'Deflicker.', sub: 'Exposure steps become a smooth ramp.', color: DINK, color2: DSUB, subDur: 0.5 });
  const cardWrap = el('div', 'position:absolute;left:1480px;top:150px', slide);
  UI.theme(cardWrap, 1);
  const look = UI.lookCard(cardWrap, { label: 'Deflicker', hint: 'Smooths exposure jumps between photos', slider: 'Strength', value: '80%' });
  look.slider.set(0.8);
  // the graph: one point per photo
  const P0 = 446, NP = 240, X0 = 120, X1 = 1800, GY = 900, A = 54;
  const px = i => X0 + (i - P0) / NP * (X1 - X0);
  const base = i => GY + (i - P0) / NP * 34;
  const raw = i => base(i) - A / 2 + A * (((i - P0) / 20) % 1);
  const gsv = sv('svg', { width: W, height: H, style: 'position:absolute;left:0;top:0;overflow:visible' }, slide);
  let d = '';
  for (let k = 0; k < NP / 20; k++) {
    const a = P0 + k * 20, b = a + 20;
    d += `${k ? 'L' : 'M'}${px(a).toFixed(1)} ${(base(a) - A / 2).toFixed(1)} L${px(b).toFixed(1)} ${(base(b) + A / 2).toFixed(1)} `;
  }
  const rawP = sv('path', { d, fill: 'none', stroke: '#FFFFFF', 'stroke-width': 2.2, 'stroke-linejoin': 'round', pathLength: 1, 'stroke-dasharray': '1 1' }, gsv);
  let d2 = `M${px(P0)} ${base(P0)} L${px(P0 + NP)} ${base(P0 + NP)}`;
  const fixP = sv('path', { d: d2, fill: 'none', stroke: AMBER_D, 'stroke-width': 4, 'stroke-linecap': 'round', pathLength: 1, 'stroke-dasharray': '1 1' }, gsv);
  const head = sv('line', { y1: 820, y2: 975, stroke: '#FFFFFF', 'stroke-width': 1.5, 'stroke-opacity': 0.55 }, gsv);
  const dotC = sv('circle', { r: 7, fill: '#FFFFFF' }, gsv);
  const SHUT = ['1/250', '1/200', '1/160', '1/125', '1/100', '1/80', '1/60', '1/50', '1/40', '1/30', '1/25', '1/20'];
  const labs = SHUT.map((v, k) => {
    const a = P0 + k * 20;
    return el('div', `position:absolute;left:${px(a) + 6}px;top:${base(a) - A / 2 - 30}px;font:600 15px/18px var(--txt);color:rgba(255,255,255,0.78);font-variant-numeric:tabular-nums;white-space:nowrap`, slide, `${v} s`);
  });
  const cap = el('div', 'position:absolute;left:120px;top:782px;font:600 20px/26px var(--txt);color:rgba(255,255,255,0.86)', slide, 'Exposure, photo by photo');
  const leg = el('div', `position:absolute;left:1500px;top:782px;width:300px;display:flex;gap:22px;justify-content:flex-end;font:600 18px/26px var(--txt);color:rgba(255,255,255,0.86)`, slide,
    `<span style="display:flex;align-items:center;gap:8px"><i style="display:block;width:22px;height:2px;background:#fff"></i>Photos</span><span data-r="legA" style="display:flex;align-items:center;gap:8px"><i style="display:block;width:22px;height:4px;border-radius:2px;background:${AMBER_D}"></i>Deflicker</span>`);
  const legA = refs(leg).legA;
  const ON = 19.0;
  HEAVY_MORE.push([16.95, 17.55, 14]);
  s.update = t => {
    const sx = 1920 * (1 - E.expoOut(prog(t, 17.04, 17.5))), sx2 = 1920 * (1 - E.expoOut(prog(t + 1 / 60, 17.04, 17.5)));
    slide.style.transform = `translateX(${sx.toFixed(1)}px)`;
    const wb = Math.min(22, Math.abs(sx - sx2) / 6);
    slide.style.filter = wb > 0.15 ? `blur(${wb.toFixed(2)}px)` : 'none';
    const p = P0 + (t - 17.0) * 40;
    const T = p / 1439;
    const z = 1.03 + 0.03 * prog(t, 17, 23.4);
    WORLD.draw(ctx, W, H, T, viewFor(W, H, 0.75, 0.5, z), { ripple: Math.floor(p) * 5 });
    // the flicker: a sawtooth of a third of a stop, gone once deflicker is on
    const on = E.move(prog(t, ON, ON + 0.25));
    const saw = t < 17.5 ? 0 : 0.25 - 0.5 * (((t - 17.5) * 2) % 1);
    cv.style.filter = `brightness(${Math.pow(2, saw * (1 - on)).toFixed(4)})`;
    look.toggle.set(E.move(prog(t, ON - 0.08, ON + 0.09)));
    cardWrap.style.opacity = E.enter(prog(t, 17.55, 18.0));
    cardWrap.style.transform = `translateX(${(1 - E.enter(prog(t, 17.55, 18.05))) * 28}px)`;
    title.set(t, 17.65, 23.0, 19.25);
    const gone = t >= 23.0;
    show(cv, !gone); slide.querySelectorAll('.scr').forEach(n => show(n, !gone));
    // graph
    const dr = prog(t, 17.35, 18.1);
    rawP.setAttribute('stroke-dashoffset', (1 - dr).toFixed(4));
    rawP.setAttribute('stroke-opacity', lerp(0.95, 0.32, on).toFixed(3));
    const fx = E.move(prog(t, ON + 0.05, ON + 0.75));
    fixP.setAttribute('stroke-dashoffset', (1 - fx).toFixed(4));
    labs.forEach((l, k) => { l.style.opacity = (clamp((dr * 12 - k) * 1.5) * lerp(1, 0.45, on)).toFixed(3); });
    const hx = px(Math.min(p, P0 + NP - 0.01));
    head.setAttribute('x1', hx); head.setAttribute('x2', hx);
    const yy = lerp(raw(p), base(p), on);
    dotC.setAttribute('cx', hx); dotC.setAttribute('cy', yy);
    dotC.setAttribute('fill', on > 0.5 ? AMBER_D : '#FFFFFF');
    const ga = prog(t, 17.35, 17.6) * (t >= 23.0 ? 0 : 1);
    gsv.style.opacity = ga; cap.style.opacity = ga; leg.style.opacity = ga;
    labs.forEach(l => { l.style.visibility = ga > 0 ? 'visible' : 'hidden'; });
    legA.style.opacity = prog(t, ON + 0.1, ON + 0.4);
    cardWrap.style.opacity = E.enter(prog(t, 17.55, 18.0)) * (1 - prog(t, 23.0, 23.25));
  };
  return s;
})();

// ================================================================ CROP  crop and pan, then any shape  [23.0, 29.45)
const CROP = (() => {
  const s = scene(23.0, 29.45, 50);
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const scr = scrim(s.root, 'left:0;top:0;width:1100px;height:1080px;background:linear-gradient(90deg,rgba(10,10,26,0.62),rgba(10,10,26,0))');
  const P = { x: 660, y: 170, w: 1140, h: 760 };                  // the whole photo, as the preview shows it
  const P2 = { x: 0, y: -100, w: 1920, h: 1280 };                  // the photo when its 16:9 band fills the frame
  const SV = V169, EV = { x: 0.735, y: 0.474, w: 0.5, h: 0.28125 };   // start and end framings, 3x
  const toScr = (v, R) => ({ x: R.x + v.x / 1.5 * R.w, y: R.y + v.y * R.h, w: v.w / 1.5 * R.w, h: v.h * R.h });
  const t1 = Title(s.root, { x: 120, y: 360, kicker: 'Frame', head: 'Crop and pan.', sub: 'Up to 4× zoom.', color: DINK, color2: DSUB, w: 560, subW: 470 });
  const t2 = Title(s.root, { x: 120, y: 360, kicker: 'Frame', head: 'Any shape.', color: DINK, color2: DSUB, w: 560, subW: 470, dur: 0.5 });
  // the Preview shows control, and the Shape control
  const ui = el('div', 'position:absolute;left:0;top:0;font-family:var(--txt)', s.root);
  UI.theme(ui, 1);
  const showW = el('div', `position:absolute;left:${P.x}px;top:${P.y - 58}px;display:flex;align-items:center;gap:14px;color:var(--text2);font-size:16px`, ui);
  el('span', '', showW, 'Preview shows');
  const segShow = UI.segmented(showW, ['Video', 'Start', 'End'], 270);
  const shapeW = el('div', 'position:absolute;left:0;top:0', ui);
  const segShape = UI.segmented(shapeW, ['Match', '16:9', '9:16', '1:1', '4:5', '4:3'], 480);
  const pill = (txt) => el('div', 'position:absolute;left:0;top:0;height:28px;padding:0 10px;border-radius:14px;background:rgba(12,12,13,0.62);color:#F4F2EE;font:600 14px/28px var(--txt);white-space:nowrap', ui, txt);
  const pS = pill('Start'), pE = pill('End · 3.0×');
  const ASP = [[27.0, 16 / 9], [27.5, 9 / 16], [27.875, 1], [28.25, 4 / 5], [28.625, 4 / 3], [29.0, 16 / 9]];
  const SHI = [1, 2, 3, 4, 5, 1];
  const GC = { x: 1215, y: 500 }, GH = 640;
  function gateAt(t) {
    let k = 0;
    while (k + 1 < ASP.length && t >= ASP[k + 1][0]) k++;
    let a = ASP[k][1];
    if (k + 1 < ASP.length) a = Math.exp(lerp(Math.log(a), Math.log(ASP[k + 1][1]), E.move(prog(t, ASP[k + 1][0] - 0.3, ASP[k + 1][0]))));
    const w = Math.min(1140, GH * a), h = Math.min(GH, w / a);
    return { x: GC.x - w / 2, y: GC.y - h / 2, w, h, a, k };
  }
  HEAVY_MORE.push([22.95, 23.9, 8], [24.3, 25.0, 6], [24.95, 27.1, 8], [26.95, 29.5, 8]);
  s.update = t => {
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.fillStyle = '#0B0C16'; ctx.fillRect(0, 0, W, H);
    const T = 0.477 + (t - 23.0) * 0.019;
    const rip = Math.floor(T * 1439) * 5;
    const pull = E.move(prog(t, 23.0, 23.8));          // full frame -> the whole photo
    const push = E.move(prog(t, 25.0, 25.45));         // the whole photo -> the start framing fills the frame
    const mv = E.move(prog(t, 25.45, 26.95));          // the move, as the export draws it ("Smooth")
    const sh = prog(t, 27.0, 27.35), back = E.move(prog(t, 29.0, 29.45));
    let overlay = 0;
    if (t < 25.45) {
      const R = t < 25.0 ? lerpView(P2, P, pull) : lerpView(P, P2, push);
      worldInto(ctx, R.x, R.y, R.w, R.h, T, V32, { ripple: rip }, 6 * pull * (1 - push));
      overlay = prog(t, 23.55, 23.85) * (1 - prog(t, 24.95, 25.2));
      if (overlay > 0) {
        // dim outside the framing being edited, then draw the framings: 2 px amber with 12 px corner handles
        const sel = t < 24.4 ? 0 : E.move(prog(t, 24.4, 24.9));
        const fr = toScr(lerpView(SV, EV, sel), R);
        ctx.save();
        ctx.beginPath(); ctx.rect(R.x, R.y, R.w, R.h); ctx.rect(fr.x + fr.w, fr.y, -fr.w, fr.h);
        ctx.fillStyle = `rgba(12,12,13,${(0.62 * overlay).toFixed(3)})`; ctx.fill('evenodd');
        ctx.restore();
        const S0 = toScr(SV, R);
        ctx.save();
        ctx.globalAlpha = overlay;
        ctx.strokeStyle = AMBER_D; ctx.lineWidth = 2;
        const box = (b, a) => {
          ctx.globalAlpha = overlay * a;
          ctx.strokeRect(b.x + 1, b.y + 1, b.w - 2, b.h - 2);
          ctx.lineWidth = 4; ctx.lineCap = 'square';
          for (const [x, y, sx, sy] of [[b.x, b.y, 1, 1], [b.x + b.w, b.y, -1, 1], [b.x + b.w, b.y + b.h, -1, -1], [b.x, b.y + b.h, 1, -1]]) {
            ctx.beginPath(); ctx.moveTo(x + 2 * sx, y + 14 * sy); ctx.lineTo(x + 2 * sx, y + 2 * sy); ctx.lineTo(x + 14 * sx, y + 2 * sy); ctx.stroke();
          }
          ctx.lineWidth = 2;
        };
        if (sel > 0) {
          box(S0, 0.45);
          // the path of the move, start centre to end centre
          ctx.setLineDash([2, 8]); ctx.lineCap = 'round'; ctx.lineWidth = 2.5; ctx.globalAlpha = overlay * sel * 0.9;
          ctx.beginPath(); ctx.moveTo(S0.x + S0.w / 2, S0.y + S0.h / 2); ctx.lineTo(fr.x + fr.w / 2, fr.y + fr.h / 2); ctx.stroke();
          ctx.setLineDash([]);
        }
        box(fr, 1);
        ctx.restore();
        pS.style.transform = `translate(${S0.x + 10}px,${S0.y + 10}px)`;
        pE.style.transform = `translate(${fr.x + 10}px,${fr.y + 10}px)`;
        pS.style.opacity = overlay; pE.style.opacity = overlay * sel;
      }
      if (overlay <= 0) { pS.style.opacity = 0; pE.style.opacity = 0; }
    } else if (t < 27.0) {
      pS.style.opacity = 0; pE.style.opacity = 0;
      WORLD.draw(ctx, W, H, T, lerpView(SV, EV, mv), { ripple: rip });
    } else {
      pS.style.opacity = 0; pE.style.opacity = 0;
      // the shapes: the frame closes around each shape in turn (400 ms Move in the app; quicker here)
      const g0 = gateAt(t);
      let R = lerpView({ x: 0, y: 0, w: W, h: H }, g0, E.move(sh));
      if (back > 0) R = lerpView(g0, { x: 0, y: 0, w: W, h: H }, back);
      const v = viewFor(R.w, R.h, 0.985, 0.615, 3);
      worldInto(ctx, R.x, R.y, R.w, R.h, T, v, { ripple: rip }, 6 * (1 - back) * clamp(sh * 3));
      shapeW.style.transform = `translate(${GC.x - 240}px,${GC.y + GH / 2 + 30}px)`;
      const k = g0.k + (prog(t, ASP[Math.min(g0.k + 1, 5)][0] - 0.3, ASP[Math.min(g0.k + 1, 5)][0]) > 0.5 ? 1 : 0);
      const kk = Math.min(k, 5);
      let f = SHI[g0.k];
      if (g0.k + 1 < SHI.length) f = lerp(SHI[g0.k], SHI[g0.k + 1], E.move(prog(t, ASP[g0.k + 1][0] - 0.3, ASP[g0.k + 1][0])));
      segShape.set(f, SHI[kk]);
    }
    showW.style.opacity = overlay;
    segShow.set(t < 24.4 ? 1 : t < 25.0 ? lerp(1, 2, E.move(prog(t, 24.4, 24.6))) : lerp(2, 0, E.move(prog(t, 25.0, 25.2))), t < 24.5 ? 1 : t < 25.1 ? 2 : 0);
    shapeW.style.opacity = prog(t, 27.15, 27.4) * (1 - prog(t, 28.95, 29.15));
    scr.style.opacity = 1 - back;
    t1.set(t, 23.55, 27.0, 24.05);
    t2.set(t, 27.3, 29.0);
  };
  return s;
})();

// ================================================================ BLEND  frame blending  [29.4, 35.45)
// Night: 16 photos 5 s apart, each a 4 s exposure, averaged into one frame.
const BLEND = (() => {
  const s = scene(29.4, 35.45, 60);
  s.root.style.background = '#05070F';
  const cv = canvas(W, H, 'position:absolute;left:0;top:0', s.root);
  const ctx = cv.getContext('2d');
  const TN = 0.93, TAU0 = TN * 7200;
  const EV = { x: 0.735, y: 0.474, w: 0.5, h: 0.28125 };
  // the exploded stack: the landscape at the back, then one plate of car lights per photo
  const deck = el('div', 'position:absolute;inset:0;perspective:2400px;perspective-origin:50% 45%', s.root);
  const rig = el('div', 'position:absolute;left:0;top:0;width:1920px;height:1080px;transform-style:preserve-3d;transform-origin:50% 50%', deck);
  const baseC = canvas(1920, 1080, 'position:absolute;left:0;top:0;width:1920px;height:1080px;backface-visibility:hidden', rig);
  WORLD.draw(baseC.getContext('2d'), 1920, 1080, TN, V169, { cars: false, tau: TAU0 + 75 });
  const plates = [];
  for (let k = 0; k < 16; k++) {
    const c = canvas(960, 540, 'position:absolute;left:0;top:0;width:1920px;height:1080px;box-sizing:border-box;border:1px solid rgba(255,255,255,0.28);background:rgba(30,36,80,0.06)', rig);
    WORLD.cars(c.getContext('2d'), 960, 540, TN, V169, { tau: TAU0 + k * INTERVAL });
    plates.push(c);
  }
  const lab = el('div', `position:absolute;left:0;top:0;color:${DINK};font:600 44px/52px var(--disp);letter-spacing:-0.02em;white-space:nowrap`, s.root);
  const labN = el('span', 'font-variant-numeric:tabular-nums', lab, '16');
  el('span', '', lab, ' photos');
  const scr = scrim(s.root, 'left:0;top:0;width:1300px;height:620px;background:radial-gradient(ellipse 72% 80% at 18% 26%,rgba(3,5,14,0.62),rgba(3,5,14,0))');
  const title = Title(s.root, { x: 120, y: 112, kicker: 'Look', head: 'Frame blending.', sub: 'Light trails from 16 photos.', color: DINK, color2: DSUB, subW: 760, subDur: 0.55 });
  const cardWrap = el('div', 'position:absolute;left:1480px;top:150px', s.root);
  UI.theme(cardWrap, 1);
  const look = UI.lookCard(cardWrap, { label: 'Frame blending', hint: 'Blends nearby photos for light trails', slider: 'Photos blended', value: '16' });
  look.slider.set(1);
  HEAVY_MORE.push([29.4, 30.2, 10], [30.1, 31.2, 8]);
  s.update = t => {
    // pull back from the village as night falls, then the stack opens and closes
    const pb = E.move(prog(t, 29.4, 30.15));
    const T = lerp(0.595, TN, E.sine(prog(t, 29.4, 30.1)));
    const ex = E.move(prog(t, 30.15, 30.55)), cl = E.move(prog(t, 30.6, 31.0));
    const open = ex * (1 - cl);
    const deckOn = t >= 30.15 && t < 31.06;
    show(deck, deckOn);
    const cvA = deckOn ? prog(t, 30.96, 31.05) : 1;
    cv.style.opacity = cvA;
    if (cvA > 0) {
      const blended = t >= 30.5;
      const tau = TAU0 + 75 + Math.max(0, t - 31.1) * 9;
      WORLD.draw(ctx, W, H, T, blended ? V169 : lerpView(EV, V169, pb), blended ? { blend: 16, tau, ripple: 0 } : { ripple: Math.floor(T * 1439) * 5 });
    }
    if (deckOn) {
      rig.style.transform = `translateZ(${(-900 * open).toFixed(1)}px) rotateY(${(-38 * open).toFixed(2)}deg) rotateX(${(9 * open).toFixed(2)}deg)`;
      plates.forEach((c, k) => {
        const a = k === 0 ? 1 : clamp(ex * 16 - k * 0.6);
        c.style.opacity = a.toFixed(3);
        c.style.transform = `translateZ(${((k + 1) * 46 * open).toFixed(1)}px)`;
        c.style.borderColor = `rgba(255,255,255,${(0.32 * open).toFixed(3)})`;
      });
      baseC.style.transform = 'translateZ(0px)';
    }
    lab.style.opacity = (prog(t, 30.3, 30.5) * (1 - prog(t, 30.68, 30.88))).toFixed(3);
    lab.style.transform = `translate(${1250}px, ${250}px)`;
    title.set(t, 31.0, 35.0, 31.3);
    scr.style.opacity = prog(t, 30.8, 31.2) * (1 - prog(t, 35.0, 35.3));
    look.toggle.set(E.move(prog(t, 31.35, 31.52)));
    cardWrap.style.opacity = E.enter(prog(t, 31.05, 31.5)) * (1 - prog(t, 35.0, 35.25));
    cardWrap.style.transform = `translateX(${(1 - E.enter(prog(t, 31.05, 31.55))) * 28}px)`;
  };
  return s;
})();
