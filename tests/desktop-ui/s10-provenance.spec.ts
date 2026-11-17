import {test,expect} from '@playwright/test';

test('@S10 formal Paper Panel provenance and exact-version reverse navigation (renderer IPC mock)',async({page})=>{
 await page.addInitScript(()=>{
  const calls:string[]=[];
  const asset={assetVersionId:'v1',usage:'display',versionNumber:1,name:'sample_raw.tif',
    sha256:'a'.repeat(64),stage:'raw',assayId:'assay',replicateId:'rep'};
  const paper={id:'paper',title:'Research Paper',status:'draft',notes:'',figures:[{
   id:'figure',label:'Figure 1',panels:[{id:'panel',label:'A',assets:[asset],representatives:[]}]
  }]};
  (window as any).__s10_calls=calls;
  (window as any).__TAURI_INTERNALS__={
   invoke:async(command:string,args:any)=>{
    calls.push(command);
    if(command==='project_tree')return {nodes:[{id:'project',kind:'project',parentId:null,name:'研究项目'}]};
    if(command==='paper_workspace')return {papers:[paper],candidates:[]};
    if(command==='provenance_trace'){
     if(args.request.projectId!=='project')throw Error('Wrong project');
     const first={paperId:'paper',figureId:'figure',panelId:'panel',usage:'display',
      analysisId:null,analysisRole:null,frozenAt:null,includedReplicateIds:[],
      assetId:'asset',assetVersionId:'v1',versionNumber:1,blobSha256:'a'.repeat(64),
      sizeBytes:3,assetDeleted:false};
     const second={...first,paperId:null,figureId:null,panelId:null,usage:null,
      analysisId:'analysis',analysisRole:'input',frozenAt:'2026-01-01',
      includedReplicateIds:['rep']};
     const edges=args.request.action==='panel'?[first]:[first,second];
     return {edges,totalCount:edges.length,offset:0,limit:50,truncated:false};
    }
    throw Error('Unexpected command '+command);
   },
   transformCallback:()=>1,metadata:{currentWindow:{label:'main'}}
  };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'论文管理'}).click();
 await page.getByRole('button',{name:/A · 1 份/}).click();
 await expect(page.getByRole('region',{name:'S10 版本溯源'})).toHaveCount(1);
 await expect(page.getByText('共 1 条明确引用')).toBeVisible();
 await page.getByLabel('选择反向查询版本').selectOption('v1');
 await expect(page.getByRole('region',{name:'S10 版本溯源'})).toHaveCount(2);
 await expect(page.getByText('历史纳入 n=1')).toBeVisible();
 await expect(page.getByText('尚未建立统计分析关联')).toHaveCount(0);
});
