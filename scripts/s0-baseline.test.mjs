import {test} from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync,existsSync} from 'node:fs';
const read = path => readFileSync(new URL('../'+path,import.meta.url),'utf8');

test('S0 shell retains authorized metadata/import IPC without storage work at startup',()=>{
  const shell=read('apps/desktop/src-tauri/src/lib.rs');
  const handlers=shell.match(/generate_handler!\[([\s\S]*?)\]/)?.[1];
  assert.ok(handlers);
  const registered = handlers.split(',').map(x=>x.trim()).filter(Boolean);
  const baseline = ['shell_status','drop_preview::inspect_drop_paths','project_tree::project_tree','experiment_tree::experiment_tree','import_jobs::import_jobs'];
  for (const command of baseline) {
    assert.equal(registered.filter(x=>x===command).length,1,`baseline IPC missing or duplicated: ${command}`);
  }
  const s4 = ['asset_browser::list_assets','asset_browser::get_asset'];
  for (const command of s4) {
    assert.equal(registered.filter(x=>x===command).length,1,`S4 IPC missing or duplicated: ${command}`);
  }
  const s5 = ['asset_transfer::asset_transfer'];
  for (const command of s5) {
    assert.equal(registered.filter(x=>x===command).length,1,`S5 audited transfer IPC missing or duplicated: ${command}`);
  }
  const s6 = ['asset_lifecycle::asset_lifecycle','asset_recycle::list_recycled_assets'];
  for (const command of s6) {
    assert.equal(registered.filter(x=>x===command).length,1,`S6 audited lifecycle IPC missing or duplicated: ${command}`);
  }
  const s7 = ['repository_safety::repository_safety'];
  for (const command of s7) {
    assert.equal(registered.filter(x=>x===command).length,1,`S7 audited snapshot IPC missing or duplicated: ${command}`);
  }
  const s8 = ['statistical_analysis::statistical_analysis'];
  for (const command of s8) {
    assert.equal(registered.filter(x=>x===command).length,1,`S8 audited analysis IPC missing or duplicated: ${command}`);
  }
  const s9 = ['paper_workspace::paper_workspace'];
  for (const command of s9) {
    assert.equal(registered.filter(x=>x===command).length,1,`S9 paper workspace IPC missing or duplicated: ${command}`);
  }
  const s10 = ['provenance_trace::provenance_trace','figure_export::figure_export','figure_export::figure_export_cancel'];
  for (const command of s10) assert.equal(registered.filter(x=>x===command).length,1,'S10 provenance IPC missing or duplicated');
  assert.deepEqual([...registered].sort(),[...baseline,...s4,...s5,...s6,...s7,...s8,...s9,...s10].sort(),'unexpected IPC command registered');
  const start=shell.indexOf('pub fn run()');
  const end=shell.indexOf('#[cfg(test)]',start);
  const run=shell.slice(start,end<0?undefined:end);
  assert.match(run,/tauri::Builder::default/,'startup check must inspect the actual builder');
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
  // S11 deliberately enables an installer. Keep the S0 non-GUI configuration
  // contract strict while explicitly checking the only authorized bundle type.
  assert.equal(config.bundle.active,true);
  assert.deepEqual(config.bundle.targets,['nsis']);
  assert.equal(config.bundle.windows?.nsis?.installMode,'currentUser');
  assert.equal(config.identifier,'com.deviljian.datam');
  assert.equal(config.version,'0.1.0');
});

test('S0 CI installs locked dependencies and guards S0 test discovery',()=>{
  assert.ok(existsSync(new URL('../package-lock.json',import.meta.url)));
  const workflow=read('.github/workflows/desktop-windows.yml');
  assert.match(workflow,/npm ci/);
  assert.match(workflow,/test:s0/);
  assert.match(workflow,/cargo test --locked --no-default-features s0_/);
  assert.match(workflow,/S0 Rust suite must contain tests/);
});
