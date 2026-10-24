// The timeline: render.mjs drives the page through these.
'use strict';

// ================================================================ timeline
window.CUTS = [];
window.renderAt = function (t) {
  for (const s of SCENES) {
    const on = t >= s.t0 && t < s.t1;
    s.root.style.display = on ? '' : 'none';
    if (on) s.update(t);
  }
};
// motion blur: frames averaged over a 180-degree shutter; more samples where things move fast
const HEAVY = [[2.9, 3.8, 12], [6.2, 6.95, 10], [6.8, 7.6, 8], [7.9, 8.8, 8], [8.5, 9.6, 6], [12.5, 13.6, 10]].concat(window.HEAVY_MORE || []);
window.samplesAt = function (t) {
  let n = 3;
  for (const [a, b, k] of HEAVY) if (t >= a && t < b) n = Math.max(n, k);
  return n;
};
window.DURATION = DUR;
document.fonts.ready.then(() => { TITLE.layout(); window.ready = true; });
