#!/usr/bin/env node
/** Capture immutable S11 Demo screenshots from a detached Git worktree.
 * This script never edits the baseline and never opens a production database.
 * Usage: node scripts/capture-s12r-baseline.mjs <baseline-worktree> <evidence-dir>
 */
import {spawn} from 'node:child_process';
import {mkdir,writeFile} from 'node:fs/promises';
import {resolve,join} from 'node:path';
import {chromium} from '@playwright/test';

const baseline=resolve(process.argv[2]||'');
const target=resolve(process.argv[3]||'s12r-evidence');
if(!process.argv[2]||!process.argv[3])throw Error('Pass immutable baseline checkout and evidence directory');
await mkdir(target,{recursive:true});
const port=5183;
const child=spawn(process.platform==='win32'?'npm.cmd':'npm',
  ['run','dev:shadcn','--','--host','127.0.0.1','--port',String(port)],{
  cwd:baseline,shell:process.platform==='win32',stdio:'ignore'
});
let browser;
const captures=[];
async function ready(){
  for(let i=0;i<150;i++){
    if(child.exitCode!==null)throw Error('Immutable Demo dev server ended prematurely');
    try{const res=await fetch('http://127.0.0.1:'+port);if(res.ok)return;}
    catch{/* startup in progress */}
    await new Promise(r=>setTimeout(r,400));
  }
  throw Error('Immutable Demo dev server failed to launch');
}
try{
  await ready();
  browser=await chromium.launch();
  const page=await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
  await page.goto('http://127.0.0.1:'+port,{waitUntil:'networkidle'});
  await page.getByRole('heading',{name:/ROS/}).first().waitFor();
  async function capture(name){
    await page.screenshot({path:join(target,name+'.png'),animations:'disabled'});
    captures.push({name,viewport:'1440x900',deviceScaleFactor:1});
  }
  await capture('01-immutable-demo-home');
  const view=page.locator('.dm-view-switch');
  if(await view.count()){
    const list=view.locator('button[title="list"]');
    if(await list.count()){await list.first().click();await capture('02-immutable-demo-overview-list');}
    const table=view.locator('button[title="table"]');
    if(await table.count()){await table.first().click();await capture('03-immutable-demo-overview-table');}
  }
  await page.getByRole('button',{name:/分阶段/}).first().click();
  for(const [id,pattern] of [
    ['raw',/01_Raw/],['processed',/02_Processed/],
    ['statistics',/03_Statistics/],['display',/04_Display/]
  ]){
    const btn=page.getByRole('button',{name:pattern}).first();
    if(await btn.count()){await btn.click();await capture('04-immutable-demo-stage-'+id);}
  }
  const file=page.locator('[data-explorer-id]').first();
  if(await file.count()){
    await file.click({button:'right'});await capture('05-immutable-demo-explorer-menu');
    await page.keyboard.press('Escape');
  }
  await page.getByRole('button',{name:'论文管理'}).first().click();
  await capture('06-immutable-demo-paper');
  const panel=page.locator('.dm-paper-panel').first();
  if(await panel.count()){await panel.click();await capture('06-immutable-demo-panel-drawer');}
  await page.setViewportSize({width:1024,height:768});
  await capture('07-immutable-demo-narrow');
  await writeFile(join(target,'immutable-baseline-index.json'),JSON.stringify({
    sha:'e7b12d7022ed40d33bccc42f9a628b305e91ccf9',
    repo:'Devil-Jian/data-m',
    source:'detached immutable SHA worktree (UI comparison Demo)',
    captures
  },null,2));
}finally{
  await browser?.close();
  child.kill();
}
