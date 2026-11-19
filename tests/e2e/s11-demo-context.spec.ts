import {expect,test} from '@playwright/test';

// Preserved S11 mock Explorer context-menu regression, now isolated to the demo renderer.
test('@S11 nested file action right-click repeatedly opens context menu without retry',async ({page})=>{
  await page.goto('/');
  await page.getByRole('button',{name:'分阶段'}).click();
  await page.getByRole('button',{name:/04_Display/}).first().click();
  const file=page.locator('[data-explorer-id^="asset:display:"]').first();
  await expect(file).toBeVisible();
  for(let attempt=0;attempt<5;attempt++){
    const nested=file.locator('button').first();
    if(await nested.count())await nested.click({button:'right'});
    else await file.click({button:'right'});
    await expect(page.getByRole('menu',{name:'文件操作菜单'})).toBeVisible();
    await expect(page.getByRole('menuitem',{name:/重命名/})).toBeVisible();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('menu',{name:'文件操作菜单'})).toHaveCount(0);
  }
});
