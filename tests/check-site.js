import fs from 'node:fs';
import path from 'node:path';
import assert from 'node:assert/strict';

console.log('Testing Astro website build outputs...');

const root = process.cwd();
const vercelOutput = path.join(root, '.vercel', 'output');
const staticDir = path.join(vercelOutput, 'static');
const configFile = path.join(vercelOutput, 'config.json');
const indexHtml = path.join(staticDir, 'index.html');
const notFoundHtml = path.join(staticDir, '404.html');
const setupExe = path.join(staticDir, 'downloads', 'YellySetup.exe');
const portableExe = path.join(staticDir, 'downloads', 'Yelly.exe');

// 1. Verify Vercel output folder and config
assert.ok(fs.existsSync(vercelOutput), '.vercel/output directory must exist');
assert.ok(fs.existsSync(configFile), '.vercel/output/config.json must exist');

// 2. Verify static pages
assert.ok(fs.existsSync(indexHtml), 'static/index.html must exist');
assert.ok(fs.existsSync(notFoundHtml), 'static/404.html must exist');

const htmlContent = fs.readFileSync(indexHtml, 'utf-8');
assert.ok(htmlContent.includes('yelly'), 'index.html must include branding "yelly"');
assert.ok(htmlContent.includes('/downloads/YellySetup.exe'), 'index.html must contain download installer link');
assert.ok(htmlContent.includes('/downloads/Yelly.exe'), 'index.html must contain portable exe link');
assert.ok(htmlContent.includes('CoolMode'), 'index.html must mention CoolMode');

// 3. Verify downloads in public/static
assert.ok(fs.existsSync(setupExe), 'static/downloads/YellySetup.exe must exist');
assert.ok(fs.existsSync(portableExe), 'static/downloads/Yelly.exe must exist');

console.log('✓ All website build assertions passed successfully.');
