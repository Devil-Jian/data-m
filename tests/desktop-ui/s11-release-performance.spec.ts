import {expect,test} from '@playwright/test';

test('@S11 @S12 10024-row renderer IPC mock retains bounded pages, search and real project navigation',async ({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'p',kind:'project',parentId:null,name:'S11临时项目'},
      {id:'t',kind:'theme',parentId:'p',name:'S11主题'},
      {id:'a',kind:'assay',parentId:'t',name:'S11实验'}]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'a',groups:[{id:'g',name:'Control',replicates:[{id:'r',label:'R1'}]}],
      stages:[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}],
      replicateCount:1
    });
    const records=Array.from({length:10024},(_,i)=>({
      assetId:'asset'+i,assetLinkId:'link'+i,assetVersionId:'version'+i,
      versionNumber:1,name:'科研样本'+String(i).padStart(5,'0')+'.png',
      stage:'raw',groupId:'g',replicateId:'r',sha256:'a'.repeat(64),sizeBytes:i+1,referenced:false
    }));
    const requests:any[]=[];
    (window as any).__S11_MOCK__={requests};
    (window as any).__S4_ASSETS_MOCK__={
      async list(request:any){
        requests.push(request);
        const found=records.filter(x=>x.stage===request.stage&&x.groupId===request.groupId&&
          x.replicateId===request.replicateId&&x.name.includes(request.search??''));
        const offset=Number(request.cursor??0),limit=request.limit??50;
        return {items:found.slice(offset,offset+limit),total:found.length,
          nextCursor:offset+limit<found.length?String(offset+limit):null};
      },
      async detail(assetLinkId:string){return {item:records.find(x=>x.assetLinkId===assetLinkId)};}
    };
  });
  await page.goto('/');
  await expect(page.getByLabel('安装及版本信息')).toContainText('未签名');
  await page.getByRole('button',{name:'实验：S11实验'}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await page.getByRole('button',{name:'样本：R1'}).click();
  const browser=page.getByRole('region',{name:'真实资产浏览器'});
  const list=browser.getByRole('listbox',{name:'资产文件列表'});
  await expect(browser.getByRole('status')).toContainText('匹配 10024 条');
  await expect(list.getByRole('option')).toHaveCount(50);
  await browser.getByRole('button',{name:'下一页'}).click();
  await expect(browser.getByRole('button',{name:'选择 科研样本00050.png'})).toBeVisible();
  await browser.getByRole('button',{name:'选择本页'}).click();
  await expect(list.getByRole('option',{selected:true})).toHaveCount(50);
  await browser.getByRole('textbox',{name:'搜索已入库文件'}).fill('10023');
  await expect(browser.getByRole('status')).toContainText('匹配 1 条');
  await expect(list.getByRole('option')).toHaveCount(1);
  await expect(page.getByRole('button',{name:'演示工作区',exact:true})).toHaveCount(0);
  await page.getByRole('button',{name:'返回项目管理',exact:true}).click();
  await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
  await expect(page.getByLabel('安装及版本信息')).toContainText('Schema v1–v3');
  const requests=await page.evaluate(()=>(window as any).__S11_MOCK__.requests);
  expect(requests.length).toBeGreaterThan(2);
  expect(requests.every((request:any)=>request.limit===50)).toBeTruthy();
});

