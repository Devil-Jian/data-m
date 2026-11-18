import {test,expect,type Page} from '@playwright/test';

// This explicit browser mock persists ONLY mock backend jobs in sessionStorage
// so a renderer reload can exercise recovery UI. No native persistence is claimed.
async function setup(page:Page,{behavior='complete',unavailable=false}={}){
  await page.addInitScript(({behavior,unavailable})=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async ()=>({nodes:[
      {id:'p',kind:'project',parentId:null,name:'导入测试项目'},
      {id:'t',kind:'theme',parentId:'p',name:'导入测试主题'},
      {id:'a',kind:'assay',parentId:'t',name:'导入测试实验'},
    ]});
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async ()=>({assayId:'a',groups:[{id:'g',name:'对照组',replicates:[{id:'r1',label:'R1'},{id:'r2',label:'R2'}]}],
      stages:[{id:'raw',label:'Raw',scope:'replicate'},{id:'processed',label:'Processed',scope:'replicate'},{id:'statistics',label:'Statistics',scope:'assay'},{id:'display',label:'Display',scope:'assay'}],replicateCount:2});
    const persisted=JSON.parse(sessionStorage.getItem('s3-explicit-mock-jobs')??'[]');
    const state={jobs:persisted as any[],requests:[] as any[],behavior,prepareFailure:false,next:persisted.length+1};
    (window as any).__S3_TEST__=state;
    if(unavailable)return;
    const persist=()=>sessionStorage.setItem('s3-explicit-mock-jobs',JSON.stringify(state.jobs));
    const reply=(job?:any)=>({jobs:structuredClone(job?[job]:state.jobs)});
    const commit=(job:any,file:any)=>{file.status='committed';file.sha256='a'.repeat(64);file.assetVersionId=`version-${file.id}`;file.errorCode=null;
      job.completedCount=job.files.filter((f:any)=>f.status==='committed').length;job.copiedBytes=job.files.filter((f:any)=>f.status==='committed').reduce((n:number,f:any)=>n+f.sizeBytes,0);};
    (window as any).__S3_IMPORTS_MOCK__=async (request:any)=>{
      state.requests.push(structuredClone(request));
      if(request.action==='list')return reply();
      if(request.action==='prepare'){
        if(state.prepareFailure){state.prepareFailure=false;throw {code:'SOURCE_CHANGED',message:'C:\\private\\sensitive-source.dat'};}
        const old=state.jobs.find(j=>j.requestId===request.requestId);
        if(old)return reply(old);
        const id=`job-${state.next++}`;
        const job={id,status:'prepared',target:request.target,requestId:request.requestId,ticks:0,
          files:request.paths.map((path:string,index:number)=>({id:`${id}-f${index}`,name:path.split(/[\\/]/).at(-1),sizeBytes:100,sha256:null,status:'queued',assetVersionId:null,errorCode:null})),
          totalBytes:request.paths.length*100,copiedBytes:0,completedCount:0,errorCode:null};
        state.jobs.push(job);persist();return reply(job);
      }
      const job=state.jobs.find(j=>j.id===request.jobId);
      if(!job)throw {code:'NOT_FOUND'};
      if(request.action==='start'||request.action==='retry'){
        if(request.action==='retry')state.behavior='complete';
        job.status='running';job.errorCode=null;job.ticks=0;
        for(const file of job.files)if(file.status!=='committed'){file.status='queued';file.errorCode=null;}
      }
      if(request.action==='cancel'){
        job.status=job.completedCount===job.files.length?'completed':'cancelled';
        for(const file of job.files)if(file.status!=='committed')file.status='cancelled';
      }
      if(request.action==='get'&&job.status==='running'){
        job.ticks++;
        if(state.behavior==='invalid_completed'){
          job.status='completed';
        }else if(state.behavior==='cancel'){
          commit(job,job.files[0]);
        }else if(state.behavior==='partial'||state.behavior==='changed'){
          if(state.behavior==='partial')commit(job,job.files[0]);
          const failed=job.files.find((file:any)=>file.status!=='committed');
          failed.status='failed';failed.errorCode='SOURCE_CHANGED';job.status='failed';job.errorCode='SOURCE_CHANGED';
        }else if(job.ticks>=2){
          for(const file of job.files)commit(job,file);
          job.status='completed';
        }else{
          const file=job.files.find((f:any)=>f.status!=='committed');
          if(file)file.status='copying';
          job.copiedBytes=Math.min(job.totalBytes,job.copiedBytes+50);
        }
      }
      persist();return reply(job);
    };
  },{behavior,unavailable});
  await page.goto('/');
  await enter(page);
}
async function enter(page:Page){
  await page.getByRole('button',{name:'实验：导入测试实验',exact:true}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await page.getByRole('button',{name:'样本：R1',exact:true}).click();
  await page.getByRole('button',{name:'安全导入（测试库）',exact:true}).click();
  await expect(page.getByRole('region',{name:'安全导入任务'})).toBeVisible();
}
async function drop(page:Page,count=2){
  await page.evaluate(count=>window.dispatchEvent(new CustomEvent('data-m-test-import-drop',{detail:{paths:Array.from({length:count},(_,i)=>`C:\\discardable-test-source\\file-${i+1}.dat`)}})),count);
}
const panel=(page:Page)=>page.getByRole('region',{name:'安全导入任务'});

test('@S3 drop only prepares; explicit confirmation imports 100 files and preserves biological n',async ({page})=>{
  await setup(page);
  await expect(panel(page).getByText('浏览器 Mock（不写入原生数据库）',{exact:true})).toBeVisible();
  await drop(page,100);
  await expect(panel(page).getByText('待确认',{exact:true})).toBeVisible();
  await expect(panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true})).toBeEnabled();
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.filter((r:any)=>r.action==='start').length)).toBe(0);
  await expect(page.getByRole('button',{name:'返回项目管理',exact:true})).toBeDisabled();
  await expect(page.getByRole('button',{name:/^Processed /})).toBeDisabled();
  await expect(page.getByRole('button',{name:'样本：R2',exact:true})).toBeDisabled();
  await expect(panel(page)).toContainText('file-100.dat');
  await expect(page.locator('body')).not.toContainText('discardable-test-source');
  await panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true}).click();
  await expect(panel(page).getByText('已完成',{exact:true})).toBeVisible();
  await expect(panel(page)).toContainText('已登记 100 / 100 个文件');
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 2');
  await expect(page.getByRole('button',{name:'返回项目管理',exact:true})).toBeEnabled();
  const prepare=await page.evaluate(()=>(window as any).__S3_TEST__.requests.find((r:any)=>r.action==='prepare'));
  expect(prepare.target).toEqual({assayId:'a',stage:'raw',groupId:'g',replicateId:'r1'});
  expect(prepare.requestId).toMatch(/^[\da-f-]{36}$/i);
});

test('@S3 cancellation preserves committed count and retry imports only unfinished files',async ({page})=>{
  await setup(page,{behavior:'cancel'});
  await drop(page,3);
  await panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true}).click();
  await expect(panel(page)).toContainText('已登记 1 / 3 个文件');
  await expect(panel(page).getByRole('button',{name:'取消任务',exact:true})).toBeEnabled();
  await panel(page).getByRole('button',{name:'取消任务',exact:true}).click();
  await expect(panel(page).getByText('已取消',{exact:true}).first()).toBeVisible();
  await expect(panel(page)).toContainText('已登记 1 / 3 个文件');
  const firstVersion=await page.evaluate(()=>(window as any).__S3_TEST__.jobs[0].files[0].assetVersionId);
  await panel(page).getByRole('button',{name:'重试未完成文件',exact:true}).click();
  await expect(panel(page).getByText('已完成',{exact:true})).toBeVisible();
  await expect(panel(page)).toContainText('已登记 3 / 3 个文件');
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.jobs[0].files[0].assetVersionId)).toBe(firstVersion);
});

test('@S3 partial failure shows committed count and safe per-file source-change detail',async ({page})=>{
  await setup(page,{behavior:'partial'});
  await drop(page,3);
  await panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true}).click();
  await expect(panel(page)).toContainText('已登记 1 / 3 个文件');
  await expect(panel(page)).toContainText('源文件已变化');
  await expect(panel(page).getByText('已完成',{exact:true})).toBeHidden();
  await expect(panel(page).getByRole('button',{name:'重试未完成文件',exact:true})).toBeEnabled();
  await expect(page.getByRole('status',{name:'实验样本统计'})).toContainText('n = 2');
});

test('@S3 rejected preparation exposes no path and never starts or claims completion',async ({page})=>{
  await setup(page);
  await page.evaluate(()=>{(window as any).__S3_TEST__.prepareFailure=true;});
  await drop(page);
  await expect(panel(page).getByRole('alert')).toContainText('源文件已变化');
  await expect(page.locator('body')).not.toContainText('sensitive-source');
  await expect(panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true})).toBeHidden();
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.filter((r:any)=>r.action==='start').length)).toBe(0);
});

test('@S3 persisted jobs reload after leaving workspace and renderer restart',async ({page})=>{
  await setup(page);
  await drop(page);
  await panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true}).click();
  await expect(panel(page).getByText('已完成',{exact:true})).toBeVisible();
  await panel(page).getByRole('button',{name:'关闭导入任务',exact:true}).click();
  await page.getByRole('button',{name:'返回项目管理',exact:true}).click();
  await page.getByRole('button',{name:'分组与样本',exact:true}).click();
  await page.getByRole('button',{name:'安全导入（测试库）',exact:true}).click();
  await expect(panel(page)).toContainText('已登记 2 / 2 个文件');
  await page.reload();
  await enter(page);
  await expect(panel(page)).toContainText('已登记 2 / 2 个文件');
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.filter((r:any)=>r.action==='prepare').length)).toBe(0);
});

test('@S3 assay-level target omits sample/group; invalid raw target cannot prepare',async ({page})=>{
  await setup(page);
  await panel(page).getByRole('button',{name:'关闭导入任务',exact:true}).click();
  await page.getByRole('button',{name:'分组：对照组',exact:true}).click();
  await page.getByRole('button',{name:'安全导入（测试库）',exact:true}).click();
  await drop(page);
  await expect(panel(page).getByRole('alert')).toContainText('选择样本');
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.filter((r:any)=>r.action==='prepare').length)).toBe(0);
  await page.getByRole('button',{name:/^Statistics /}).click();
  await drop(page,1);
  await expect(panel(page).getByText('待确认',{exact:true})).toBeVisible();
  const prepare=await page.evaluate(()=>(window as any).__S3_TEST__.requests.find((r:any)=>r.action==='prepare'));
  expect(prepare.target).toEqual({assayId:'a',stage:'statistics'});
  await panel(page).getByRole('button',{name:'取消任务',exact:true}).click();
});

test('@S3 normal browser has no native backend and ignores synthetic drop hooks',async ({page})=>{
  await setup(page,{unavailable:true});
  await expect(panel(page).getByRole('alert')).toContainText('浏览器未连接原生导入服务');
  await drop(page);
  await expect(panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true})).toBeHidden();
  await expect(panel(page).getByText('已完成',{exact:true})).toBeHidden();
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.length)).toBe(0);
});

test('@S3 contradictory completed reply cannot claim success and still permits cancellation',async ({page})=>{
  await setup(page,{behavior:'invalid_completed'});
  await drop(page);
  await panel(page).getByRole('button',{name:'确认导入（仅限可丢弃测试文件）',exact:true}).click();
  await expect(panel(page).getByRole('alert')).toContainText('尚未确认完成');
  await expect(panel(page).getByText('已完成',{exact:true})).toBeHidden();
  await expect(panel(page)).toContainText('已登记 0 / 2 个文件');
  await expect(panel(page).getByRole('button',{name:'取消任务',exact:true})).toBeEnabled();
  await panel(page).getByRole('button',{name:'取消任务',exact:true}).click();
  await expect(panel(page).getByText('已取消',{exact:true}).first()).toBeVisible();
  await expect(page.getByRole('button',{name:'返回项目管理',exact:true})).toBeEnabled();
});

test('@S3 restored prepared job retains its fixed target and requires explicit confirmation',async ({page})=>{
  await setup(page);
  await page.getByRole('button',{name:/^Statistics /}).click();
  await drop(page,1);
  await expect(panel(page).getByText('待确认',{exact:true})).toBeVisible();
  await page.reload();
  await enter(page);
  await expect(panel(page)).toContainText('待处理任务固定目标：导入测试实验 / STATISTICS');
  await expect(panel(page).getByText('待确认',{exact:true})).toBeVisible();
  expect(await page.evaluate(()=>(window as any).__S3_TEST__.requests.filter((r:any)=>r.action==='start').length)).toBe(0);
  await expect(page.getByRole('button',{name:'演示工作区',exact:true})).toHaveCount(0);
  await panel(page).getByRole('button',{name:'取消任务',exact:true}).click();
});
