// Renders reel.html frame by frame in headless Chromium.
//   node render.mjs stills <outDir> <t1,t2,...>     one PNG per time, single sample
//   node render.mjs video <fps> [from] [to]         writes frames to stdout for accum.py:
//                                                   per frame: u32 sample count, then per sample u32 length + PNG bytes
// PLAYWRIGHT_CORE: a playwright-core package folder; CHROME: a Chromium or chrome-headless-shell executable.
import { createRequire } from 'module';
import { pathToFileURL } from 'url';
import fs from 'fs';
import path from 'path';

const require = createRequire(import.meta.url);
const { chromium } = require(process.env.PLAYWRIGHT_CORE || 'playwright-core');
const exe = process.env.CHROME || undefined;
const here = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));

const [mode, a1, a2, a3] = process.argv.slice(2);
const browser = await chromium.launch({ executablePath: exe, args: ['--force-color-profile=srgb', '--hide-scrollbars', '--disable-gpu-vsync'] });
try {
  const page = await browser.newPage({ viewport: { width: 1920, height: 1080 }, deviceScaleFactor: 1 });
  page.on('pageerror', e => process.stderr.write('PAGE ERROR ' + e.message + '\n'));
  page.on('console', m => { if (m.type() === 'error' && !/ERR_FILE_NOT_FOUND/.test(m.text())) process.stderr.write('CONSOLE ' + m.text() + '\n'); });
  await page.goto(pathToFileURL(path.join(here, 'reel.html')).href);
  await page.waitForFunction(() => window.ready === true, null, { timeout: 180000 });
  const cdp = await page.context().newCDPSession(page);
  const shot = async t => {
    await page.evaluate(tt => window.renderAt(tt), t);
    const r = await cdp.send('Page.captureScreenshot', { format: 'png', fromSurface: true, optimizeForSpeed: true });
    return Buffer.from(r.data, 'base64');
  };
  if (mode === 'stills') {
    fs.mkdirSync(a1, { recursive: true });
    for (const s of a2.split(',')) {
      const t = parseFloat(s);
      fs.writeFileSync(path.join(a1, `t${t.toFixed(3).padStart(7, '0')}.png`), await shot(t));
    }
  } else if (mode === 'video') {
    const fps = parseInt(a1, 10);
    const dur = await page.evaluate(() => window.DURATION);
    const from = a2 ? parseFloat(a2) : 0, to = a3 ? parseFloat(a3) : dur;
    const cuts = await page.evaluate(() => window.CUTS);
    const shutter = 0.5 / fps;
    const out = process.stdout;
    const write = buf => new Promise(res => { if (out.write(buf)) res(); else out.once('drain', res); });
    const n0 = Math.round(from * fps), n1 = Math.round(to * fps);
    const t0 = Date.now();
    for (let f = n0; f < n1; f++) {
      const t = f / fps;
      const n = await page.evaluate(tt => window.samplesAt(tt), t);
      let lo = t - shutter / 2, hi = t + shutter / 2;
      for (const c of cuts) {
        if (c > lo && c <= hi) { if (t >= c) { lo = c; hi = c + shutter; } else { hi = c - 1e-6; lo = c - shutter; } }
      }
      lo = Math.max(0, lo); hi = Math.min(dur - 1e-6, hi);
      const head = Buffer.alloc(4); head.writeUInt32LE(n, 0);
      await write(head);
      for (let k = 0; k < n; k++) {
        const ts = n === 1 ? t : lo + (k + 0.5) / n * (hi - lo);
        const png = await shot(ts);
        const len = Buffer.alloc(4); len.writeUInt32LE(png.length, 0);
        await write(len); await write(png);
      }
      if (f % 30 === 0) process.stderr.write(`frame ${f}/${n1} t=${t.toFixed(2)} samples=${n} elapsed=${((Date.now() - t0) / 1000).toFixed(0)}s\n`);
    }
  }
} finally {
  await browser.close();
}
