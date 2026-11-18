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
