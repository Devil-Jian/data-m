import {test,expect} from '@playwright/test';

// @S12 browser renderer smoke. This does not prove native IPC or Windows GUI acceptance.
test('@S12 production root never mounts demo projects',async ({page})=>{
  await page.goto('/');
  await expect(page.getByRole('navigation',{name:'正式科研工作区'})).toBeVisible();
  await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
  await expect(page.getByRole('button',{name:'演示工作区'})).toHaveCount(0);
  await expect(page.getByText('演示数据与文件交互，不代表真实入库')).toHaveCount(0);
});
