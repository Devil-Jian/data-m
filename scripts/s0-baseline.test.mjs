import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
const read = path => readFileSync(new URL('../'+path,import.meta.url),'utf8');

test('S0 shell retains read-only drop/status plus authorized S1/S2 metadata IPC, with no DB startup',()=>{
  const shell=read('apps/desktop/src-tauri/src/lib.rs');
  const handlers=shell.match(/generate_handler!\[([\s\S]*?)\]/)?.[1];
  assert.ok(handlers);
  assert.deepEqual(handlers.split(',').map(x=>x.trim()).filter(Boolean),
    ['shell_status','drop_preview::inspect_drop_paths','project_tree::project_tree','experiment_tree::experiment_tree']);
  const run=shell.slice(shell.indexOf('pub fn run()'),shell.indexOf('#[cfg(test)]'));
  assert.doesNotMatch(run,/Connection::|domain_db::|cas::|\.setup\(/);
});

test('S0 both renderer entries consume the canonical UI and styles',()=>{
  for(const entry of ['apps/desktop/src/main.tsx','demos/shadcn/src/main.tsx']){
    const source=read(entry);
    assert.match(source,/@data-m\/ui\/workbench-ui/);
  }
  assert.match(read('apps/desktop/src/main.tsx'),/@data-m\/ui\/style.css/);
  assert.match(read('demos/shadcn/src/style.css'),/@data-m\/ui\/style.css/);
  for(const name of ['button','checkbox','input','select','badge']){
    assert.equal(existsSync(new URL(`../demos/shadcn/src/components/ui/${name}.tsx`,import.meta.url)),false);
  }
  const config=JSON.parse(read('apps/desktop/src-tauri/tauri.conf.json'));
  assert.equal(config.build.devUrl,'http://127.0.0.1:5176');
  assert.equal(config.build.frontendDist,'../dist');
  assert.equal(config.bundle.active,false);
});

test('S0 CI installs locked dependencies and guards S0 test discovery',()=>{
  assert.ok(existsSync(new URL('../package-lock.json',import.meta.url)));
  const workflow=read('.github/workflows/desktop-windows.yml');
  assert.match(workflow,/npm ci/);
  assert.match(workflow,/test:s0/);
  assert.match(workflow,/cargo test --locked --no-default-features s0_/);
  assert.match(workflow,/S0 Rust suite must contain tests/);
});
