import {test,expect} from '@playwright/test';

async function setup(page:import('@playwright/test').Page){
 await page.addInitScript(()=>{
  (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
    {id:'p',kind:'project',parentId:null,name:'S6项目'},
    {id:'t',kind:'theme',parentId:'p',name:'S6主题'},
    {id:'a',kind:'assay',parentId:'t',name:'S6实验'}]});
  (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
    assayId:'a',groups:[{id:'g',name:'Control',replicates:[{id:'r',label:'R1'}]}],
    stages:[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},
      {id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}],
    replicateCount:1
  });
  const records=Array.from({length:4},(_,i)=>({
    assetId:'asset'+i,assetLinkId:'link'+i,assetVersionId:'version'+i,
    versionNumber:1,name:'raw'+i+'.png',stage:'raw',groupId:'g',replicateId:'r',
    sha256:'a'.repeat(64),sizeBytes:100+i,referenced:i===3,deletedAt:null as string|null
  }));
  const audit:any[]=[],requests:any[]=[],responses=new Map<string,{request:any;result:any}>();
  (window as any).__S6_TEST__={records,audit,requests};
  (window as any).__S4_ASSETS_MOCK__={
    async list(req:any){
      const rows=records.filter(x=>x.deletedAt===null&&x.stage===req.stage&&x.groupId===req.groupId
        &&x.replicateId===req.replicateId&&x.name.includes(req.search??''));
      const from=Number(req.cursor??0);
      return {items:rows.slice(from,from+(req.limit??50)),total:rows.length,nextCursor:null};
    },
    async detail(id:string){return {item:records.find(x=>x.assetLinkId===id&&x.deletedAt===null)};}
  };
  (window as any).__S6_RECYCLE_MOCK__=async(req:any)=>{
    const items=records.filter(x=>x.deletedAt!==null&&x.stage===req.stage&&x.groupId===req.groupId
      &&x.replicateId===req.replicateId&&x.name.includes(req.search??''))
      .map(x=>({...x,assayId:'a',canRestore:!records.some(other=>other!==x&&other.deletedAt===null
        &&other.stage===x.stage&&other.groupId===x.groupId&&other.replicateId===x.replicateId
        &&other.name.toLowerCase()===x.name.toLowerCase()),
        restoreBlockReason:records.some(other=>other!==x&&other.deletedAt===null&&other.name.toLowerCase()===x.name.toLowerCase())
         ?'NAME_CONFLICT':null}));
    return {items,total:items.length,nextCursor:null};
  };
  (window as any).__S6_LIFECYCLE_MOCK__=async(req:any)=>{
    requests.push(req);
    const previous=responses.get(req.requestId);
    if(previous){
      if(JSON.stringify(previous.request)!==JSON.stringify(req))throw {code:'REQUEST_CONFLICT'};
      return previous.result;
    }
    const items=req.action==='rename'?[req.item]:req.items;
    if(req.action==='rename'&&(!req.newName?.trim()||/[\/\\:*?"<>|]/.test(req.newName)||
      /^(con|nul|aux|prn)(\.|$)/i.test(req.newName)))throw {code:'INVALID_NAME'};
    const matches=items.map((item:any)=>{
      const row=records.find(x=>x.assetLinkId===item.assetLinkId);
      if(!row||row.assetId!==item.assetId||row.assetVersionId!==item.assetVersionId
        ||row.stage!==item.stage||row.groupId!==item.groupId||row.replicateId!==item.replicateId)
        throw {code:'STALE_SELECTION'};
      if(req.action==='restore'&&row.deletedAt===null)throw {code:'ALREADY_ACTIVE'};
      if(req.action!=='restore'&&row.deletedAt!==null)throw {code:'ALREADY_DELETED'};
      return row;
    });
    if(req.action==='soft_delete'&&matches.some((row:any)=>row.referenced&&
       records.filter(x=>x.assetId===row.assetId&&x.deletedAt===null).every(x=>matches.includes(x))))
       throw {code:'REFERENCE_PROTECTED'};
    if(req.action==='rename'||req.action==='restore'){
      for(const row of matches){
        const name=req.action==='rename'?req.newName:row.name;
        if(records.some(x=>x!==row&&x.deletedAt===null&&x.name.toLowerCase()===name.toLowerCase()&&x.stage===row.stage
           &&x.groupId===row.groupId&&x.replicateId===row.replicateId))
          throw {code:'NAME_CONFLICT'};
      }
    }
    const output=matches.map((row:any)=>{
      if(req.action==='rename')row.name=req.newName;
      else if(req.action==='soft_delete')row.deletedAt='2026-11-16T15:00:00.000Z';
      else row.deletedAt=null;
      return {assetLinkId:row.assetLinkId,assetId:row.assetId,assetVersionId:row.assetVersionId,
        name:row.name,deletedAt:row.deletedAt};
    });
    const result={requestId:req.requestId,action:req.action,results:output};
    responses.set(req.requestId,{request:req,result});audit.push(result);return result;
  };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'项目管理',exact:true}).click();
 await page.getByRole('button',{name:'实验：S6实验'}).click();
 await page.getByRole('button',{name:'分组与样本',exact:true}).click();
 await page.getByRole('button',{name:'样本：R1'}).click();
}
test('@S6 F2 rename and escape cancel preserve asset identity (explicit browser mock)',async({page})=>{
 await setup(page);
 const browser=page.getByRole('region',{name:'真实资产浏览器'});
 await expect(browser.getByRole('status').first()).toContainText('匹配 4 条');
 await browser.getByRole('button',{name:'选择 raw0.png'}).click();
 await page.keyboard.press('F2');
 const dialog=page.getByRole('dialog',{name:'文件操作确认'});
 await expect(dialog).toBeVisible();
 await dialog.getByRole('textbox',{name:'新的文件名称'}).fill('处理后.png');
 await dialog.getByRole('button',{name:'保存名称'}).click();
 await expect(browser.getByRole('button',{name:'选择 处理后.png'})).toBeVisible();
 const store=await page.evaluate(()=>(window as any).__S6_TEST__);
 expect(store.records[0].assetId).toBe('asset0');
 expect(store.records[0].assetVersionId).toBe('version0');
 expect(store.audit.map((x:any)=>x.action)).toEqual(['rename']);
 await browser.getByRole('button',{name:'选择 处理后.png'}).click();
 await page.keyboard.press('F2');
 await dialog.getByRole('textbox',{name:'新的文件名称'}).fill('discard.png');
 await page.keyboard.press('Escape');
 await expect(dialog).not.toBeVisible();
 await expect(browser.getByRole('button',{name:'选择 处理后.png'})).toBeVisible();
});
test('@S6 Delete confirmation, recycle listing, restore and n unchanged (explicit browser mock)',async({page})=>{
 await setup(page);
 const browser=page.getByRole('region',{name:'真实资产浏览器'});
 await browser.getByRole('button',{name:'选择 raw1.png'}).click();
 await page.keyboard.press('Delete');
 const dialog=page.getByRole('dialog',{name:'文件操作确认'});
 await expect(dialog).toContainText('仅标记为已删除');
 await dialog.getByRole('button',{name:'取消'}).click();
 await expect(browser.getByRole('button',{name:'选择 raw1.png'})).toBeVisible();
 await page.keyboard.press('Delete');
 await dialog.getByRole('button',{name:'确认移至回收区'}).click();
 await expect(browser.getByRole('button',{name:'选择 raw1.png'})).not.toBeVisible();
 await browser.getByRole('button',{name:'回收区',exact:true}).click();
 const recycle=browser.getByRole('region',{name:'资产回收区'});
 await expect(recycle).toContainText('raw1.png');
 await recycle.getByRole('button',{name:'恢复',exact:true}).click();
 await dialog.getByRole('button',{name:'确认恢复'}).click();
 await expect(recycle).toContainText('已回收 0 条');
 await browser.getByRole('button',{name:'关闭回收区'}).click();
 await expect(browser.getByRole('button',{name:'选择 raw1.png'})).toBeVisible();
 await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 1');
 const store=await page.evaluate(()=>(window as any).__S6_TEST__);
 expect(store.audit.map((x:any)=>x.action)).toEqual(['soft_delete','restore']);
});
test('@S6 context rename, batch delete and paper reference protection (explicit browser mock)',async({page})=>{
 await setup(page);
 const browser=page.getByRole('region',{name:'真实资产浏览器'});
 await browser.getByRole('button',{name:'选择 raw0.png'}).click({button:'right'});
 await page.getByRole('menu',{name:'资产右键菜单'}).getByRole('menuitem',{name:'重命名'}).click();
 await expect(page.getByRole('dialog',{name:'文件操作确认'})).toBeVisible();
 await page.getByRole('textbox',{name:'新的文件名称'}).fill('from-menu.png');
 await page.getByRole('button',{name:'保存名称'}).click();
 await expect(browser.getByRole('button',{name:'选择 from-menu.png'})).toBeVisible();
 await browser.getByRole('button',{name:'选择本页'}).click();
 await browser.getByRole('button',{name:'移至回收区 (Delete)'}).click();
 await page.getByRole('dialog',{name:'文件操作确认'}).getByRole('button',{name:'确认移至回收区'}).click();
 await expect(browser.getByRole('alert')).toContainText('论文引用保护');
 await expect(browser.getByRole('status').first()).toContainText('匹配 4 条');
 const data=await page.evaluate(()=>(window as any).__S6_TEST__);
 expect(data.audit).toHaveLength(1);
});
test('@S6 invalid name error, retry keeps modal and no partial changes (explicit browser mock)',async({page})=>{
 await setup(page);
 const browser=page.getByRole('region',{name:'真实资产浏览器'});
 await browser.getByRole('button',{name:'选择 raw0.png'}).click();
 await browser.getByRole('button',{name:'重命名 (F2)'}).click();
 const dialog=page.getByRole('dialog',{name:'文件操作确认'});
 await dialog.getByRole('textbox',{name:'新的文件名称'}).fill('CON.txt');
 await dialog.getByRole('button',{name:'保存名称'}).click();
 await expect(dialog.getByRole('alert')).toContainText('Windows 规则');
 await expect(dialog).toBeVisible();
 await dialog.getByRole('textbox',{name:'新的文件名称'}).fill('renamed.png');
 await dialog.getByRole('button',{name:'保存名称'}).click();
 await expect(browser.getByRole('button',{name:'选择 renamed.png'})).toBeVisible();
});
