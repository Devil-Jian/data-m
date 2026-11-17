import {test,expect} from '@playwright/test';

// Regression: the S9 candidate cap must not be mistaken for a complete
// project-wide search. All data here are synthetic renderer mocks.
test('@S9 @I1 warns when paper candidate search is capped at 500',async({page})=>{
  const candidates=Array.from({length:500},(_,i)=>({
    assetVersionId:'version-'+i,assetId:'asset-'+i,versionNumber:1,
    name:'Synthetic file '+i,sha256:'a'.repeat(64),stage:'raw',
    assayId:'assay',groupId:'group',replicateId:null
  }));
  await page.addInitScript(({candidates})=>{
    (window as any).__TAURI_INTERNALS__={
      invoke:async(command:string,args:any)=>{
        if(command==='project_tree')return {nodes:[{id:'project',kind:'project',parentId:null,name:'Synthetic project'}]};
        if(command==='paper_workspace'){
          if(args.request.action==='list')return {papers:[],candidates:[]};
          if(args.request.action==='candidates')return {papers:[],candidates};
          throw Error('Unexpected write action: '+args.request.action);
        }
        throw Error('Unexpected IPC: '+command);
      },
      transformCallback:()=>1,
      metadata:{currentWindow:{label:'main'}}
    };
  },{candidates});
  await page.goto('/');
  await page.getByRole('button',{name:'论文管理'}).click();
  await expect(page.getByLabel('S9 真实论文管理工作区')).toBeVisible();
  await expect(page.getByText(/候选列表仅显示最多 500 条/)).toBeVisible();
  await page.getByLabel('搜索真实文件版本').fill('not-in-first-500');
  await expect(page.getByText(/筛选仅作用于已加载候选项/)).toBeVisible();
  await expect(page.getByRole('button',{name:/原子关联所选/})).toBeDisabled();
});
