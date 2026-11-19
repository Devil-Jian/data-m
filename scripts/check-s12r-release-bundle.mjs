#!/usr/bin/env node
/**
 * Release safety gate: the compiled desktop renderer must not contain Demo
 * research fixtures or the development-only LegacyDevelopmentHarness.
 * This is an independent source/packaging protection check, not proof that
 * all manual Windows UI validation has passed.
 */
import {existsSync,readdirSync,readFileSync,statSync} from 'node:fs';
import {resolve,join} from 'node:path';

const root=resolve(process.argv[2]||'apps/desktop/dist');
if(!existsSync(root))throw Error('S12R renderer release output is missing: '+root);
function jsFiles(dir){
  return readdirSync(dir).flatMap(name=>{
    const path=join(dir,name);
    return statSync(path).isDirectory()?jsFiles(path):name.endsWith('.js')?[path]:[];
  });
}
const chunks=jsFiles(root);
if(chunks.length===0)throw Error('S12R release has no renderer JS chunks');
const content=chunks.map(file=>readFileSync(file,'utf8')).join('\n');
const forbidden=[
  'LegacyDevelopmentHarness',
  'asset:raw:ros-g1-r1',
  'ros-g1-r1',
  'FRBM 论文',
  '演示模式：不会修改真实文件',
  '演示模式：真实 ZIP 导入待后端接入'
];
for(const marker of forbidden){
  if(content.includes(marker))throw Error('S12R release includes forbidden Demo fixture marker: '+marker);
}
console.log('S12R release-only renderer isolation passed:',chunks.length,'JavaScript chunks checked');
