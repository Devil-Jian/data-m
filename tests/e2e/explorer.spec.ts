import {expect,test} from '@playwright/test';

test.beforeEach(async ({page})=>{
  await page.goto('/');
  await expect(page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]')).toBeVisible();
});

test('Ctrl toggles individual Asset IDs; Shift selects an ordered range',async ({page})=>{
  const raw=page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]');
  const processed=page.locator('[data-explorer-id="asset:processed:ros-g1-r1"]');
  const second=page.locator('[data-explorer-id="asset:processed:ros-g1-r2"]');
  await raw.click();
  await processed.click({modifiers:['Control']});
  await expect(raw).toHaveClass(/selected/);
  await expect(processed).toHaveClass(/selected/);
  await processed.click({modifiers:['Control']});
  await expect(processed).not.toHaveClass(/selected/);
  await second.click({modifiers:['Shift']});
  // The most recent Ctrl-click moved the selection anchor to the processed file.
  await expect(page.locator('[data-explorer-id="asset:raw:ros-g1-r2"]')).toHaveClass(/selected/);
  await expect(second).toHaveClass(/selected/);
});

test('selected files drag together and do not create biological replicates',async ({page})=>{
  const raw=page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]');
  const processed=page.locator('[data-explorer-id="asset:processed:ros-g1-r1"]');
  const destination=page.locator('[data-drop-container-id="ros-g2"]');
  await raw.click();
  await processed.click({modifiers:['Control']});
  const from=await raw.boundingBox(),to=await destination.boundingBox();
  expect(from).not.toBeNull();expect(to).not.toBeNull();
  await page.mouse.move(from!.x+from!.width/2,from!.y+from!.height/2);
  await page.mouse.down();
  await page.mouse.move(to!.x+to!.width/2,to!.y+to!.height/2,{steps:15});
  await page.mouse.up();
  await expect(page.locator('.dm-toast')).toContainText('2 个文件');
  await expect(page.locator('.dm-group-count').first()).toHaveText('6');
  await expect(raw).toHaveClass(/selected/);
  await expect(processed).toHaveClass(/selected/);
});

test('marquee starts from workspace whitespace and selects file assets',async ({page})=>{
  const root=page.locator('.dm-explorer-canvas');
  const raw=page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]');
  const canvas=await root.boundingBox(),asset=await raw.boundingBox();
  expect(canvas).not.toBeNull();expect(asset).not.toBeNull();
  const startX=canvas!.x+7,startY=asset!.y+asset!.height/2;
  await page.mouse.move(startX,startY);
  await page.mouse.down();
  await page.mouse.move(asset!.x+asset!.width/2,startY,{steps:12});
  await page.mouse.up();
  await expect(raw).toHaveClass(/selected/);
});

test('edge marquee autoscroll advances the workbench scroll position',async ({page})=>{
  await page.setViewportSize({width:1200,height:580});
  const root=page.locator('.dm-explorer-canvas');
  const box=await root.boundingBox();
  expect(box).not.toBeNull();
  const height=await root.evaluate(el=>el.scrollHeight-el.clientHeight);
  expect(height).toBeGreaterThan(0);
  const left=box!.x+6;
  await page.mouse.move(left,box!.y+68);
  await page.mouse.down();
  await page.mouse.move(left+40,box!.y+box!.height-3,{steps:10});
  await expect.poll(async ()=>root.evaluate(el=>el.scrollTop),{timeout:5_000})
    .toBeGreaterThan(0);
  await page.mouse.up();
});

test('Escape cancels pending drag without submitting operation',async ({page})=>{
  const raw=page.locator('[data-explorer-id="asset:raw:ros-g1-r1"]');
  const target=page.locator('[data-drop-container-id="ros-g2"]');
  const a=await raw.boundingBox(),b=await target.boundingBox();
  expect(a).not.toBeNull();expect(b).not.toBeNull();
  await page.mouse.move(a!.x+a!.width/2,a!.y+a!.height/2);
  await page.mouse.down();
  await page.mouse.move(b!.x+b!.width/2,b!.y+b!.height/2,{steps:10});
  await page.keyboard.press('Escape');
  await page.mouse.up();
  await expect(page.locator('.dm-toast')).toHaveCount(0);
});
