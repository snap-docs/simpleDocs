import fs from 'node:fs';
import path from 'node:path';
import dotenv from 'dotenv';

export function loadEnvironment() {
  const cwd = process.cwd();
  const environmentName = process.env.APP_ENV || process.env.NODE_ENV || 'development';
  const inheritedKeys = new Set(Object.keys(process.env));
  const candidates = [
    '.env',
    `.env.${environmentName}`,
    '.env.local',
    `.env.${environmentName}.local`
  ];

  for (const fileName of candidates) {
    const fullPath = path.join(cwd, fileName);
    if (fs.existsSync(fullPath)) {
      const values = dotenv.parse(fs.readFileSync(fullPath));
      for (const [key, value] of Object.entries(values)) {
        if (!inheritedKeys.has(key)) process.env[key] = value;
      }
    }
  }

  return environmentName;
}
