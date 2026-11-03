// Generates the PWA icons (Feature 153 T17) from the brand artwork in docs/Logo.png.
// Run: node scripts/generate-pwa-icons.mjs   (outputs are committed to public/icons/)
//
// The source is a 1536×1024 render of the app tile (navy rounded square, teal mark, gold star) on black.
// "any" icons crop the inside of the tile, so they are full-bleed navy with the mark centered.
// The bars are transparent cut-outs, so every crop is flattened onto the tile color.
// The maskable icon shrinks the crop into the central safe zone (a circle of 80 % diameter) and pads it by
// repeating its edge pixels, so launchers that mask to circles or squircles never clip the mark.
import { mkdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const frontendRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.resolve(frontendRoot, "../../docs/Logo.png");
const outputDir = path.resolve(frontendRoot, "public/icons");

// Square region inside the rounded tile, clear of its corners and glow (source pixels).
const TILE_CROP = { left: 530, top: 222, width: 476, height: 476 };
// Fraction of the maskable canvas the tile crop occupies, so the mark stays inside the 80 % safe circle.
const MASKABLE_SCALE = 0.82;

async function tileCrop(size, background) {
  return sharp(source)
    .extract(TILE_CROP)
    .flatten({ background })
    .resize(size, size, { fit: "cover" })
    .png()
    .toBuffer();
}

async function tileColor() {
  const { data } = await sharp(source)
    .extract({
      left: TILE_CROP.left + 8,
      top: TILE_CROP.top + TILE_CROP.height - 24,
      width: 16,
      height: 16,
    })
    .resize(1, 1)
    .raw()
    .toBuffer({ resolveWithObject: true });
  return { r: data[0], g: data[1], b: data[2], alpha: 1 };
}

async function main() {
  await mkdir(outputDir, { recursive: true });
  const background = await tileColor();
  for (const [name, size] of [
    ["icon-192.png", 192],
    ["icon-512.png", 512],
    ["apple-touch-icon.png", 180],
  ]) {
    await sharp(await tileCrop(size, background)).toFile(path.join(outputDir, name));
  }

  const canvas = 512;
  const inner = Math.round(canvas * MASKABLE_SCALE);
  const before = Math.floor((canvas - inner) / 2);
  const after = canvas - inner - before;
  await sharp(await tileCrop(inner, background))
    .extend({ top: before, bottom: after, left: before, right: after, extendWith: "copy" })
    .png()
    .toFile(path.join(outputDir, "icon-maskable-512.png"));

  console.log(`PWA icons written to ${path.relative(frontendRoot, outputDir)}`);
}

await main();
