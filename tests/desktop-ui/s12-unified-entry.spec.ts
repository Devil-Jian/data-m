import {test,expect} from '@playwright/test';

// @S12 browser renderer smoke. This does not prove native IPC or Windows GUI acceptance.
test('@S12 production root never mounts demo projects',async ({page})=>{
  await page.goto('/');
  await expect(page.getByRole('navigation',{name:'正式科研工作区'})).toBeVisible();
  await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
  await expect(page.getByRole('button',{name:'演示工作区'})).toHaveCount(0);
  await expect(page.getByText('演示数据与文件交互，不代表真实入库')).toHaveCount(0);
});

test('@S12 real node IDs drive breadcrumbs, history and deleted-target fallback (explicit renderer mock)',async ({page})=>{
  await page.addInitScript(()=>{
    const state={nodes:[
      {id:'project-a',kind:'project',parentId:null,name:'氧化应激研究'},
      {id:'project-b',kind:'project',parentId:null,name:'另一项目'},
      {id:'theme-a',kind:'theme',parentId:'project-a',name:'氧化应激机制'},
      {id:'assay-ros',kind:'assay',parentId:'theme-a',name:'ROS'},
    ]};
    (window as any).__S12_TREE__=state;
    (window as any).__S1_PROJECT_TREE_MOCK__=async (request:any)=>{
      if(request.action!=='list')throw {code:'INVALID_REQUEST'};
      return {nodes:structuredClone(state.nodes)};
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'项目：氧化应激研究'}).click();
  await page.getByRole('button',{name:'主题：氧化应激机制'}).click();
  await page.getByRole('button',{name:'实验：ROS'}).click();
  const location=page.getByRole('navigation',{name:'科研位置'});
  await expect(location.getByRole('button',{name:'ROS'})).toHaveAttribute('aria-current','page');
  await location.getByRole('button',{name:'后退'}).click();
  await expect(location.getByRole('button',{name:'氧化应激机制'})).toHaveAttribute('aria-current','page');
  await page.keyboard.down('Alt');
  await page.keyboard.press('ArrowRight');
  await page.keyboard.up('Alt');
  await expect(location.getByRole('button',{name:'ROS'})).toHaveAttribute('aria-current','page');
  await page.evaluate(()=>{(window as any).__S12_TREE__.nodes=(window as any).__S12_TREE__.nodes.filter((n:any)=>n.id!=='assay-ros');});
  await page.getByRole('button',{name:'重新加载'}).click();
  await expect(page.getByRole('button',{name:'实验：ROS'})).toHaveCount(0);
  await expect(location.getByRole('button',{name:'ROS'})).toHaveCount(0);
});

test('@S12 safety module keeps the real project sidebar and restores project overview (renderer mock)',async ({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async (request:any)=>{
      if(request.action!=='list')throw {code:'INVALID_REQUEST'};
      return {nodes:[{id:'p12',kind:'project',parentId:null,name:'真实测试项目'}]};
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'数据完整性与备份恢复 (S7)'}).click();
  await expect(page.getByRole('complementary',{name:'科研项目导航'})).toBeVisible();
  await expect(page.getByRole('button',{name:'项目：真实测试项目'})).toBeVisible();
  await page.getByRole('button',{name:'项目概览'}).click();
  await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
});

test('@S12 saved project route restores only after backend confirms the real ID (renderer mock)',async ({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async ()=>({nodes:[
      {id:'persistent-p',kind:'project',parentId:null,name:'持久导航项目'},
      {id:'other-p',kind:'project',parentId:null,name:'另一项目'}
    ]});
  });
  await page.goto('/');
  await page.getByRole('button',{name:'项目：持久导航项目'}).click();
  await expect(page.getByRole('button',{name:'项目：持久导航项目'})).toHaveAttribute('aria-current','true');
  await page.reload();
  await expect(page.getByRole('button',{name:'项目：持久导航项目'})).toHaveAttribute('aria-current','true');
  // Invalid persisted preferences do not create phantom rows or a selected deleted node.
  await page.evaluate(()=>window.localStorage.setItem('data-m.s12.active-node','deleted-node'));
  await page.reload();
  await expect(page.getByRole('button',{name:'项目：持久导航项目'})).not.toHaveAttribute('aria-current','true');
  await expect(page.getByRole('button',{name:'项目：deleted-node'})).toHaveCount(0);
});

test('@S12 Paper project selector follows real project IDs without unmounting Paper (explicit IPC mock)',async ({page})=>{
  await page.addInitScript(()=>{
    const nodes=[
      {id:'p-one',kind:'project',parentId:null,name:'第一项目'},
      {id:'p-two',kind:'project',parentId:null,name:'第二项目'}
    ];
    (window as any).__S12_PAPER_NODES__=nodes;
    // Let the real desktop entry initialize through its explicit S1 browser
    // contract before injecting the S9-only IPC mock into this page.
    (window as any).__S1_PROJECT_TREE_MOCK__=async ()=>({nodes});
  });
  await page.goto('/');
  await page.getByRole('button',{name:'项目：第二项目'}).click();
  await page.evaluate(()=>{
    const nodes=(window as any).__S12_PAPER_NODES__;
    (window as any).__TAURI_INTERNALS__={
      invoke:async(command:string)=>{
        if(command==='project_tree')return {nodes};
        if(command==='paper_workspace')return {papers:[],candidates:[],candidateTotalCount:0,candidateTruncated:false};
        throw Error('Unexpected mock IPC: '+command);
      },
      transformCallback:()=>1,metadata:{currentWindow:{label:'main'}}
    };
  });
  await page.getByRole('button',{name:'论文管理 (S9)'}).click();
  const paper=page.getByLabel('S9 真实论文管理工作区');
  await expect(paper).toBeVisible();
  await expect(paper.getByRole('combobox',{name:'选择真实科研项目'})).toHaveValue('p-two');
  const location=page.getByRole('navigation',{name:'科研位置'});
  await expect(location.getByRole('button',{name:'第二项目'})).toHaveAttribute('aria-current','page');
  await paper.getByRole('combobox',{name:'选择真实科研项目'}).selectOption('p-one');
  await expect(page.getByRole('button',{name:'项目：第一项目'})).toHaveAttribute('aria-current','true');
  await expect(paper).toBeVisible();
  await expect(location.getByRole('button',{name:'第一项目'})).toHaveAttribute('aria-current','page');
});
