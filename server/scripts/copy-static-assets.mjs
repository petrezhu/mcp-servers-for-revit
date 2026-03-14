import fs from "fs";
import path from "path";

const rootDir = path.resolve(import.meta.dirname, "..");
const sourceDir = path.join(rootDir, "src", "data");
const targetDir = path.join(rootDir, "build", "data");

if (!fs.existsSync(sourceDir)) {
  console.warn(`Static asset source not found: ${sourceDir}`);
  process.exit(0);
}

fs.mkdirSync(targetDir, { recursive: true });
fs.cpSync(sourceDir, targetDir, { recursive: true });

console.log(`Copied static assets: ${sourceDir} -> ${targetDir}`);
