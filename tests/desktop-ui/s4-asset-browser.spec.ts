import {test,expect} from '@playwright/test';

test('@S4 real-context asset browsing pagination search and selection (explicit mock)',async ({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'p',kind:'project',parentId:null,name:'S4测试项目'},
      {id:'t',kind:'theme',parentId:'p',name:'S4测试主题'},
      {id:'a',kind:'assay',parentId:'t',name:'S4测试实验'}]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({assayId:'a',groups:[{id:'g',name:'Control',replicates:[{id:'r',label:'R1'}]}],
      stages:[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}],replicateCount:1});
    const requests:any[]=[];
    const records=Array.from({length:100},(_,i)=>({assetId:'asset'+i,assetLinkId:'link'+i,assetVersionId:'version'+i,
      versionNumber:1,name:'sample'+String(i).padStart(3,'0')+'.png',stage:'raw',groupId:'g',replicateId:'r',
      sha256:'a'.repeat(64),sizeBytes:i+1,referenced:false}));
    (window as any).__S4_TEST__={requests};
    (window as any).__S4_ASSETS_MOCK__={
      async list(request:any){
        requests.push(request);
        const found=records.filter(x=>x.stage===request.stage&&x.groupId===request.groupId&&x.replicateId===request.replicateId
          &&x.name.includes(request.search??''));
        const offset=Number(request.cursor??0);
        return {items:found.slice(offset,offset+(request.limit??50)),total:found.length,
          nextCursor:offset+50<found.length?String(offset+50):null};
      },
      async detail(assetLinkId:string){return {item:records.find(x=>x.assetLinkId===assetLinkId)};}
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'项目管理',exact:true}).click();
  await page.getByRole('button',{name:'实验：S4测试实验'}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await page.getByRole('button',{name:'样本：R1'}).click();
  const browser=page.getByRole('region',{name:'真实资产浏览器'});
  const list=browser.getByRole('listbox',{name:'资产文件列表'});
  await expect(browser.getByRole('status')).toContainText('匹配 100 条');
  await expect(list.getByRole('option')).toHaveCount(50);
  await browser.getByRole('button',{name:'下一页'}).click();
  await expect(list.getByRole('option')).toHaveCount(50);
  await expect(browser.getByRole('button',{name:'选择 sample050.png'})).toBeVisible();
  await browser.getByRole('button',{name:'上一页'}).click();
  await browser.getByRole('button',{name:'选择 sample001.png'}).click();
  await browser.getByRole('button',{name:'选择 sample003.png'}).click({modifiers:['Shift']});
  await expect(list.getByRole('option',{selected:true})).toHaveCount(3);
  await browser.getByRole('button',{name:'选择本页'}).click();
  await expect(list.getByRole('option',{selected:true})).toHaveCount(50);
  await browser.getByRole('button',{name:'详情'}).first().click();
  await expect(browser.getByRole('complementary',{name:'文件详情'})).toContainText('link0');
  await browser.getByRole('button',{name:'关闭详情'}).click();
  await browser.getByRole('textbox',{name:'搜索已入库文件'}).fill('sample099');
  await expect(browser.getByRole('status')).toContainText('匹配 1 条');
  await expect(list.getByRole('option')).toHaveCount(1);
  const requests=await page.evaluate(()=>(window as any).__S4_TEST__.requests);
  expect(requests.some((x:any)=>x.replicateId==='r'&&x.groupId==='g'&&x.assayId==='a')).toBeTruthy();
});
