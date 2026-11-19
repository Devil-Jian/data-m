import {test,expect} from '@playwright/test';

test('@S12R frozen S8 inclusion and pinned S9 representative proof are distinct',async({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'project-1',kind:'project',parentId:null,name:'真实测试课题'},
      {id:'theme-1',kind:'theme',parentId:'project-1',name:'氧化'},
      {id:'assay-1',kind:'assay',parentId:'theme-1',name:'ROS'}
    ]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'assay-1',replicateCount:2,
      groups:[{id:'group-1',name:'Control',replicates:[
        {id:'rep-1',label:'R1'},{id:'rep-2',label:'R2'}
      ]}],stages:[
        {id:'raw',label:'Raw',scope:'replicate'},
        {id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},
        {id:'display',label:'Display',scope:'assay'}
      ]
    });
    (window as any).__S4_ASSETS_MOCK__={
      list:async()=>({items:[],total:0,nextCursor:null}),detail:async()=>null
    };
    const samples=[
      {replicateId:'rep-1',groupId:'group-1',groupName:'Control',label:'R1',included:true},
      {replicateId:'rep-2',groupId:'group-1',groupName:'Control',label:'R2',included:false}
    ];
    const analyses=[{id:'analysis-1',name:'冻结统计',notes:'',includedN:1,
      frozenAt:'2026-11-01T00:00:00Z',updatedAt:'2026-11-01T00:00:00Z'}];
    (window as any).__S8_ANALYSIS_MOCK__=async(req:any)=>({
      assayId:'assay-1',selectedId:req.action==='get'?'analysis-1':null,
      analyses,samples:req.action==='get'?samples:samples.map(s=>({...s,included:false})),
      availableVersions:[],inputVersionIds:[],resultVersionIds:[]
    });
    (window as any).__S12R_PAPER_MOCK__=async()=>({
      papers:[{id:'paper-1',title:'论文',status:'draft',notes:'',figures:[{
        id:'figure-1',label:'Figure 1',panels:[{
          id:'panel-1',label:'A',
          assets:[{assetVersionId:'version-display',usage:'display',versionNumber:1,
            name:'R1_show.tif',sha256:'a'.repeat(64),stage:'display',assayId:'assay-1',replicateId:null}],
          representatives:[{replicateId:'rep-1',assetVersionId:'version-display'}]
        }]
      }]}],candidates:[],candidateTotalCount:0,candidateTruncated:false
    });
  });
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  await expect(page.getByText('固定版本 Panel 来源：Figure 1 / A')).toBeVisible();
  await page.getByRole('button',{name:/统计分析/}).click();
  const drawer=page.getByRole('dialog',{name:'真实统计分析'});
  await expect(drawer).toBeVisible();
  await drawer.getByRole('button',{name:/冻结统计/}).click();
  await expect(drawer.getByText(/本次纳入 n=1/)).toBeVisible();
  await drawer.getByRole('button',{name:'关闭 ×'}).click();
  await expect(page.getByText(/冻结分析 analysis-1：已纳入/)).toBeVisible();
  await page.getByRole('combobox',{name:'按真实冻结分析筛选'}).click();
  await page.getByRole('option',{name:'本分析已纳入'}).click();
  await expect(page.getByText('R1',{exact:true})).toBeVisible();
  await expect(page.getByText('R2',{exact:true})).toHaveCount(0);
});

test('@S12R S7 safety overlay audits without mutating the running repository',async({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[]});
    (window as any).__S7_SAFETY_MOCK__=async(req:any)=>({
      action:req.action,status:req.action==='audit'?'clean':'completed',
      snapshotId:null,restoreLabel:null,
      report:req.action==='audit'?{
        checkedBlobs:2,verifiedBlobs:2,issues:[],truncated:false,tableCounts:{assets:2}
      }:null,snapshots:[]
    });
  });
  await page.goto('/');
  await expect(page.getByText('暂无科研项目')).toBeVisible();
  await page.getByRole('button',{name:/完整性 · 备份/}).click();
  const dialog=page.getByRole('dialog',{name:'仓库数据安全工具'});
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText('不是异地备份');
  await expect(dialog.getByRole('button',{name:'确认创建并验证备份'})).toBeDisabled();
  await dialog.getByRole('button',{name:'运行只读完整性检查'}).click();
  await expect(dialog.getByText('Blob：已核对 2')).toBeVisible();
  await dialog.getByRole('button',{name:'关闭安全工具'}).click();
  await expect(dialog).toHaveCount(0);
});
