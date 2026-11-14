import {defineConfig,devices} from '@playwright/test';

export default defineConfig({
  testDir:'./tests/e2e',
  timeout:30_000,
  expect:{timeout:5_000},
  retries:process.env.CI?1:0,
  use:{
    baseURL:'http://127.0.0.1:5173',
    trace:'retain-on-failure',
    ...devices['Desktop Chrome']
  },
  webServer:{
    command:'npm run dev:shadcn',
    url:'http://127.0.0.1:5173',
    reuseExistingServer:!process.env.CI,
    timeout:90_000
  }
});
