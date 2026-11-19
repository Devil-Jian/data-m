import {test,expect} from '@playwright/test';

async function setup(page:import('@playwright/test').Page) {
 await page.addInitScript(()=>{
  (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
   {id:'p',kind:'project',parentId:null,name:'S7测试项目'},
   {id:'t',kind:'theme',parentId:'p',name:'S7主题'},
   {id:'a',kind:'assay',parentId:'t',name:'S7实验'}]});
  const id='a'.repeat(32),snapshots:any[]=[],calls:any[]=[],restores:string[]=[];
  const counts={projects:1,themes:1,assays:1,experiment_groups:1,
   replicates:2,blobs:2,asset_versions:2,asset_links:2,
   panel_asset_versions:1,representative_sources:1};
  let corrupted=false;
  (window as any).__S7_TEST__={id,snapshots,calls,restores,counts,setCorrupt:(v:boolean)=>{corrupted=v;}};
  (window as any).__S7_SAFETY_MOCK__=async(req:any)=>{
    calls.push(req);
    const base={action:req.action,status:'completed',snapshotId:req.snapshotId??null,
      restoreLabel:req.restoreLabel??null,report:null,snapshots:[] as any[]};
    if(req.action==='list')return {...base,snapshots:[...snapshots]};
    if(req.action==='audit')return {...base,status:corrupted?'issues_found':'clean',
      report:{checkedBlobs:2,verifiedBlobs:corrupted?1:2,
       issues:corrupted?[{code:'BLOB_HASH',subject:'hash-truncated'}]:[],
       truncated:false,tableCounts:counts}};
    if(req.action==='backup'){
      if(corrupted)throw {code:'INTEGRITY_FAILED'};
      const item={snapshotId:id,blobCount:2,versionCount:2,schemaVersion:2,createdAt:'2026-11-17T00:00:00Z'};
      if(!snapshots.length)snapshots.push(item);
      window.dispatchEvent(new CustomEvent('data-m-s7-progress',{detail:{phase:'copy_blobs',completed:2,total:2}}));
      return {...base,status:'verified',snapshotId:id};
    }
    if(req.action==='verify'){
      if(corrupted)throw {code:'SNAPSHOT_CORRUPT'};
      return {...base,status:'verified',report:{checkedBlobs:2,verifiedBlobs:2,
        issues:[],truncated:false,tableCounts:counts}};
    }
    if(req.action==='restore'){
      if(corrupted)throw {code:'SNAPSHOT_CORRUPT'};
      if(req.confirm!==true)throw {code:'INVALID_REQUEST'};
      if(restores.includes(req.restoreLabel))throw {code:'RESTORE_CONFLICT'};
      restores.push(req.restoreLabel);
      return {...base,status:'restored',report:{checkedBlobs:2,verifiedBlobs:2,
        issues:[],truncated:false,tableCounts:counts}};
    }
    throw {code:'INVALID_REQUEST'};
  };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'数据完整性与备份恢复 (S7)'}).click();
 const root=page.getByRole('main',{name:'S7 完整性与备份恢复工作台'});
 await expect(root).toBeVisible();
 return root;
}
test('@S7 audit shows counts and is read-only (explicit browser mock)',async({page})=>{
 const root=await setup(page);
 await root.getByRole('button',{name:'运行只读完整性检查'}).click();
 await expect(root.getByRole('region',{name:'完整性核验报告'})).toContainText('Blob：已核对 2');
 await expect(root.getByRole('region',{name:'完整性核验报告'})).toContainText('replicates');
 await expect(root).toContainText('受控测试仓库检查通过');
 const data=await page.evaluate(()=>(window as any).__S7_TEST__);
 expect(data.calls.filter((x:any)=>x.action==='audit')).toHaveLength(1);
 expect(data.restores).toHaveLength(0);
});
test('@S7 backup requires consent, exposes sealed manifest and verify (explicit browser mock)',async({page})=>{
 const root=await setup(page);
 const backup=root.getByRole('button',{name:'确认创建并验证备份'});
 await expect(backup).toBeDisabled();
 await root.getByRole('checkbox',{name:/设备内快照并非异地备份/}).check();
 await backup.click();
 await expect(root).toContainText('已封存快照');
 await page.getByRole('button',{name:/任务状态/}).click();
 await expect(page.getByRole('region',{name:'任务进度明细'})).toContainText('创建并验证快照');
 await expect(page.getByRole('region',{name:'任务进度明细'})).toContainText('已收到原生 S7 确认结果');

 const list=root.locator('.dm-safety-snapshots');
 await expect(list.getByRole('listitem')).toHaveCount(1);
 await list.getByRole('button',{name:'验证哈希'}).click();
 await expect(root).toContainText('数据库和 Blob 已核验');
 const data=await page.evaluate(()=>(window as any).__S7_TEST__);
 expect(data.calls.map((x:any)=>x.action)).toContain('backup');
 expect(data.snapshots).toHaveLength(1);
});
test('@S7 isolated restore rejects unsafe labels and confirms never overwrite (explicit browser mock)',async({page})=>{
 const root=await setup(page);
 await root.getByRole('checkbox',{name:/设备内快照并非异地备份/}).check();
 await root.getByRole('button',{name:'确认创建并验证备份'}).click();
 await root.getByRole('button',{name:'恢复到新目录'}).click();
 const confirmation=root.getByRole('group',{name:'隔离恢复确认'});
 await confirmation.getByRole('textbox',{name:'恢复目录标签'}).fill('../escape');
 await expect(confirmation.getByRole('button',{name:'确认隔离恢复'})).toBeDisabled();
 await confirmation.getByRole('textbox',{name:'恢复目录标签'}).fill('s7-restore');
 await expect(confirmation.getByRole('button',{name:'确认隔离恢复'})).toBeDisabled();
 await confirmation.getByRole('checkbox',{name:/绝不覆盖当前仓库/}).check();
 await confirmation.getByRole('button',{name:'确认隔离恢复'}).click();
 await expect(root).toContainText('独立测试仓库 s7-restore 恢复成功');
 const data=await page.evaluate(()=>(window as any).__S7_TEST__);
 expect(data.restores).toEqual(['s7-restore']);
 expect(data.calls.find((x:any)=>x.action==='restore').confirm).toBe(true);
});
test('@S7 corrupt snapshot error leaves recovery disabled; no fake success (explicit browser mock)',async({page})=>{
 const root=await setup(page);
 await root.getByRole('checkbox',{name:/设备内快照并非异地备份/}).check();
 await root.getByRole('button',{name:'确认创建并验证备份'}).click();
 await page.evaluate(()=>(window as any).__S7_TEST__.setCorrupt(true));
 await root.getByRole('button',{name:'验证哈希'}).click();
 await expect(root.getByRole('alert')).toContainText('快照的校验清单');
 await root.getByRole('button',{name:'恢复到新目录'}).click();
 const box=root.getByRole('group',{name:'隔离恢复确认'});
 await box.getByRole('textbox',{name:'恢复目录标签'}).fill('new-case');
 await box.getByRole('checkbox',{name:/绝不覆盖当前仓库/}).check();
 await box.getByRole('button',{name:'确认隔离恢复'}).click();
 await expect(root.getByRole('alert')).toContainText('快照的校验清单');
 const data=await page.evaluate(()=>(window as any).__S7_TEST__);
 expect(data.restores).toHaveLength(0);
});
