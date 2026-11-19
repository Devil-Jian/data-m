import {test,expect} from '@playwright/test';

test('@S12R production Workbench shows only native-mock data with scoped matrix',async({page})=>{
  await page.addInitScript(()=>{
    const hash='a'.repeat(64);
    const file=(id:string,stage:string,replicateId:string|null)=>({
      assetId:'asset-'+id,assetLinkId:'link-'+id,assetVersionId:'version-'+id,
      versionNumber:1,name:id+'.tif',stage,groupId:replicateId?'group-1':null,
      replicateId,sha256:hash,sizeBytes:13,referenced:false
    });
    const files=[file('real-raw','raw','rep-1'),file('real-stat','statistics',null),file('real-display','display',null)];
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'project-1',kind:'project',parentId:null,name:'真实测试课题'},
      {id:'theme-1',kind:'theme',parentId:'project-1',name:'氧化应激机制'},
      {id:'assay-1',kind:'assay',parentId:'theme-1',name:'ROS'}
    ]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'assay-1',replicateCount:1,groups:[{
        id:'group-1',name:'Control',replicates:[{id:'rep-1',label:'ROS_CTRL01'}]}],
      stages:[
        {id:'raw',label:'01_Raw',scope:'replicate'},
        {id:'processed',label:'02_Processed',scope:'replicate'},
        {id:'statistics',label:'03_Statistics',scope:'assay'},
        {id:'display',label:'04_Display',scope:'assay'}
      ]
    });
    (window as any).__S4_ASSETS_MOCK__={
      list:async(req:any)=>{
        const items=files.filter(f=>f.stage===req.stage);
        return {items,total:items.length,nextCursor:null};
      },
      detail:async(id:string)=>({item:files.find(f=>f.assetLinkId===id)})
    };
  });
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  await expect(page.getByRole('group',{name:'实验分组'})).toContainText('Control');
  await expect(page.getByText('ROS_CTRL01')).toBeVisible();
  await expect(page.getByText('real-raw.tif')).toBeVisible();
  await expect(page.getByText('尚无可核验的本样本引用')).toHaveCount(2);
  await page.getByRole('button',{name:/分阶段/}).click();
  await page.getByRole('button',{name:'03_Statistics'}).click();
  await expect(page.getByText('real-stat.tif')).toBeVisible();
  await expect(page.getByText('real-display.tif')).toHaveCount(0);
  await page.getByRole('button',{name:/real-stat.tif/}).click();
  await expect(page.getByRole('dialog',{name:'真实文件详情'})).toContainText('version-real-stat');
  await expect(page.getByRole('dialog',{name:'真实文件详情'})).toContainText('aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
});
test('@S12R browser without an explicit native mock never loads demo projects',async({page})=>{
  await page.goto('/');
  await expect(page.getByText('真实测试课题')).toHaveCount(0);
  await expect(page.getByText('暂无科研项目')).toBeVisible();
  await expect(page.getByText('Nrf2/HO-1 抗氧化通路课题 (FRBM 论文)')).toHaveCount(0);
});
