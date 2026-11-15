import {defineConfig,devices} from '@playwright/test';

// Test the exact renderer used by the Windows EXE without compiling Rust.
export default defineConfig({
  testDir:'./tests/desktop-ui',
  timeout:30_000,
  expect:{timeout:5_000},
  retries:process.env.CI?1:0,
  use:{
    baseURL:'http://127.0.0.1:5176',
    trace:'retain-on-failure',
    ...devices['Desktop Chrome']
  },
  webServer:{
    command:'npm run preview:desktop',
    url:'http://127.0.0.1:5176',
    reuseExistingServer:!process.env.CI,
    timeout:90_000
  }
});
