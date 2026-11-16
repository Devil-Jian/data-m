import {test,expect,Page} from '@playwright/test';
import type {Locator} from '@playwright/test';

/** Exercise native HTML5 drag gestures: Chromium needs several pointer moves
 * after the initial mouse-down to cross its drag-start threshold on Windows. */
async function pointerDrag(page:Page,source:Locator,destination:Locator,modifier:'Control'|'Shift'){
  const from=await source.boundingBox(),to=await destination.boundingBox();
  if(!from||!to)throw new Error('S5 drag source or target was not visible');
  const x=from.x+from.width/2,y=from.y+from.height/2;
  await page.mouse.move(x,y);
  await page.keyboard.down(modifier);
  try{
    await page.mouse.down();
    await page.mouse.move(x+4,y+3,{steps:8});
    await page.mouse.move(x+12,y+10,{steps:8});
    await page.mouse.move(to.x+to.width/2,to.y+to.height/2,{steps:24});
    await page.mouse.up();
  }finally{
    await page.keyboard.up(modifier);
  }
}


async function setup(page:Page){
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
      {id:'p',kind:'project',parentId:null,name:'S5项目'},
      {id:'t',kind:'theme',parentId:'p',name:'S5主题'},
      {id:'a',kind:'assay',parentId:'t',name:'S5实验'}]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async()=>({
      assayId:'a',groups:[
        {id:'g1',name:'Control',replicates:[{id:'r1',label:'R1'}]},
        {id:'g2',name:'Treatment',replicates:[{id:'r2',label:'R2'}]}
      ],
      stages:[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},
        {id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}],
      replicateCount:2
    });
    const records=Array.from({length:10},(_,i)=>({
      assetId:'asset'+i,assetLinkId:'link'+i,assetVersionId:'version'+i,
      versionNumber:1,name:'raw'+String(i).padStart(2,'0')+'.png',
      stage:'raw',groupId:'g1',replicateId:'r1',sha256:'a'.repeat(64),sizeBytes:100+i,referenced:false
    }));
    const requests:any[]=[],audit:any[]=[],responses=new Map<string,any>();
    (window as any).__S5_TEST__={records,requests,audit};
    (window as any).__S4_ASSETS_MOCK__={
      async list(request:any){
        const found=records.filter(row=>row.stage===(request.stage??row.stage)&&row.groupId===(request.groupId??row.groupId)
          &&row.replicateId===(request.replicateId??row.replicateId)&&row.name.includes(request.search??''));
        const offset=Number(request.cursor??0);
        return {items:found.slice(offset,offset+(request.limit??50)),total:found.length,
          nextCursor:offset+(request.limit??50)<found.length?String(offset+(request.limit??50)):null};
      },
      async detail(assetLinkId:string){return {item:records.find(row=>row.assetLinkId===assetLinkId)};}
    };
    (window as any).__S5_TRANSFER_MOCK__=async(request:any)=>{
      requests.push(request);
      const previous=responses.get(request.requestId);
      if(previous){
        if(JSON.stringify(previous.request)!==JSON.stringify(request))throw {code:'REQUEST_CONFLICT'};
        return previous.result;
      }
      if(!(['raw','processed'].includes(request.target.stage)?request.target.replicateId==='r1'&&request.target.groupId==='g1'
        ||request.target.replicateId==='r2'&&request.target.groupId==='g2':request.target.groupId==null&&request.target.replicateId==null))
        throw {code:'INVALID_TARGET'};
      const origins=request.items.map((item:any)=>{
        const row=records.find(x=>x.assetLinkId===item.assetLinkId);
        if(!row||row.assetId!==item.assetId||row.assetVersionId!==item.assetVersionId
          ||row.stage!==item.stage||row.groupId!==item.groupId||row.replicateId!==item.replicateId)throw {code:'STALE_SELECTION'};
        return row;
      });
      if(origins.some((row:any)=>records.some(x=>x!==row&&x.stage===request.target.stage
        &&x.groupId===(request.target.groupId??null)&&x.replicateId===(request.target.replicateId??null)
        &&x.name.toLowerCase()===row.name.toLowerCase()))
        ||origins.some((row:any)=>row.stage===request.target.stage&&row.groupId===(request.target.groupId??null)
          &&row.replicateId===(request.target.replicateId??null)))throw {code:'NAME_CONFLICT'};
      const links=origins.map((row:any,index:number)=>{
        const resultLinkId=request.action==='copy'?'s5-copy-'+audit.length+'-'+index:row.assetLinkId;
        const next={...row,assetLinkId:resultLinkId,stage:request.target.stage,
          groupId:request.target.groupId??null,replicateId:request.target.replicateId??null};
        if(request.action==='copy')records.push(next);
        else Object.assign(row,next);
        return {sourceLinkId:row.assetLinkId,resultLinkId,assetId:row.assetId,assetVersionId:row.assetVersionId};
      });
      const result={requestId:request.requestId,action:request.action,
        target:{assayId:request.target.assayId,stage:request.target.stage,
          groupId:request.target.groupId??null,replicateId:request.target.replicateId??null},links};
      responses.set(request.requestId,{request,result});audit.push(result);return result;
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'项目管理',exact:true}).click();
  await page.getByRole('button',{name:'实验：S5实验'}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
}
test('@S5 ten files copied across groups by Ctrl+C/Ctrl+V keep n=2 (explicit browser mock)',async({page})=>{
  await setup(page);
  await page.getByRole('button',{name:'样本：R1'}).click();
  const browser=page.getByRole('region',{name:'真实资产浏览器'});
  await expect(browser.getByRole('status')).toContainText('匹配 10 条');
  await browser.getByRole('button',{name:'选择本页'}).click();
  await browser.getByRole('button',{name:'选择 raw00.png'}).focus();
  await page.keyboard.press('Control+c');
  await expect(browser.getByText(/剪贴板 10 条/)).toBeVisible();
  await page.getByRole('button',{name:'样本：R2'}).click();
  await expect(browser.getByRole('status')).toContainText('匹配 0 条');
  await page.keyboard.press('Control+v');
  await expect(browser.getByRole('status')).toContainText('匹配 10 条');
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 2');
  const result=await page.evaluate(()=>(window as any).__S5_TEST__);
  expect(result.requests).toHaveLength(1);
  expect(result.audit).toHaveLength(1);
  expect(result.records).toHaveLength(20);
  expect(new Set(result.records.map((x:any)=>x.assetId)).size).toBe(10);
});
test('@S5 right-click cut move and destination conflict are atomic (explicit browser mock)',async({page})=>{
  await setup(page);
  await page.getByRole('button',{name:'样本：R1'}).click();
  const browser=page.getByRole('region',{name:'真实资产浏览器'});
  await expect(browser.getByRole('status')).toContainText('匹配 10 条');
  await browser.getByRole('button',{name:'选择 raw00.png'}).click({button:'right'});
  await browser.getByRole('menu',{name:'资产右键菜单'}).getByRole('menuitem',{name:'剪切'}).click();
  await page.getByRole('button',{name:'样本：R2'}).click();
  await browser.getByRole('button',{name:'粘贴 (Ctrl+V)'}).click();
  await expect(browser.getByRole('status')).toContainText('匹配 1 条');
  await page.getByRole('button',{name:'样本：R1'}).click();
  await expect(browser.getByRole('status')).toContainText('匹配 9 条');
  await browser.getByRole('button',{name:'选择 raw01.png'}).click({button:'right'});
  await browser.getByRole('menu',{name:'资产右键菜单'}).getByRole('menuitem',{name:'复制',exact:true}).click();
  await page.getByRole('button',{name:'样本：R2'}).click();
  await browser.getByRole('button',{name:'粘贴 (Ctrl+V)'}).click();
  await expect(browser.getByRole('status')).toContainText('匹配 2 条');
  await page.getByRole('button',{name:'样本：R1'}).click();
  await browser.getByRole('button',{name:'选择 raw01.png'}).click();
  await browser.getByRole('button',{name:'复制 (Ctrl+C)'}).click();
  await page.getByRole('button',{name:'样本：R2'}).click();
  await browser.getByRole('button',{name:'粘贴 (Ctrl+V)'}).click();
  await expect(browser.getByRole('alert')).toContainText('目标已有同名');
  await expect(browser.getByRole('status')).toContainText('匹配 2 条');
  const result=await page.evaluate(()=>(window as any).__S5_TEST__);
  expect(result.audit).toHaveLength(2);
  expect(result.records).toHaveLength(11);
});
test('@S5 Ctrl+drag copies across stages and Shift+drag moves to another sample (explicit browser mock)',async({page})=>{
  await setup(page);
  await page.getByRole('button',{name:'样本：R1'}).click();
  const browser=page.getByRole('region',{name:'真实资产浏览器'});
  const nav=page.getByRole('navigation',{name:'数据阶段'});
  await expect(browser.getByRole('status')).toContainText('匹配 10 条');
  await page.evaluate(()=>{
    (window as any).__S5_DRAG_EVENTS__=[];
    for (const type of ['dragstart','dragover','drop','dragend'])document.addEventListener(type,(event:Event)=>{
      const drag=event as DragEvent;
      const events=(window as any).__S5_DRAG_EVENTS__ as any[];
      if(events.length<40)events.push({type,ctrl:drag.ctrlKey,shift:drag.shiftKey,
        target:(drag.target as HTMLElement)?.getAttribute('aria-label')??(drag.target as HTMLElement)?.textContent?.slice(0,40)});
    },true);
  });
  await pointerDrag(page,browser.getByRole('button',{name:'选择 raw00.png'}),nav.getByRole('button',{name:/Processed/}),'Control');
  console.log('S5 drag diagnostics',JSON.stringify(await page.evaluate(()=>({
    events:(window as any).__S5_DRAG_EVENTS__,
    requests:(window as any).__S5_TEST__.requests,
    notices:[...document.querySelectorAll('.dm-asset-feedback')].map(x=>x.textContent)
  }))));
  await expect(browser.getByRole('status')).toContainText('匹配 1 条');
  await expect(nav.getByRole('button',{name:/Processed/})).toHaveAttribute('aria-current','page');
  await pointerDrag(page,browser.getByRole('button',{name:'选择 raw00.png'}),page.getByRole('button',{name:'样本：R2'}),'Shift');
  await expect(nav.getByRole('button',{name:/Processed/})).toHaveAttribute('aria-current','page');
  await expect(browser.getByRole('status')).toContainText('匹配 1 条');
  await expect(page.getByRole('button',{name:'样本：R2'})).toHaveAttribute('aria-current','true');
  const result=await page.evaluate(()=>(window as any).__S5_TEST__);
  expect(result.audit.map((x:any)=>x.action)).toEqual(['copy','move']);
});
