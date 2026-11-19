import {test,expect} from '@playwright/test';

test('@S12R production Paper Figure/Panel cards show pinned real version and return to assay',async({page})=>{
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
    (window as any).__S4_ASSETS_MOCK__={
      list:async()=>({items:[],total:0,nextCursor:null}),detail:async()=>null
    };
    const papers=[{id:'paper-1',title:'真实论文',status:'draft',notes:'',figures:[
      {id:'figure-1',label:'Figure 1',panels:[{
        id:'panel-1',label:'Fig 1a',
        assets:[{assetVersionId:'version-123',usage:'display',versionNumber:2,
          name:'ROS_show.tiff',sha256:'b'.repeat(64),stage:'display',
          assayId:'assay-1',replicateId:null}],
        representatives:[]
      }]}
    ]}];
    (window as any).__S12R_PAPER_MOCK__=async()=>({
      papers,candidates:[],candidateTotalCount:0,candidateTruncated:false
    });
  });
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  await page.getByRole('button',{name:'论文管理'}).click();
  await expect(page.getByRole('heading',{name:'论文管理'})).toBeVisible();
  await expect(page.getByText('Figure 1')).toBeVisible();
  await page.getByRole('button',{name:/Fig 1a/}).click();
  const drawer=page.getByRole('dialog',{name:'Panel 详情抽屉'});
  await expect(drawer).toContainText('version-123');
  await expect(drawer).toContainText('ROS_show.tiff');
  await expect(drawer).toContainText('SHA-256');
  await expect(drawer).toContainText('未设置有证据的代表样本');
  await drawer.getByRole('button',{name:'查看来源实验'}).click();
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
});
