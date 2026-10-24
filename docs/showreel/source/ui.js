// The 10Lapse window and inspector pieces, rebuilt in HTML from DESIGN.md's tokens and the app's own strings.
'use strict';

const UI = (() => {
  const LIGHT = {
    mica: '#F3F2F0', layer: '#FAF9F7', card: '#FEFDFC', ctrl: '#FEFDFC', stroke: '#DCDAD5', divider: '#E7E5E1',
    text: '#1C1B19', text2: '#5D5A55', accent: '#E8962E', accentStroke: 'rgba(120,70,10,0.22)', onAccent: '#1F1404',
    accentText: '#9A5208', accentTint: 'rgba(232,150,46,0.10)', segTrack: '#F0EEEB', pill: '#FEFDFC',
    pillShadow: '0 1px 2px rgba(28,27,25,0.14), 0 0 0 1px rgba(28,27,25,0.04)', track: '#E4E2DE',
    success: '#0F7B0F', successBg: '#DFF6DD', flyout: '0 8px 28px rgba(28,27,25,0.16), 0 2px 6px rgba(28,27,25,0.06)',
    dropStroke: '#CFCCC6', indicator: '#B5620D', frame: 'rgba(0,0,0,0.10)', subtleHover: 'rgba(28,27,25,0.06)',
  };
  const DARK = {
    mica: '#1F1E1D', layer: '#282726', card: '#302F2D', ctrl: '#363431', stroke: '#4A4743', divider: '#393735',
    text: '#F4F2EE', text2: '#C3BFB8', accent: '#F0A54A', accentStroke: 'rgba(255,230,190,0.18)', onAccent: '#1F1404',
    accentText: '#F3B05C', accentTint: 'rgba(240,165,74,0.10)', segTrack: '#2E2D2B', pill: '#4A4743',
    pillShadow: '0 1px 2px rgba(0,0,0,0.4)', track: '#3F3D3A',
    success: '#6CCB5F', successBg: '#26381F', flyout: '0 8px 28px rgba(0,0,0,0.5), 0 2px 6px rgba(0,0,0,0.3)',
    dropStroke: '#5A5651', indicator: '#F0A54A', frame: 'rgba(255,255,255,0.10)', subtleHover: 'rgba(244,242,238,0.06)',
  };
  // u = 0 light, 1 dark; colours crossfade, the rest switches at the midpoint
  function theme(node, u) {
    for (const k in LIGHT) {
      const a = LIGHT[k], b = DARK[k];
      node.style.setProperty('--' + k, a[0] === '#' && b[0] === '#' ? rgba(mixc(a, b, u)) : (u < 0.5 ? a : b));
    }
  }

  const IC = (d, s = 16, x = '') => `<svg width="${s}" height="${s}" viewBox="0 0 16 16" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round" ${x}>${d}</svg>`;
  const I = {
    plus: '<path d="M8 3v10M3 8h10"/>',
    file: '<path d="M9 2H4.5A1.5 1.5 0 0 0 3 3.5v9A1.5 1.5 0 0 0 4.5 14h7a1.5 1.5 0 0 0 1.5-1.5V6zM9 2v4h4"/>',
    chev: '<path d="M4 6l4 4 4-4"/>',
    newp: '<path d="M9 2H4.5A1.5 1.5 0 0 0 3 3.5v9A1.5 1.5 0 0 0 4.5 14h7a1.5 1.5 0 0 0 1.5-1.5V6zM9 2v4h4M8 8.5v3M6.5 10h3"/>',
    gear: '<path d="M2.5 5h6M12.5 5h1M2.5 11h1M7.5 11h6"/><circle cx="10.5" cy="5" r="1.75"/><circle cx="5.5" cy="11" r="1.75"/>',
    up: '<path d="M8 10V2.5M5 5.5L8 2.5l3 3M3 9v3.5A1.5 1.5 0 0 0 4.5 14h7a1.5 1.5 0 0 0 1.5-1.5V9"/>',
    folder: '<path d="M2 4.5A1.5 1.5 0 0 1 3.5 3h3l1.5 1.5h4.5A1.5 1.5 0 0 1 14 6v5.5a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 2 11.5z"/>',
    photos: '<rect x="2" y="3" width="12" height="10" rx="1.5"/><path d="M2 11l3.5-3.5 3 3 2-2L14 12"/>',
    lock: '<rect x="3.5" y="7" width="9" height="7" rx="1.5"/><path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2"/>',
    play: '<path d="M5 3.2v9.6a.6.6 0 0 0 .9.5l7.4-4.8a.6.6 0 0 0 0-1L5.9 2.7a.6.6 0 0 0-.9.5z" fill="currentColor" stroke="none"/>',
  };
  // TenLapse-16.svg: five bands of 3, 3, 4, 3 and 3 px on a 16 px tile with 3.5 px corners
  const ICON16 = '<svg width="16" height="16" viewBox="0 0 16 16"><defs><clipPath id="ic16"><rect width="16" height="16" rx="3.5"/></clipPath></defs><g clip-path="url(#ic16)"><rect x="0" width="3" height="16" fill="#8EC0EA"/><rect x="3" width="3" height="16" fill="#F3C46B"/><rect x="6" width="4" height="16" fill="#EE8F4F"/><rect x="10" width="3" height="16" fill="#B4567A"/><rect x="13" width="3" height="16" fill="#2B3A75"/></g></svg>';

  function segmented(parent, labels, width, h = 32) {
    const wrap = el('div', `position:relative;display:grid;grid-template-columns:repeat(${labels.length},minmax(0,1fr));height:${h}px;border-radius:5px;background:var(--segTrack);border:1px solid var(--stroke);padding:2px;box-sizing:border-box;width:${width}px`, parent);
    const segW = (width - 6) / labels.length;
    const pill = el('div', `position:absolute;top:2px;bottom:2px;left:2px;width:${segW}px;border-radius:3px;background:var(--pill);box-shadow:var(--pillShadow)`, wrap);
    const items = labels.map(l => el('div', 'position:relative;display:grid;place-items:center;font-size:14px;line-height:20px;color:var(--text2);white-space:nowrap;font-variant-numeric:tabular-nums', wrap, l));
    return {
      wrap, items,
      set(f, sel) {
        pill.style.transform = `translateX(${f * segW}px)`;
        items.forEach((it, i) => { it.style.fontWeight = i === sel ? 600 : 400; it.style.color = i === sel ? 'var(--text)' : 'var(--text2)'; });
      },
    };
  }

  // 40 x 20 toggle: off is an Ink Secondary outline and knob, on a Deep Amber Indicator track with a Card knob
  function toggle(parent) {
    const t = el('div', 'position:relative;width:40px;height:20px;border-radius:10px;box-sizing:border-box;flex:none', parent);
    const on = el('div', 'position:absolute;inset:0;border-radius:10px;background:var(--indicator);opacity:0', t);
    const ring = el('div', 'position:absolute;inset:0;border-radius:10px;border:1px solid var(--text2);box-sizing:border-box', t);
    const knob = el('div', 'position:absolute;left:4px;top:4px;width:12px;height:12px;border-radius:6px;background:var(--text2)', t);
    return {
      el: t,
      set(u) {
        on.style.opacity = u; ring.style.opacity = 1 - u;
        knob.style.transform = `translateX(${u * 20}px) scale(${1 + 0.17 * Math.sin(Math.PI * u) * 0})`;
        knob.style.background = u > 0.5 ? 'var(--card)' : 'var(--text2)';
      },
    };
  }

  function slider(parent, width) {
    const s = el('div', `position:relative;width:${width}px;height:20px`, parent);
    el('div', 'position:absolute;left:0;right:0;top:8px;height:4px;border-radius:2px;background:var(--track)', s);
    const fill = el('div', 'position:absolute;left:0;top:8px;height:4px;border-radius:2px;background:var(--text2);width:0', s);
    const knob = el('div', 'position:absolute;top:0;width:20px;height:20px;margin-left:-10px;border-radius:50%;background:var(--ctrl);box-shadow:0 0 0 1px var(--stroke),0 1px 2px rgba(0,0,0,0.25);display:grid;place-items:center', s);
    el('div', 'width:12px;height:12px;border-radius:50%;background:var(--text2)', knob);
    return { el: s, set(u) { fill.style.width = (u * width) + 'px'; knob.style.left = (u * width) + 'px'; } };
  }

  // ------------------------------------------------------------ the window, 1440 x 900
  function buildWindow(parent) {
    const win = el('div', 'position:absolute;left:0;top:0;width:1440px;height:900px;transform-origin:0 0;color:var(--text);font-family:var(--txt);font-size:14px;line-height:20px', parent);
    win.innerHTML = `
    <div data-r="shadow" style="position:absolute;inset:0;border-radius:8px;box-shadow:0 50px 140px rgba(20,16,10,0.34),0 14px 40px rgba(20,16,10,0.22)"></div>
    <div data-r="mica" style="position:absolute;inset:0;border-radius:8px;background:var(--mica);box-shadow:inset 0 0 0 1px var(--frame)"></div>
    <div data-r="chrome" style="position:absolute;left:0;top:0;width:1440px;height:88px">
      <div style="position:absolute;left:0;top:0;width:1440px;height:40px;display:flex;align-items:center;gap:12px;padding-left:16px;box-sizing:border-box">
        <span data-r="icon16" style="display:inline-block;width:16px;height:16px">${ICON16}</span><span style="font-size:12px;line-height:16px">10Lapse</span>
        <span data-r="projName" style="font-size:12px;line-height:16px;color:var(--text2)"></span>
        <div style="flex:1"></div>
        <div style="display:flex;height:40px">
          <div style="width:46px;height:40px;display:grid;place-items:center"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M0 5h10" stroke="currentColor" stroke-width="1"/></svg></div>
          <div style="width:46px;height:40px;display:grid;place-items:center"><svg width="10" height="10" viewBox="0 0 10 10"><rect x="0.5" y="0.5" width="9" height="9" rx="1.5" fill="none" stroke="currentColor" stroke-width="1"/></svg></div>
          <div style="width:46px;height:40px;display:grid;place-items:center"><svg width="10" height="10" viewBox="0 0 10 10"><path d="M0.5 0.5l9 9M9.5 0.5l-9 9" stroke="currentColor" stroke-width="1"/></svg></div>
        </div>
      </div>
      <div style="position:absolute;left:0;top:40px;width:1440px;height:48px;display:flex;align-items:center;gap:4px;padding:0 12px;box-sizing:border-box">
        <div class="sb">${IC(I.file)}File${IC(I.chev, 12)}</div>
        <div class="sb">${IC(I.plus)}Add photos</div>
        <div data-r="newProj" class="sb">${IC(I.newp)}New project</div>
        <div style="flex:1"></div>
        <div class="ib" style="margin-right:4px">${IC(I.gear)}</div>
        <div data-r="exportBtn" class="acc">${IC(I.up)}<span data-r="exportLabel">Export video</span></div>
      </div>
    </div>
    <div data-r="body" style="position:absolute;left:0;top:88px;width:1440px;height:812px">
      <div style="position:absolute;inset:0;background:var(--layer);border-top:1px solid var(--divider);border-radius:0 0 8px 8px;box-sizing:border-box"></div>
      <div data-r="first" style="position:absolute;left:16px;top:8px;width:1408px;height:788px;box-sizing:border-box;border-radius:8px;border:1.5px dashed var(--dropStroke);display:grid;grid-template-columns:minmax(0,0.85fr) minmax(0,1.15fr);gap:56px;align-items:center;padding:0 64px">
        <div data-r="dropBg" style="position:absolute;inset:-1.5px;border-radius:8px;background:var(--accentTint);border:1.5px solid var(--indicator);opacity:0"></div>
        <div style="position:relative;display:flex;flex-direction:column;gap:20px;max-width:430px">
          <div style="font-family:var(--disp);font-size:28px;line-height:36px;font-weight:600">Drop a folder of photos<br>to make a timelapse</div>
          <div style="color:var(--text2);max-width:46ch">10Lapse plays your photos back as a video. Shoot one photo every few seconds, and 300 of them become a 10-second clip at 30 frames per second.</div>
          <div style="display:flex;gap:8px;padding-top:4px"><div class="acc">${IC(I.folder)}Choose folder</div><div class="std">${IC(I.photos)}Choose photos</div></div>
          <div class="cap" style="display:flex;gap:8px;align-items:center">${IC(I.lock, 14)}<span>Your photos never leave this PC.</span></div>
        </div>
        <div style="position:relative;display:flex;flex-direction:column;gap:10px">
          <div style="position:relative;aspect-ratio:16/9;border-radius:8px;overflow:hidden;box-shadow:var(--flyout)"><canvas data-r="dayCv" width="1100" height="619" style="width:100%;height:100%;display:block"></canvas></div>
          <div data-r="dayThumbs" style="display:grid;grid-template-columns:repeat(12,minmax(0,1fr));gap:3px"></div>
          <div class="cap tab" style="display:flex;justify-content:space-between"><span>Frame 0363 of 0600</span><span>00:12.06 / 00:20.00</span></div>
        </div>
      </div>
      <div data-r="stage" style="position:absolute;left:0;top:1px;width:1120px;height:811px;opacity:0">
        <div data-r="gate" style="position:absolute;left:20px;top:20px;width:1080px;height:607.5px;background:#0C0C0D;border-radius:6px;overflow:hidden">
          <canvas data-r="gateCv" width="1080" height="608" style="position:absolute;left:0;top:0;width:1080px;height:607.5px"></canvas>
        </div>
        <div data-r="transport" style="position:absolute;left:20px;top:639.5px;width:1080px;height:36px;display:flex;align-items:center;gap:14px">
          <div style="width:36px;height:36px;flex:none;border-radius:50%;border:1px solid var(--stroke);background:var(--ctrl);display:grid;place-items:center;box-sizing:border-box">${IC(I.play, 14)}</div>
          <div style="position:relative;flex:1;height:20px">
            <div style="position:absolute;left:0;right:0;top:8px;height:4px;border-radius:2px;background:var(--track)"></div>
            <div data-r="scrubFill" style="position:absolute;left:0;top:8px;height:4px;width:0;border-radius:2px;background:var(--indicator)"></div>
            <div data-r="knob" style="position:absolute;top:0;left:0;width:20px;height:20px;margin-left:-10px;border-radius:50%;background:var(--ctrl);box-shadow:0 0 0 1px var(--stroke),0 1px 2px rgba(0,0,0,0.2);display:grid;place-items:center"><div style="width:12px;height:12px;border-radius:50%;background:var(--indicator)"></div></div>
          </div>
          <span data-r="tc" class="cap tab" style="white-space:nowrap">0001 / 1440&nbsp;&nbsp;&nbsp;00:00.00 / 00:48.00</span>
        </div>
        <div data-r="strip" style="position:absolute;left:20px;top:687.5px;width:1080px;height:40px"></div>
        <div class="cap tab" style="position:absolute;left:20px;top:739px;width:1080px;display:flex;justify-content:space-between"><span data-r="frameLabel">Frame 0001 of 1440</span><span data-r="fileLabel">IMG_0001.JPG · 17:42:00</span></div>
      </div>
      <div data-r="insp" style="position:absolute;left:1120px;top:1px;width:320px;height:811px;opacity:0">
        <div style="position:absolute;left:0;top:0;width:1px;height:811px;background:var(--divider)"></div>
        <div style="position:absolute;left:20px;top:0;width:280px">
          <div class="sec">
            <div class="h2">Timing</div>
            <div data-r="segModeH"></div>
            <div data-r="segValH"></div>
            <div style="display:flex;flex-direction:column;gap:2px;padding-top:4px">
              <div data-r="readout" style="font-family:var(--disp);font-size:20px;line-height:28px;font-weight:600;font-variant-numeric:tabular-nums;white-space:nowrap">0 photos</div>
              <div data-r="readSub" class="cap">at 30 fps</div>
            </div>
            <div style="display:flex;flex-direction:column;gap:4px">
              <div style="height:4px;border-radius:2px;background:var(--track);overflow:hidden"><div data-r="lenBar" style="height:4px;width:0;background:var(--text2)"></div></div>
              <div class="cap tab" style="display:flex;justify-content:space-between"><span>0 s</span><span>30 s</span><span>60 s</span></div>
            </div>
          </div>
          <div class="sec">
            <div class="h2">Frame</div>
            <div data-r="segShapeH"></div>
            <div data-r="segFitH"></div>
            <div class="cap">Fill crops the edges so the frame has no bars.</div>
          </div>
          <div class="sec" style="border-bottom:0">
            <div class="h2">Output</div>
            <div class="lbl">Resolution<div class="sel"><span>4K · 3840 × 2160</span>${IC(I.chev, 12)}</div></div>
            <div class="lbl">Format<div class="sel"><span>MP4 · H.264</span>${IC(I.chev, 12)}</div></div>
          </div>
        </div>
      </div>
    </div>`;
    const r = refs(win);
    r.win = win;
    r.segMode = segmented(r.segModeH, ['Per second', 'Length', 'Per photo'], 280);
    r.segVal = segmented(r.segValH, ['12', '24', '25', '30', '50', '60'], 280);
    r.segShape = segmented(r.segShapeH, ['Match', '16:9', '9:16', '1:1', '4:5', '4:3'], 280);
    r.segFit = segmented(r.segFitH, ['Fit', 'Fill'], 280);
    r.segMode.set(0, 0); r.segVal.set(3, 3); r.segShape.set(1, 1); r.segFit.set(1, 1);
    // filmstrip: 18 thumbnails at 3:2 with 4 px gaps
    r.thumbs = [];
    const tw = (1080 - 17 * 4) / 18;
    for (let i = 0; i < 18; i++) {
      const d = el('div', `position:absolute;left:${i * (tw + 4)}px;top:0;width:${tw}px;height:${tw / 1.5}px;border-radius:3px;overflow:hidden;opacity:0`, r.strip);
      const c = canvas(150, 100, 'width:100%;height:100%;display:block', d);
      c.getContext('2d').drawImage(TEX[Math.round(i / 17 * (TEX.length - 1))], 0, 0, 150, 100);
      r.thumbs.push(d);
    }
    r.ring = el('div', `position:absolute;left:-4px;top:-4px;width:${tw + 8}px;height:${tw / 1.5 + 8}px;border-radius:5px;box-sizing:border-box;border:2px solid var(--indicator);box-shadow:inset 0 0 0 2px var(--layer);opacity:0`, r.strip);
    r.tw = tw;
    for (let i = 0; i < 12; i++) {
      const d = el('div', 'aspect-ratio:3/2;border-radius:3px;overflow:hidden', r.dayThumbs);
      const c = canvas(90, 60, 'width:100%;height:100%;display:block', d);
      c.getContext('2d').drawImage(TEX[Math.round(i / 11 * (TEX.length - 1))], 0, 0, 90, 60);
      if (i === 7) d.style.boxShadow = '0 0 0 2px var(--layer), 0 0 0 4px var(--indicator)';
    }
    WORLD.draw(r.dayCv.getContext('2d'), 1100, 619, 0.6, V169, { small: true });
    return r;
  }

  // the Look section on its own, as the inspector shows it
  function lookCard(parent, opts) {
    const c = el('div', 'position:absolute;width:320px;padding:18px 20px 20px;box-sizing:border-box;border-radius:8px;background:var(--layer);box-shadow:var(--flyout),inset 0 0 0 1px var(--frame);color:var(--text);font-family:var(--txt);font-size:14px;line-height:20px;display:flex;flex-direction:column;gap:12px', parent);
    el('div', '', c, '<div class="h2">Look</div>');
    const rowA = el('div', 'display:flex;align-items:flex-start;justify-content:space-between;gap:12px', c);
    el('div', 'display:flex;flex-direction:column;gap:2px', rowA, `<span>${opts.label}</span><span class="cap">${opts.hint}</span>`);
    const tg = toggle(rowA);
    const val = el('div', 'display:flex;flex-direction:column;gap:6px', c);
    const lab = el('div', 'display:flex;justify-content:space-between', val, `<span class="cap">${opts.slider}</span><span data-v class="cap tab" style="color:var(--text)">${opts.value}</span>`);
    const sl = slider(val, 280);
    return { el: c, toggle: tg, slider: sl, value: lab.querySelector('[data-v]'), body: val };
  }

  return { LIGHT, DARK, theme, IC, I, ICON16, segmented, toggle, slider, buildWindow, lookCard };
})();
