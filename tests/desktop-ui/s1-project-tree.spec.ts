import {expect,test,type Page} from '@playwright/test';

// Explicit browser-only test server. Its memory lives outside the React view;
// this verifies renderer contracts, never native SQLite persistence.
async function mockServer(page:Page,initialFailure=false){
  await page.addInitScript(({initialFailure})=>{
    const state={nodes:[] as any[],requests:[] as any[],failList:initialFailure,failMutation:false,delay:0,next:1};
    (window as any).__S1_TEST__=state;
    const applied=new Map<string,string>();
    (window as any).__S1_PROJECT_TREE_MOCK__=async (request:any)=>{
      state.requests.push({...request});
      if(state.delay)await new Promise(resolve=>setTimeout(resolve,state.delay));
      if(request.action==='list'){
        if(state.failList){state.failList=false;throw {code:'DATABASE_UNAVAILABLE',message:'C:\\private\\secret.sqlite'};}
        return {nodes:structuredClone(state.nodes)};
      }
      if(state.failMutation){state.failMutation=false;throw {code:'DATABASE_UNAVAILABLE',message:'C:\\private\\secret.sqlite'};}
      if(!request.requestId)throw {code:'INVALID_REQUEST'};
      const payload=JSON.stringify(request);
      if(applied.has(request.requestId)){
        if(applied.get(request.requestId)!==payload)throw {code:'REQUEST_CONFLICT'};
        return {nodes:structuredClone(state.nodes)};
      }
      if(request.action==='create'){
        if(!request.name?.trim())throw {code:'INVALID_NAME'};
        const parent=state.nodes.find(node=>node.id===request.parentId);
        if(request.kind!=='project'&&(!parent||parent.kind!==(request.kind==='theme'?'project':'theme')))throw {code:'INVALID_PARENT'};
        state.nodes.push({id:String(state.next++),kind:request.kind,parentId:request.parentId??null,name:request.name.trim()});
      }else{
        const node=state.nodes.find(node=>node.id===request.id);
        if(!node)throw {code:'NOT_FOUND'};
        if(request.action==='rename')node.name=request.name.trim();
        else{
          if(state.nodes.some(item=>item.parentId===node.id))throw {code:'HAS_CHILDREN'};
          state.nodes=state.nodes.filter(item=>item.id!==node.id);
        }
      }
      applied.set(request.requestId,payload);
      return {nodes:structuredClone(state.nodes)};
    };
  },{initialFailure});
  await page.goto('/');
  await page.getByRole('button',{name:'项目管理',exact:true}).click();
  await expect(page.getByText('浏览器 Mock（不写入原生数据库）',{exact:true})).toBeVisible();
}
async function create(page:Page,kind:'项目'|'主题'|'实验',name:string){
  await page.getByRole('button',{name:`新建${kind}`,exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill(name);
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button',{name:`${kind}：${name}`,exact:true})).toBeVisible();
}

test('@S1 creates two projects, multiple themes and assays and re-queries on reopen',async ({page})=>{
  await mockServer(page);
  await expect(page.getByText('暂无项目。新建项目以开始。')).toBeVisible();
  for(const project of ['项目甲','项目乙']){
    await create(page,'项目',project);
    await page.getByRole('button',{name:`项目：${project}`,exact:true}).click();
    for(const theme of ['主题一','主题二']){
      const name=project+theme;
      await create(page,'主题',name);
      await page.getByRole('button',{name:`主题：${name}`,exact:true}).click();
      await create(page,'实验',name+'实验');
      await page.getByRole('button',{name:`项目：${project}`,exact:true}).click();
    }
  }
  const before=await page.evaluate(()=>(window as any).__S1_TEST__.requests.length);
  await page.getByRole('button',{name:'演示工作区',exact:true}).click();
  await page.getByRole('button',{name:'项目管理',exact:true}).click();
  await expect(page.getByRole('button',{name:'实验：项目乙主题二实验',exact:true})).toBeVisible();
  const requests=await page.evaluate(()=>(window as any).__S1_TEST__.requests);
  expect(requests.length).toBeGreaterThan(before);
  expect(requests.at(-1).action).toBe('list');
  for(let index=0;index<requests.length;index++)if(requests[index].action!=='list'){
    expect(requests[index].requestId).toMatch(/^[\da-f-]{36}$/i);
    expect(requests[index+1].action).toBe('list');
  }
});

test('@S1 validates names, renames, protects nonempty parents and deletes leaves',async ({page})=>{
  await mockServer(page);
  await page.getByRole('button',{name:'新建项目',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('   ');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('名称不能为空');
  expect(await page.evaluate(()=>(window as any).__S1_TEST__.requests.filter((r:any)=>r.action==='create').length)).toBe(0);
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('保护项目');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await page.getByRole('button',{name:'项目：保护项目',exact:true}).click();
  await create(page,'主题','待删除主题');
  await page.getByRole('button',{name:'删除',exact:true}).click();
  await expect(page.getByRole('dialog')).toContainText('下级或关联资产');
  await page.getByRole('button',{name:'确认删除',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('下级');
  await expect(page.getByRole('button',{name:'项目：保护项目',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'取消',exact:true}).click();
  await page.getByRole('button',{name:'主题：待删除主题',exact:true}).click();
  await page.getByRole('button',{name:'重命名',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('已重命名');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'主题：已重命名',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'删除',exact:true}).click();
  await page.getByRole('button',{name:'确认删除',exact:true}).click();
  await expect(page.getByRole('button',{name:'主题：已重命名',exact:true})).toBeHidden();
});

test('@S1 failed mutation retains query data and retry keeps its request ID',async ({page})=>{
  await mockServer(page);
  await create(page,'项目','原始项目');
  await page.getByRole('button',{name:'项目：原始项目',exact:true}).click();
  await page.evaluate(()=>{(window as any).__S1_TEST__.failMutation=true;});
  await page.getByRole('button',{name:'重命名',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('重试项目');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  await expect(page.getByRole('alert')).not.toContainText('secret.sqlite');
  await expect(page.getByRole('button',{name:'项目：原始项目',exact:true})).toBeVisible();
  await expect(page.getByRole('button',{name:'项目：重试项目',exact:true})).toBeHidden();
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'项目：重试项目',exact:true})).toBeVisible();
  const attempts=await page.evaluate(()=>(window as any).__S1_TEST__.requests.filter((r:any)=>r.action==='rename'));
  expect(attempts).toHaveLength(2);
  expect(attempts[0].requestId).toBe(attempts[1].requestId);
});

test('@S1 failed initial query has safe error and explicit retry',async ({page})=>{
  await mockServer(page,true);
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  await expect(page.getByRole('alert')).not.toContainText('private');
  await expect(page.getByRole('button',{name:'新建项目',exact:true})).toBeDisabled();
  await page.getByRole('button',{name:'重新加载',exact:true}).click();
  await expect(page.getByText('暂无项目。新建项目以开始。')).toBeVisible();
  await expect(page.getByRole('button',{name:'新建项目',exact:true})).toBeEnabled();
});

test('@S1 failed refresh after commit keeps last queried tree until idempotent retry',async ({page})=>{
  await mockServer(page);
  await page.getByRole('button',{name:'新建项目',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('服务端已提交');
  await page.evaluate(()=>{(window as any).__S1_TEST__.failList=true;});
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('暂时无法');
  // Mutation reply already contains the node, but renderer must not use it.
  await expect(page.getByRole('button',{name:'项目：服务端已提交',exact:true})).toBeHidden();
  expect(await page.evaluate(()=>(window as any).__S1_TEST__.nodes.length)).toBe(1);
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.getByRole('button',{name:'项目：服务端已提交',exact:true})).toBeVisible();
  expect(await page.evaluate(()=>(window as any).__S1_TEST__.nodes.length)).toBe(1);
  const attempts=await page.evaluate(()=>(window as any).__S1_TEST__.requests.filter((r:any)=>r.action==='create'));
  expect(attempts).toHaveLength(2);
  expect(attempts[0].requestId).toBe(attempts[1].requestId);
});

test('@S1 pending mutation disables all editing controls',async ({page})=>{
  await mockServer(page);
  await page.evaluate(()=>{(window as any).__S1_TEST__.delay=500;});
  await page.getByRole('button',{name:'新建项目',exact:true}).click();
  await page.getByRole('textbox',{name:'名称',exact:true}).fill('等待项目');
  await page.getByRole('button',{name:'保存',exact:true}).click();
  await expect(page.getByRole('button',{name:'保存',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'取消',exact:true})).toBeDisabled();
  await expect(page.getByRole('textbox',{name:'名称',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'新建项目',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:'项目：等待项目',exact:true})).toBeVisible();
});

test('@S1 browser without injected backend clearly reports unavailable',async ({page})=>{
  await page.goto('/');
  await page.getByRole('button',{name:'项目管理',exact:true}).click();
  await expect(page.getByRole('alert')).toContainText('浏览器未连接原生项目存储');
  await expect(page.getByRole('button',{name:'新建项目',exact:true})).toBeDisabled();
  await expect(page.getByText('暂无项目。新建项目以开始。')).toBeHidden();
});
