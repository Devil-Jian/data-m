import {expect,test} from '@playwright/test';

// S12 production root is intentionally different from isolated demo.
// This browser test uses an explicitly injected mock; it does not verify native IPC.
test.beforeEach(async ({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async (request:any)=>{
      if(request.action!=='list')throw {code:'INVALID_REQUEST'};
      return {nodes:[{id:'p-s0',kind:'project',parentId:null,name:'S0测试项目'}]};
    };
  });
  await page.goto('/');
  await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
});

test('@S0 desktop renderer uses styled shared project UI without demo injection',async ({page})=>{
  const node=page.getByRole('button',{name:'项目：S0测试项目'});
  await expect(node).toBeVisible();
  const actual=await node.evaluate(element=>{
    const style=getComputedStyle(element);
    return {display:style.display,padding:style.paddingTop,radius:style.borderRadius};
  });
  expect(actual.display).toBe('flex');
  expect(parseFloat(actual.padding)).toBeGreaterThan(0);
  expect(parseFloat(actual.radius)).toBeGreaterThan(0);
  await expect(page.getByRole('button',{name:'演示工作区'})).toHaveCount(0);
  await expect(page.getByText('ROS 活性氧荧光染色')).toHaveCount(0);
});

test('@S0 desktop project selection and keyboard focus use real ID navigation',async ({page})=>{
  const node=page.getByRole('button',{name:'项目：S0测试项目'});
  await node.focus();
  await page.keyboard.press('Enter');
  await expect(node).toHaveAttribute('aria-current','true');
  await expect(page.getByRole('navigation',{name:'科研位置'}).getByRole('button',{name:'S0测试项目'})).toHaveAttribute('aria-current','page');
});
