import fs from 'node:fs';
import path from 'node:path';
import dotenv from 'dotenv';

export function loadEnvironment() {
  const cwd = process.cwd();
  const environmentName = process.env.APP_ENV || process.env.NODE_ENV || 'development';
  const candidates = [
    '.env',
    `.env.${environmentName}`,
    '.env.local',
    `.env.${environmentName}.local`
  ];

  for (const fileName of candidates) {
    const fullPath = path.join(cwd, fileName);
    if (fs.existsSync(fullPath)) {
      dotenv.config({
        path: fullPath,
        override: true
      });
    }
  }

  return environmentName;
}
