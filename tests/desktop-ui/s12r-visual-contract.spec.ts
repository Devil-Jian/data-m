import {test,expect} from '@playwright/test';

test.use({viewport:{width:1440,height:900},deviceScaleFactor:1});

const sharedSelectors=[
  '.dm-header','.dm-logo','.dm-project-picker','.dm-primary-nav',
  '.dm-sidebar','.dm-nav-tree','.dm-workspace','.dm-workspace-head',
  '.dm-group-tabs','.dm-workspace-toolbar','.dm-mode-switch',
  '.dm-filter-toolbar','.dm-view-switch','.dm-statusbar'
];

test('@S12R Demo and native Workbench use the same structural visual components',async({browser},testInfo)=>{
  const demo=await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
  const production=await browser.newPage({viewport:{width:1440,height:900},deviceScaleFactor:1});
  try{
    await demo.goto('http://127.0.0.1:5176/?legacy=1');
    await expect(demo.getByRole('heading',{name:'ROS 活性氧荧光染色'})).toBeVisible();
    await production.addInitScript(()=>{
      (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
        {id:'project-1',kind:'project',parentId:null,name:'真实测试科研项目'},
        {id:'theme-1',kind:'theme',parentId:'project-1',name:'氧化应激'},
        {id:'assay-1',kind:'assay',parentId:'theme-1',name:'ROS'}
      ]});
      (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
        assayId:'assay-1',replicateCount:2,
        groups:[{id:'group-1',name:'Control',replicates:[
          {id:'rep-1',label:'R1'},{id:'rep-2',label:'R2'}]}],
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
      (window as any).__S12R_PAPER_MOCK__=async()=>({
        papers:[],candidates:[],candidateTotalCount:0,candidateTruncated:false
      });
    });
    await production.goto('http://127.0.0.1:5176/');
    await expect(production.getByRole('heading',{name:'ROS'})).toBeVisible();
    for(const selector of sharedSelectors){
      await expect(demo.locator(selector).first()).toBeVisible();
      await expect(production.locator(selector).first()).toBeVisible();
      const left=await demo.locator(selector).first().evaluate(e=>e.className);
      const right=await production.locator(selector).first().evaluate(e=>e.className);
      expect(String(left)).toBe(String(right));
    }
    await testInfo.attach('s12r-demo-1440x900',{body:await demo.screenshot({animations:'disabled'}),contentType:'image/png'});
    await testInfo.attach('s12r-production-1440x900',{body:await production.screenshot({animations:'disabled'}),contentType:'image/png'});
    await demo.getByRole('button',{name:/分阶段/}).click();
    await production.getByRole('button',{name:/分阶段/}).click();
    await expect(demo.locator('.dm-filter-toolbar')).toBeVisible();
    await expect(production.locator('.dm-filter-toolbar')).toBeVisible();
    await testInfo.attach('s12r-demo-stage',{body:await demo.screenshot({animations:'disabled'}),contentType:'image/png'});
    await testInfo.attach('s12r-production-stage',{body:await production.screenshot({animations:'disabled'}),contentType:'image/png'});
  }finally{await demo.close();await production.close();}
});

test('@S12R production empty state and narrow viewport never hydrate Demo research assets',async({page},testInfo)=>{
  await page.setViewportSize({width:1024,height:768});
  await page.goto('/');
  await expect(page.getByText('暂无科研项目')).toBeVisible();
  await expect(page.getByText('Nrf2/HO-1 抗氧化通路课题 (FRBM 论文)')).toHaveCount(0);
  await testInfo.attach('s12r-empty-1024x768',{body:await page.screenshot({animations:'disabled'}),contentType:'image/png'});
});
