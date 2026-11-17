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
    if(command==='figure_export'){
      if(args.request.action==='preview')return {
        status:'preview',preview:{figureId:'figure',panelCount:1,referenceCount:1,
         uniqueFileCount:1,uniqueBytes:3},folderPath:null,manifest:null
      };
      throw {code:'BLOB_CORRUPT',message:'Source hash mismatch'};
    }
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
 await page.getByRole('button',{name:'预览导出范围'}).click();
 await expect(page.getByText(/唯一文件 1 个/)).toBeVisible();
 page.once('dialog',dialog=>{void dialog.accept()});
 await page.getByRole('button',{name:'选择目录并无损导出'}).click();
 await expect(page.getByRole('alert').filter({hasText:'Source hash mismatch'})).toBeVisible();
 await expect(page.getByText('无损导出完成')).toHaveCount(0);
});

test('@S10 formal asset detail reverse reference is project scoped (renderer IPC mock)',async({page})=>{
 await page.addInitScript(()=>{
  const nodes=[
   {id:'project',kind:'project',parentId:null,name:'项目'},
   {id:'theme',kind:'theme',parentId:'project',name:'主题'},
   {id:'assay',kind:'assay',parentId:'theme',name:'实验'},
  ];
  (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes});
  (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
   assayId:'assay',groups:[{id:'group',name:'Control',replicates:[{id:'rep',label:'R1'}]}],
   stages:[{id:'raw',label:'Raw',scope:'replicate'},
     {id:'processed',label:'Processed',scope:'replicate'},
     {id:'statistics',label:'Statistics',scope:'assay'},
     {id:'display',label:'Display',scope:'assay'}],replicateCount:1
  });
  const row={assetId:'asset',assetLinkId:'link',assetVersionId:'v1',versionNumber:1,
   name:'sample_raw.tif',stage:'raw',groupId:'group',replicateId:'rep',
   sha256:'a'.repeat(64),sizeBytes:3,referenced:true};
  (window as any).__S4_ASSETS_MOCK__={
   async list(){return {items:[row],total:1,nextCursor:null};},
   async detail(){return {item:row};}
  };
  (window as any).__TAURI_INTERNALS__={
   invoke:async(command:string,args:any)=>{
    if(command==='project_tree')return {nodes};
    if(command==='provenance_trace'){
      if(args.request.projectId!=='project'||args.request.action!=='asset'||args.request.id!=='asset')
       throw Error('Unverified project or asset');
      return {edges:[{paperId:'paper',figureId:'figure',panelId:'panel',usage:'display',
       analysisId:null,analysisRole:null,frozenAt:null,includedReplicateIds:[],
       assetId:'asset',assetVersionId:'v1',versionNumber:1,
       blobSha256:'a'.repeat(64),sizeBytes:3,assetDeleted:false}],
       totalCount:1,offset:0,limit:50,truncated:false};
    }
    throw Error('Unexpected '+command);
   },transformCallback:()=>1,metadata:{currentWindow:{label:'main'}}
  };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'项目管理',exact:true}).click();
 await page.getByRole('button',{name:'实验：实验'}).click();
 await page.getByRole('button',{name:'分组与样本',exact:true}).click();
 await page.getByRole('button',{name:'样本：R1'}).click();
 const browser=page.getByRole('region',{name:'真实资产浏览器'});
 await browser.getByRole('button',{name:'详情'}).first().click();
 const detail=browser.getByRole('complementary',{name:'文件详情'});
 await expect(detail.getByRole('region',{name:'S10 版本溯源'})).toBeVisible();
 await expect(detail.getByText('Panel panel')).toBeVisible();
});
