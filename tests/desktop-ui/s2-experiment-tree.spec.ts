import {test,expect,type Page} from '@playwright/test';

// Explicit in-memory browser backend. These tests exercise renderer contracts;
// SQLite persistence is verified separately by native repository/IPC tests.
async function openExperiment(page:Page,{initialFailure=false,missingBackend=false}={}){
  await page.addInitScript(({initialFailure,missingBackend})=>{
    const projects={nodes:[
      {id:'project',kind:'project',parentId:null,name:'S2测试项目'},
      {id:'theme',kind:'theme',parentId:'project',name:'S2测试主题'},
      {id:'assay',kind:'assay',parentId:'theme',name:'S2测试实验'},
      {id:'other-assay',kind:'assay',parentId:'theme',name:'另一实验'},
    ]};
    (window as any).__S1_PROJECT_TREE_MOCK__=async ()=>structuredClone(projects);
    const state={groups:[] as any[],requests:[] as any[],next:1,failList:initialFailure,failMutation:false,delay:0};
    (window as any).__S2_TEST__=state;
    // S2 renderer tests use an explicit empty asset fixture; no native storage is simulated.
    (window as any).__S4_ASSETS_MOCK__={
      async list(){return {items:[],total:0,nextCursor:null};},
      async detail(){throw new Error('No assets in S2 fixture');}
    };
    if(missingBackend)return;
    const applied=new Map<string,string>();
    const stages=[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},
      {id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}];
    const reply=(assayId:string)=>({assayId,groups:assayId==='assay'?structuredClone(state.groups):[],stages,
      replicateCount:assayId==='assay'?state.groups.reduce((n,g)=>n+g.replicates.length,0):0});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async (request:any)=>{
      state.requests.push({...request});
      if(state.delay)await new Promise(resolve=>setTimeout(resolve,state.delay));
      if(!['assay','other-assay'].includes(request.assayId))throw {code:'NOT_FOUND'};
      if(request.action==='list'){
        if(state.failList){state.failList=false;throw {code:'DATABASE_UNAVAILABLE',message:'D:\\private\\secret.sqlite'};}
        return reply(request.assayId);
      }
      if(state.failMutation){state.failMutation=false;throw {code:'DATABASE_UNAVAILABLE',message:'D:\\private\\secret.sqlite'};}
      if(!request.requestId)throw {code:'INVALID_REQUEST'};
      const payload=JSON.stringify(request);
      if(applied.has(request.requestId)){
        if(applied.get(request.requestId)!==payload)throw {code:'REQUEST_CONFLICT'};
        return reply(request.assayId);
      }
      if(request.assayId!=='assay')throw {code:'INVALID_PARENT'};
      if(request.action!=='delete'&&!request.name?.trim())throw {code:'INVALID_NAME'};
      if(request.action==='create'){
        if(request.kind==='group')state.groups.push({id:`g${state.next++}`,name:request.name.trim(),replicates:[]});
        else{
          const group=state.groups.find(g=>g.id===request.parentId);
          if(!group)throw {code:'INVALID_PARENT'};
          group.replicates.push({id:`r${state.next++}`,label:request.name.trim()});
        }
      }else{
        const group=state.groups.find(g=>request.kind==='group'?g.id===request.id:g.replicates.some((r:any)=>r.id===request.id));
        if(!group)throw {code:'NOT_FOUND'};
        if(request.kind==='group'){
          if(request.action==='rename')group.name=request.name.trim();
          else{
            if(group.replicates.length)throw {code:'NODE_IN_USE'};
            state.groups=state.groups.filter(g=>g.id!==request.id);
          }
        }else if(request.action==='rename')group.replicates.find((r:any)=>r.id===request.id).label=request.name.trim();
        else group.replicates=group.replicates.filter((r:any)=>r.id!==request.id);
      }
      applied.set(request.requestId,payload);
      return reply(request.assayId);
    };
  },{initialFailure,missingBackend});
  await page.goto('/');
  await page.getByRole('button',{name:'实验：S2测试实验',exact:true}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await expect(page.getByRole('heading',{name:'分组与样本',exact:true})).toBeVisible();
}
async function create(page:Page,kind:'分组'|'样本',name:string){
  await page.getByRole('button',{name:`新建${kind}`,exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill(name);
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button',{name:`${kind}：${name}`,exact:true})).toBeVisible();
}

test('@S2 creates three groups with six samples each and re-queries on reopen',async ({page})=>{
  await openExperiment(page);
  await expect(page.getByText('浏览器 Mock（不写入原生数据库）',{exact:true})).toBeVisible();
  await expect(page.getByText('暂无分组。新建分组后添加生物学重复。')).toBeVisible();
  for(const group of ['对照组','模型组','处理组']){
    await create(page,'分组',group);
    await page.getByRole('button',{name:`分组：${group}`,exact:true}).click();
    for(let index=1;index<=6;index++)await create(page,'样本',`${group}-R${index}`);
    await expect(page.getByRole('listitem',{name:`${group}，n = 6`,exact:true})).toBeVisible();
  }
  await expect(page.getByRole('status',{name:'实验样本统计'})).toHaveText('3 个分组 · 生物学重复总数 n = 18');
  const before=await page.evaluate(()=>(window as any).__S2_TEST__.requests.length);
  await page.getByRole('button',{name:'返回项目管理',exact:true}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await expect(page.getByRole('button',{name:'样本：处理组-R6',exact:true})).toBeVisible();
  const requests=await page.evaluate(()=>(window as any).__S2_TEST__.requests);
  expect(requests.length).toBeGreaterThan(before);
  expect(requests.at(-1)).toEqual({action:'list',assayId:'assay'});
  const mutations=requests.filter((r:any)=>r.action!=='list');
  expect(mutations).toHaveLength(21);
  expect(new Set(mutations.map((r:any)=>r.requestId)).size).toBe(21);
  for(let i=0;i<requests.length;i++){
    expect(requests[i].assayId).toBe('assay');
    if(requests[i].action!=='list')expect(requests[i+1].action).toBe('list');
  }
});

test('@S2 four empty stages enforce replicate and assay scopes without changing n',async ({page})=>{
  await openExperiment(page);
  for(const stage of ['Raw','Processed']){
    await page.getByRole('button',{name:new RegExp(`^${stage} `)}).click();
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('样本级');
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('请先选择一个样本');
  }
  for(const stage of ['Statistics','Display']){
    await page.getByRole('button',{name:new RegExp(`^${stage} `)}).click();
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('实验级');
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('该范围尚无已入库文件');
  }
  await create(page,'分组','分组A');
  await page.getByRole('button',{name:'分组：分组A',exact:true}).click();
  await create(page,'样本','生物学重复1');
  await page.getByRole('button',{name:'样本：生物学重复1',exact:true}).click();
  for(const stage of ['Raw','Processed']){
    await page.getByRole('button',{name:new RegExp(`^${stage} `)}).click();
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('生物学重复1');
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('该范围尚无已入库文件');
  }
  for(const stage of ['Statistics','Display']){
    await page.getByRole('button',{name:new RegExp(`^${stage} `)}).click();
    await expect(page.getByRole('region',{name:'阶段内容'})).toContainText('S2测试实验');
  }
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 1');
});

test('@S2 validates names, renames groups and samples, protects nonempty groups, deletes leaves',async ({page})=>{
  await openExperiment(page);
  await page.getByRole('button',{name:'新建分组',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('  ');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('名称不能为空');
  expect(await page.evaluate(()=>(window as any).__S2_TEST__.requests.filter((r:any)=>r.action==='create').length)).toBe(0);
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('原分组');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await page.getByRole('button',{name:'分组：原分组',exact:true}).click();
  await create(page,'样本','原样本');
  await page.getByRole('button',{name:'重命名分组',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('新分组');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'分组：新分组',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'删除分组',exact:true}).click();
  await page.getByRole('button',{name:'确认删除',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('下级或关联资产');
  await page.getByRole('button',{name:'取消',exact:true}).click();
  await page.getByRole('button',{name:'样本：原样本',exact:true}).click();
  await page.getByRole('button',{name:'重命名样本',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('新样本');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'样本：新样本',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'删除样本',exact:true}).click();
  await page.getByRole('button',{name:'确认删除',exact:true}).click();
  await expect(page.getByRole('button',{name:'样本：新样本',exact:true})).toBeHidden();
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 0');
  await page.getByRole('button',{name:'分组：新分组',exact:true}).click();
  await page.getByRole('button',{name:'删除分组',exact:true}).click();
  await page.getByRole('button',{name:'确认删除',exact:true}).click();
  await expect(page.getByText('暂无分组。新建分组后添加生物学重复。')).toBeVisible();
});

test('@S2 failed query retries safely and failed post-commit refresh keeps last confirmed tree',async ({page})=>{
  await openExperiment(page,{initialFailure:true});
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  await expect(page.getByRole('alert')).not.toContainText('secret.sqlite');
  await expect(page.getByRole('button',{name:'新建分组',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'重新加载',exact:true}).click();
  await page.getByRole('button',{name:'新建分组',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('重试分组');
  await page.evaluate(()=>{(window as any).__S2_TEST__.failList=true;});
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  await expect(page.getByRole('button',{name:'分组：重试分组',exact:true})).toBeHidden();
  expect(await page.evaluate(()=>(window as any).__S2_TEST__.groups.length)).toBe(1);
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'分组：重试分组',exact:true})).toBeVisible();
  const attempts=await page.evaluate(()=>(window as any).__S2_TEST__.requests.filter((r:any)=>r.action==='create'));
  expect(attempts).toHaveLength(2);
  expect(attempts[0].requestId).toBe(attempts[1].requestId);
  expect(await page.evaluate(()=>(window as any).__S2_TEST__.groups.length)).toBe(1);
});

test('@S2 mutation failure preserves sample labels and count; pending disables navigation',async ({page})=>{
  await openExperiment(page);
  await create(page,'分组','组一');
  await page.getByRole('button',{name:'分组：组一',exact:true}).click();
  await create(page,'样本','保留样本');
  await page.getByRole('button',{name:'样本：保留样本',exact:true}).click();
  await page.getByRole('button',{name:'重命名样本',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('新标签');
  await page.evaluate(()=>{(window as any).__S2_TEST__.failMutation=true;(window as any).__S2_TEST__.delay=400;});
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'保存',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'返回项目管理',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'演示工作区',exact:true})).toHaveCount(0);
  await expect(page.getByRole('textbox',{name:'名称',exact:true})).toBeDisabled();
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  await expect(page.getByRole('button',{name:'样本：保留样本',exact:true})).toBeVisible();
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 1');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'样本：新标签',exact:true})).toBeVisible();
});

test('@S2 missing browser backend never shows fake persisted groups',async ({page})=>{
  await openExperiment(page,{missingBackend:true});
  await expect(page.getByRole('alert')).toContainText('浏览器未连接原生实验存储');
  await expect(page.getByRole('button',{name:'新建分组',exact:true})).toBeDisabled();
  await expect(page.getByText('暂无分组。新建分组后添加生物学重复。')).toBeHidden();
});
