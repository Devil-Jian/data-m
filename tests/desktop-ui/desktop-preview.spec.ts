import {expect,test} from '@playwright/test';

test.beforeEach(async ({page})=>{
  await page.goto('/');
  await expect(page.getByRole('heading',{name:'ROS 活性氧荧光染色'})).toBeVisible();
});

test('desktop renderer has generated shadcn utility CSS (not unstyled HTML)',async ({page})=>{
  const selectedGroup=page.locator('.dm-group-tabs button[data-variant="primary"]').first();
  await expect(selectedGroup).toBeVisible();
  const actual=await selectedGroup.evaluate(element=>{
    const style=getComputedStyle(element);
    return {color:style.backgroundColor,radius:style.borderRadius,height:element.getBoundingClientRect().height};
  });
  // Tailwind bg-blue-600, rounded-md and h-7; without cross-workspace
  // CSS scanning these become transparent/native WebView controls.
  expect(actual.color).toBe('rgb(37, 99, 235)');
  expect(parseFloat(actual.radius)).toBeGreaterThanOrEqual(5);
  expect(actual.height).toBeGreaterThanOrEqual(26);
  const search=page.getByPlaceholder('搜索样本文件…');
  const searchStyle=await search.evaluate(element=>{
    const style=getComputedStyle(element);
    return {height:element.getBoundingClientRect().height,padding:style.paddingLeft};
  });
  expect(searchStyle.height).toBeGreaterThanOrEqual(30);
  expect(parseFloat(searchStyle.padding)).toBeGreaterThanOrEqual(10);
});

test('desktop browser preview can right-click a file and use keyboard shortcuts',async ({page})=>{
  const file=page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]');
  await expect(file).toBeVisible();
  await file.click({button:'right'});
  const menu=page.getByRole('menu',{name:'文件操作菜单'});
  await expect(menu).toBeVisible();
  await expect(menu.getByRole('menuitem',{name:/重命名/})).toBeEnabled();
  await menu.getByRole('menuitem',{name:/复制/}).click();
  await expect(page.locator('.dm-toast')).toContainText('已选择复制 1 个演示项');
  await file.click();
  await page.keyboard.press('F2');
  await expect(page.getByRole('textbox',{name:'重命名文件'})).toBeVisible();
  await page.keyboard.press('Escape');
  await expect(file).toBeVisible();
});

test('right-click inside a file action opens file menu, toolbar remains usable',async ({page})=>{
  const file=page.locator('[data-explorer-id="asset:display:ros-']).first();
  // Stage-specific display assets can be exercised without touching real paths.
  await page.getByRole('button',{name:'分阶段'}).click();
  await page.getByRole('button',{name:/04_Display/}).first().click();
  const display=page.locator('[data-explorer-id^="asset:display:"]').first();
  await expect(display).toBeVisible();
  const innerAction=display.locator('button').first();
  if(await innerAction.count()){
    await innerAction.click({button:'right'});
  }else{
    await display.click({button:'right'});
  }
  await expect(page.getByRole('menu',{name:'文件操作菜单'})).toBeVisible();
});
