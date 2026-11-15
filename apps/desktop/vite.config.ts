import {defineConfig} from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins:[react(),tailwindcss()],
  base:'./',
  clearScreen:false,
  server:{host:'127.0.0.1',port:5176,strictPort:true},
  envPrefix:['VITE_','TAURI_ENV_*'],
  build:{target:process.env.TAURI_ENV_PLATFORM==='windows'?'chrome105':'es2022'}
});
