import {test,expect} from '@playwright/test';

test('@S12R real transfer requires explicit sample before copy commit',async({page})=>{
  await page.addInitScript(()=>{
    const requests:any[]=[];
    (window as any).__S12R_TRANSFER_REQUESTS__=requests;
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'project-1',kind:'project',parentId:null,name:'真实课题'},
      {id:'theme-1',kind:'theme',parentId:'project-1',name:'氧化'},
      {id:'assay-1',kind:'assay',parentId:'theme-1',name:'ROS'}
    ]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'assay-1',replicateCount:1,groups:[
        {id:'group-1',name:'Control',replicates:[{id:'rep-1',label:'R1'}]}
      ],stages:[
        {id:'raw',label:'Raw',scope:'replicate'},
        {id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},
        {id:'display',label:'Display',scope:'assay'}
      ]
    });
    const asset={
      assetId:'asset-1',assetLinkId:'link-1',assetVersionId:'version-1',
      versionNumber:1,name:'R1_raw.tif',stage:'raw',groupId:'group-1',replicateId:'rep-1',
      sha256:'a'.repeat(64),sizeBytes:32,referenced:false
    };
    (window as any).__S4_ASSETS_MOCK__={
      list:async({stage}:any)=>({items:stage==='raw'?[asset]:[],total:stage==='raw'?1:0,nextCursor:null}),
      detail:async()=>({item:asset})
    };
    (window as any).__S5_TRANSFER_MOCK__=async(request:any)=>{
      requests.push(request);
      return {requestId:request.requestId,action:request.action,target:request.target,
        links:request.items.map((item:any)=>({
          sourceLinkId:item.assetLinkId,resultLinkId:'copy-'+item.assetLinkId,
          assetId:item.assetId,assetVersionId:item.assetVersionId
        }))};
    };
  });
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  await page.getByRole('button',{name:/分阶段/}).click();
  await expect(page.getByText('R1_raw.tif')).toBeVisible();
  await page.getByRole('checkbox',{name:'选择'}).check();
  await page.getByRole('button',{name:'复制',exact:true}).click();
  const dialog=page.getByRole('dialog',{name:'真实文件复制移动确认'});
  await expect(dialog).toBeVisible();
  const confirm=dialog.getByRole('button',{name:'确认复制'});
  await expect(confirm).toBeDisabled();
  expect(await page.evaluate(()=>(window as any).__S12R_TRANSFER_REQUESTS__.length)).toBe(0);
  await dialog.getByRole('combobox',{name:'复制移动目标样本'}).click();
  await page.getByRole('option',{name:'R1'}).click();
  await expect(confirm).toBeEnabled();
  await confirm.click();
  await expect(dialog).toHaveCount(0);
  const requests=await page.evaluate(()=>(window as any).__S12R_TRANSFER_REQUESTS__);
  expect(requests).toHaveLength(1);
  expect(requests[0].target).toEqual({
    assayId:'assay-1',stage:'raw',groupId:'group-1',replicateId:'rep-1'
  });
  expect(requests[0].items[0].assetVersionId).toBe('version-1');
  await expect(page.locator('.dm-workspace-head').getByText(/1 个生物学重复/)).toBeVisible();
});

test('@S12R import panel exposes real S3 target without creating ZIP illusion',async({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'project-1',kind:'project',parentId:null,name:'真实课题'},
      {id:'theme-1',kind:'theme',parentId:'project-1',name:'氧化'},
      {id:'assay-1',kind:'assay',parentId:'theme-1',name:'ROS'}
    ]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'assay-1',replicateCount:1,groups:[
        {id:'group-1',name:'Control',replicates:[{id:'rep-1',label:'R1'}]}],
      stages:[
        {id:'raw',label:'Raw',scope:'replicate'},
        {id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},
        {id:'display',label:'Display',scope:'assay'}
      ]
    });
    (window as any).__S4_ASSETS_MOCK__={list:async()=>({items:[],total:0,nextCursor:null}),detail:async()=>null};
    (window as any).__S12R_IMPORT_REQUESTS__=[];
    (window as any).__S3_IMPORTS_MOCK__=async(request:any)=>{
      (window as any).__S12R_IMPORT_REQUESTS__.push(request);
      return {jobs:[]};
    };
  });
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  await page.getByRole('button',{name:'导入实验归档包'}).click();
  const dialog=page.getByRole('dialog',{name:'受控安全导入'});
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('ZIP 不能直接解包');
  await expect(dialog).toContainText('尚未选择有效目标');
  const picker=dialog.getByRole('combobox',{name:'导入目标样本'});
  await picker.click();
  await page.getByRole('option',{name:'R1'}).click();
  await expect(dialog).toContainText('R1');
  await expect(dialog.getByRole('region',{name:'安全导入任务'})).toBeVisible();
  await page.evaluate(()=>window.dispatchEvent(new CustomEvent('data-m-test-import-drop',{
    detail:{paths:['C:\\test\\test-paper.zip']}
  })));
  await expect(dialog).toContainText('ZIP 文件不支持直接解包');
  const submitted=await page.evaluate(()=>(window as any).__S12R_IMPORT_REQUESTS__);
  expect(submitted.some((request:any)=>request.action==='prepare')).toBe(false);
});
