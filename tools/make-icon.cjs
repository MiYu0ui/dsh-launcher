/*
 * make-icon.cjs — 由 DSH 官方 favicon.svg（DeepSeek 鲸鱼）生成启动器图标资源。
 *
 * 产物：
 *   assets/app.ico        多尺寸应用图标（暖白底 + 近黑鲸鱼 + 暖杏金节点），用于 exe / 快捷方式 / 托盘
 *   assets/logo.png       近黑鲸鱼（透明底），用于亮色标题栏
 *   assets/logo-amber.png 暖褐鲸鱼（透明底），备用强调
 *
 * 配色对齐 RhineLabUI 的设计基准：暖灰白底 #eae5e1、近黑 #080a08、暖杏金 #c5a16b、暖褐 #9b7247。
 * 仅供构建期使用（依赖 npx 缓存里的 sharp）；产物已生成后构建脚本不会重复调用。
 */
'use strict';

const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const ASSETS = path.join(ROOT, 'assets');

// DSH 官方图标（@deepseek-ai/dsh-web-frontend/dist/favicon.svg）中的鲸鱼路径
const WHALE_PATH =
  'M48.8354 10.0479C48.3232 9.79199 48.1025 10.2798 47.8032 10.5278C47.7007 10.6079 47.6143 10.7119 ' +
  '47.5273 10.8076C46.7793 11.624 45.9048 12.1597 44.7622 12.0957C43.0923 12 41.666 12.5356 40.4058 ' +
  '13.8398C40.1377 12.2319 39.2476 11.272 37.8926 10.6558C37.1836 10.3359 36.4668 10.0156 35.9702 ' +
  '9.31982C35.6235 8.82373 35.5293 8.27197 35.356 7.72754C35.2456 7.3999 35.1353 7.06396 34.7651 ' +
  '7.00781C34.3633 6.94385 34.2056 7.2876 34.0479 7.57568C33.418 8.75195 33.1733 10.0479 33.1973 ' +
  '11.3599C33.2524 14.312 34.4736 16.6641 36.8999 18.3359C37.1758 18.5278 37.2466 18.7197 37.1597 ' +
  '19C36.9946 19.5757 36.7974 20.1357 36.624 20.7119C36.5137 21.0801 36.3486 21.1597 35.9624 21C34.6309 ' +
  '20.4321 33.481 19.5918 32.4644 18.5757C30.7393 16.8721 29.1792 14.9917 27.2334 13.52C26.7764 ' +
  '13.1758 26.3193 12.856 25.8467 12.5518C23.8618 10.584 26.1069 8.96777 26.627 8.77588C27.1704 ' +
  '8.57568 26.8159 7.8877 25.0591 7.896C23.3022 7.90381 21.6953 8.50391 19.647 9.30371C19.3477 ' +
  '9.42383 19.0322 9.51172 18.7095 9.58398C16.8501 9.22363 14.9199 9.14355 12.9033 9.37598C9.10596 ' +
  '9.80762 6.07275 11.6396 3.84326 14.7681C1.16455 18.5278 0.53418 22.7998 1.30664 27.2559C2.11768 ' +
  '31.9521 4.46582 35.8398 8.07373 38.8799C11.8159 42.0322 16.1255 43.5762 21.041 43.2803C24.0269 ' +
  '43.104 27.3516 42.6963 31.1016 39.4561C32.0469 39.936 33.0396 40.1279 34.686 40.272C35.9546 ' +
  '40.3921 37.1758 40.208 38.1211 40.0078C39.6021 39.688 39.4995 38.2881 38.9639 38.0322C34.623 ' +
  '35.9678 35.5762 36.8081 34.71 36.1279C36.9155 33.4639 40.2402 30.6958 41.54 21.728C41.6426 ' +
  '21.0161 41.5557 20.5679 41.54 19.9917C41.5322 19.6396 41.6108 19.5039 42.0049 19.4639C43.0923 ' +
  '19.3359 44.1479 19.0317 45.1167 18.4878C47.9292 16.9199 49.064 14.3438 49.3315 11.2559C49.3711 ' +
  '10.7837 49.3237 10.2959 48.8354 10.0479ZM24.3262 37.8398C20.1196 34.4639 18.0791 33.3521 17.2358 ' +
  '33.3999C16.4482 33.4482 16.5898 34.3682 16.7632 34.9678C16.9443 35.5601 17.1812 35.9683 17.5117 ' +
  '36.4878C17.7402 36.832 17.8979 37.3442 17.2832 37.728C15.9282 38.584 13.5728 37.4399 13.4624 ' +
  '37.3838C10.7207 35.7358 8.42822 33.5601 6.81348 30.584C5.25342 27.7197 4.34766 24.6479 4.19775 ' +
  '21.3677C4.1582 20.5757 4.38672 20.2959 5.15869 20.1519C6.17529 19.96 7.22314 19.9199 8.23926 ' +
  '20.0718C12.5327 20.7119 16.1885 22.6719 19.2529 25.7759C21.002 27.5439 22.3252 29.6558 23.6885 ' +
  '31.7202C25.1377 33.9121 26.6978 36 28.6831 37.7119C29.3843 38.312 29.9434 38.7681 30.479 ' +
  '39.104C28.8643 39.2881 26.1699 39.3281 24.3262 37.8398ZM26.3433 24.6001C26.3433 24.248 26.6191 ' +
  '23.9678 26.9658 23.9678C27.0444 23.9678 27.1152 23.9839 27.1782 24.0078C27.2651 24.04 27.3438 ' +
  '24.0879 27.4067 24.1602C27.5171 24.272 27.5801 24.4321 27.5801 24.6001C27.5801 24.9521 27.3042 ' +
  '25.2319 26.9575 25.2319C26.6108 25.2319 26.3433 24.9521 26.3433 24.6001ZM32.6064 27.8799C32.2046 ' +
  '28.0479 31.8027 28.1919 31.4165 28.208C30.8179 28.2397 30.1641 27.9922 29.8096 27.688C29.2583 ' +
  '27.2158 28.8643 26.9521 28.6987 26.1279C28.6279 25.7759 28.6675 25.2319 28.7305 24.9199C28.8721 ' +
  '24.248 28.7144 23.8159 28.2495 23.4238C27.8716 23.104 27.3911 23.0161 26.8633 23.0161C26.666 ' +
  '23.0161 26.4849 22.9277 26.3511 22.856C26.1304 22.7441 25.9492 22.4639 26.1226 22.1201C26.1777 ' +
  '22.0078 26.4458 21.7358 26.5088 21.688C27.2256 21.272 28.0527 21.4077 28.8169 21.7197C29.5259 ' +
  '22.0161 30.0615 22.5601 30.834 23.3281C31.6216 24.2559 31.7632 24.5117 32.2124 25.208C32.5669 ' +
  '25.752 32.8901 26.312 33.1104 26.9521C33.2446 27.3521 33.0713 27.6802 32.6064 27.8799Z';

// 鲸鱼在 50x50 viewBox 中的实际包围盒中心，用于精确居中
const BBOX_CX = 24.93;
const BBOX_CY = 25.3;

const TILE_A = '#F7F4F1';   // 暖白
const TILE_B = '#DED8D2';   // 暖灰
const INK = '#080A08';      // 近黑
const AMBER = '#C5A16B';    // 暖杏金信号

function tileSvg(size) {
  const s = (size * 0.62) / 48.8;            // 鲸鱼宽度占画布 62%
  const rx = size * 0.075;                   // 近乎直角的轻微圆角
  const inset = size * 0.06;
  const bw = Math.max(1, size * 0.013);      // 细边框
  const node = size * 0.05;                  // 琥珀节点（对应授权环上的节点）
  const nodeCx = size - inset - node * 1.9;
  const nodeCy = inset + node * 1.9;
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" width="' + size + '" height="' + size + '" viewBox="0 0 ' + size + ' ' + size + '">' +
    '<defs><linearGradient id="g" x1="0" y1="0" x2="0.55" y2="1">' +
    '<stop offset="0" stop-color="' + TILE_A + '"/><stop offset="1" stop-color="' + TILE_B + '"/>' +
    '</linearGradient></defs>' +
    '<rect x="0" y="0" width="' + size + '" height="' + size + '" rx="' + rx.toFixed(2) + '" ry="' + rx.toFixed(2) + '" fill="url(#g)"/>' +
    '<rect x="' + inset.toFixed(2) + '" y="' + inset.toFixed(2) + '" width="' + (size - inset * 2).toFixed(2) +
    '" height="' + (size - inset * 2).toFixed(2) + '" rx="' + (rx * 0.5).toFixed(2) + '" fill="none" stroke="' + INK +
    '" stroke-width="' + bw.toFixed(2) + '" opacity="0.5"/>' +
    '<g transform="translate(' + (size / 2) + ',' + (size / 2) + ') scale(' + s.toFixed(5) + ') translate(' + (-BBOX_CX) + ',' + (-BBOX_CY) + ')">' +
    '<path d="' + WHALE_PATH + '" fill="' + INK + '"/>' +
    '</g>' +
    '<circle cx="' + nodeCx.toFixed(2) + '" cy="' + nodeCy.toFixed(2) + '" r="' + node.toFixed(2) + '" fill="' + AMBER + '"/>' +
    '</svg>'
  );
}

function whaleSvg(color) {
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="-2.2 4.2 54.4 43.4">' +
    '<path d="' + WHALE_PATH + '" fill="' + color + '"/></svg>'
  );
}

/** 打包多尺寸 ICO：全部使用 32bpp 未压缩 DIB，保证 .NET Framework 的 Icon 类可读。 */
function buildIco(frames) {
  const entries = [];
  let offset = 6 + frames.length * 16;
  const blobs = [];
  for (const f of frames) {
    const { size, rgba } = f;
    const xorStride = size * 4;
    const andStride = Math.ceil(size / 32) * 4;
    const dib = Buffer.alloc(40 + xorStride * size + andStride * size);
    dib.writeUInt32LE(40, 0);
    dib.writeInt32LE(size, 4);
    dib.writeInt32LE(size * 2, 8); // XOR + AND 掩码，高度翻倍
    dib.writeUInt16LE(1, 12);
    dib.writeUInt16LE(32, 14);
    dib.writeUInt32LE(0, 16); // BI_RGB
    dib.writeUInt32LE(xorStride * size, 20);
    let p = 40;
    for (let y = size - 1; y >= 0; y--) {
      for (let x = 0; x < size; x++) {
        const i = (y * size + x) * 4;
        dib[p++] = rgba[i + 2]; // B
        dib[p++] = rgba[i + 1]; // G
        dib[p++] = rgba[i];     // R
        dib[p++] = rgba[i + 3]; // A
      }
    }
    // AND 掩码全 0：不透明区域交由 alpha 通道决定
    p += andStride * size;
    entries.push({ size, bytes: dib });
    blobs.push(dib);
  }
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(frames.length, 4);
  const dir = Buffer.alloc(frames.length * 16);
  for (let i = 0; i < frames.length; i++) {
    const size = frames[i].size;
    const o = i * 16;
    dir.writeUInt8(size >= 256 ? 0 : size, o);
    dir.writeUInt8(size >= 256 ? 0 : size, o + 1);
    dir.writeUInt8(0, o + 2);
    dir.writeUInt8(0, o + 3);
    dir.writeUInt16LE(1, o + 4);
    dir.writeUInt16LE(32, o + 6);
    dir.writeUInt32LE(blobs[i].length, o + 8);
    dir.writeUInt32LE(offset, o + 12);
    offset += blobs[i].length;
  }
  return Buffer.concat([header, dir].concat(blobs));
}

/**
 * 莱茵生命（Rhine Lab）官方标志。
 * 路径逐字取自 RhineLabUI 的 src/brand.ts（labelMarkSvg / logo 共用同一组路径）：
 * 双弧 + 左圆内的加号与右圆的短横，纯 stroke 绘制。
 */
const RHINE_MARK_PATHS =
  '<path d="M156 75C127 48 103 15 70 15C37 15 15 39 15 70S38 128 70 128C103 128 127 96 176 52' +
  'M155 75C182 99 208 128 240 128C273 128 295 105 295 73S273 15 240 15C221 15 207 23 192 38" ' +
  'fill="none" stroke="COLOR" stroke-width="26"/>' +
  '<path d="M44 70h50M69 45v50M219 70h44" fill="none" stroke="COLOR" stroke-width="15"/>';

function rhineMarkSvg(color, width) {
  // 路径包围盒 15..295 / 15..128，线宽 26 → 描边外扩 13，故取 viewBox 1..309 / 1..141
  const height = (width * 140) / 308;
  return (
    '<svg xmlns="http://www.w3.org/2000/svg" width="' + Math.round(width) + '" height="' + Math.round(height) +
    '" viewBox="1 1 308 140">' + RHINE_MARK_PATHS.split('COLOR').join(color) + '</svg>'
  );
}

async function main() {
  const sharpPath = process.env.SHARP_PATH || 'sharp';
  const sharp = require(sharpPath);
  fs.mkdirSync(ASSETS, { recursive: true });

  const SIZES = [16, 20, 24, 32, 40, 48, 64, 128];
  const frames = [];
  for (const size of SIZES) {
    const svg = Buffer.from(tileSvg(size));
    const rgba = await sharp(svg).resize(size, size).ensureAlpha().raw().toBuffer();
    frames.push({ size, rgba });
  }
  const ico = buildIco(frames);
  fs.writeFileSync(path.join(ASSETS, 'app.ico'), ico);

  fs.writeFileSync(path.join(ASSETS, 'logo.png'), await sharp(Buffer.from(whaleSvg(INK))).png().toBuffer());
  fs.writeFileSync(path.join(ASSETS, 'logo-amber.png'), await sharp(Buffer.from(whaleSvg('#9B7247'))).png().toBuffer());
  // 莱茵生命官方标志（近黑，站点要求"整个标志保持黑色"）
  fs.writeFileSync(path.join(ASSETS, 'rhinelab-mark.png'),
    await sharp(Buffer.from(rhineMarkSvg(INK, 1232))).png().toBuffer());
  fs.writeFileSync(path.join(ASSETS, 'rhinelab-mark-preview.png'),
    await sharp(Buffer.from(rhineMarkSvg(INK, 616))).png().toBuffer());
  // 预览图（人工核对用，不参与打包）
  fs.writeFileSync(path.join(ASSETS, 'preview.png'), await sharp(Buffer.from(tileSvg(256))).png().toBuffer());

  console.log('app.ico         ' + ico.length + ' bytes  (' + SIZES.join('/') + ')');
  console.log('logo.png        近黑鲸鱼 ok');
  console.log('logo-amber.png  暖褐鲸鱼 ok');
  console.log('rhinelab-mark.png  莱茵生命标志 ok');
  console.log('preview.png     ok');
}

main().catch((e) => {
  console.error('ICON_BUILD_FAILED: ' + (e && e.message));
  process.exit(1);
});
