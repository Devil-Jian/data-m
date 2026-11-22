import {test,expect} from '@playwright/test';

test('@S13 existing nested archive is scanned and ambiguous quant files require explicit review',async({page})=>{
  await page.addInitScript(()=>{
    (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[]});
    (window as any).__S13_PICK_FOLDER_MOCK__=async()=> 'C:\\ArchiveProject';
    const entry={
      id:'entry-1',relativePath:'Theme/ROS/Control/ROS01_quant.csv',
      sourcePath:'C:\\ArchiveProject\\Theme\\ROS\\Control\\ROS01_quant.csv',
      sizeBytes:4,sha256:'a'.repeat(64),projectName:'ArchiveProject',themeName:'Theme',
      assayName:'ROS',groupName:'Control',replicateName:'ROS01',stage:'review',
      processTag:'',alias:'ROS01_quant.csv',decision:'review',issue:'quant file: ambiguous stage',
      status:'queued',jobId:null
    };
    const task={
      id:'archive-1',rootName:'ArchiveProject',layout:'project_theme_assay_group',
      targetProjectId:null,targetThemeName:'',fallbackGroup:'',status:'scanned',
      total:1,imported:0,skipped:0,review:1,failed:0,ready:0,
      entries:[entry],nextOffset:null
    };
    (window as any).__S13_ARCHIVE_MOCK__=async(req:any)=>{
      if(req.action==='edit'){
        Object.assign(entry,req,{issue:''});
        entry.decision=req.decision;task.status='scanned';
        task.review=req.decision==='review'?1:0;
      }
      if(req.action==='prepare'){
        task.status=task.review>0?'review':'prepared';
      }
      return {current:req.action==='list'?null:{...task,entries:[{...entry}]},
        tasks:[{...task,entries:[]}],templates:[]};
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'智能导入文件夹'}).click();
  const wizard=page.getByRole('dialog',{name:'智能归档文件夹导入'});
  await expect(wizard).toBeVisible();
  await wizard.getByRole('button',{name:'选择本地文件夹'}).click();
  await expect(wizard.getByText('ROS01_quant.csv',{exact:false}).first()).toBeVisible();
  await expect(wizard.getByText(/1 待确认/)).toBeVisible();
  await wizard.getByRole('button',{name:'校对'}).click();
  const edit=page.getByRole('dialog',{name:'校对导入映射'});
  await edit.getByRole('combobox',{name:'当前文件阶段'}).click();
  await page.getByRole('option',{name:/Statistics/}).click();
  await edit.getByRole('button',{name:'保存映射'}).click();
  await wizard.getByRole('button',{name:'检查冲突与源文件'}).click();
  await expect(wizard.getByRole('button',{name:'确认批量导入'})).toBeEnabled();
});

test('@S13 full archive workflow creates real scope once, commits through S3 and opens Workbench',async({page})=>{
  await page.addInitScript(()=>{
    const sha='b'.repeat(64);
    const nodes:any[]=[];
    const groups:any[]=[];
    const entry:any={
      id:'entry-1',relativePath:'Theme/ROS/Control/ROS01_raw.tif',
      sourcePath:'C:\\ArchiveProject\\Theme\\ROS\\Control\\ROS01_raw.tif',
      sizeBytes:5,sha256:sha,projectName:'ArchiveProject',themeName:'Theme',
      assayName:'ROS',groupName:'Control',replicateName:'ROS01',stage:'raw',processTag:'',
      alias:'ROS01_raw.tif',decision:'import',issue:'',status:'queued',jobId:null
    };
    const task:any={
      id:'archive-1',rootName:'ArchiveProject',layout:'project_theme_assay_group',
      targetProjectId:null,targetThemeName:'',fallbackGroup:'',status:'scanned',
      total:1,imported:0,skipped:0,review:0,failed:0,ready:1,
      entries:[entry],nextOffset:null
    };
    let nativeJob:any=null;
    (window as any).__S13_PICK_FOLDER_MOCK__=async()=> 'C:\\ArchiveProject';
    (window as any).__S13_ARCHIVE_MOCK__=async(req:any)=>{
      if(req.action==='prepare')task.status='prepared';
      if(req.action==='resume')task.status='running';
      if(req.action==='bind'){entry.jobId=req.jobId;entry.status='running';}
      if(req.action==='reconcile'&&nativeJob?.status==='completed'){
        entry.status='imported';task.imported=1;task.status='completed';
      }
      return {current:req.action==='list'?null:{...task,entries:[{...entry}]},
        tasks:[{...task,entries:[]}],templates:[]};
    };
    (window as any).__S1_PROJECT_TREE_MOCK__=async(req:any)=>{
      if(req.action==='create'){
        nodes.push({id:req.kind+'-1',name:req.name,kind:req.kind,parentId:req.parentId??null});
      }
      return {nodes:[...nodes]};
    };
    (window as any).__S2_EXPERIMENT_TREE_MOCK__=async(req:any)=>{
      if(req.action==='create'&&req.kind==='group')
        groups.push({id:'group-1',name:req.name,replicates:[]});
      if(req.action==='create'&&req.kind==='replicate')
        groups[0].replicates.push({id:'rep-1',label:req.name});
      return {assayId:req.assayId,replicateCount:groups.reduce((n,g)=>n+g.replicates.length,0),
        groups:groups.map(g=>({...g,replicates:[...g.replicates]})),
        stages:[
          {id:'raw',label:'Raw',scope:'replicate'},
          {id:'processed',label:'Processed',scope:'replicate'},
          {id:'statistics',label:'Statistics',scope:'assay'},
          {id:'display',label:'Display',scope:'assay'}
        ]};
    };
    (window as any).__S3_IMPORTS_MOCK__=async(req:any)=>{
      (window as any).__S13_REQUESTS__??=[];
      (window as any).__S13_REQUESTS__.push(req);
      if(req.action==='prepare')nativeJob={
        id:'job-1',status:'prepared',target:req.target,
        files:[{id:'file-1',name:'ROS01_raw.tif',sizeBytes:5,
          sha256:null,status:'queued',assetVersionId:null,errorCode:null}],
        totalBytes:5,copiedBytes:0,completedCount:0,errorCode:null
      };
      if(req.action==='start'){
        nativeJob.status='completed';nativeJob.completedCount=1;nativeJob.copiedBytes=5;
        nativeJob.files[0]={...nativeJob.files[0],status:'committed',sha256:sha,
          assetVersionId:'version-1'};
      }
      return {jobs:nativeJob?[nativeJob]:[]};
    };
    (window as any).__S4_ASSETS_MOCK__={
      list:async()=>({items:[],total:0,nextCursor:null}),detail:async()=>null
    };
  });
  await page.goto('/');
  await page.getByRole('button',{name:'智能导入文件夹'}).click();
  const wizard=page.getByRole('dialog',{name:'智能归档文件夹导入'});
  await wizard.getByRole('button',{name:'选择本地文件夹'}).click();
  await wizard.getByRole('button',{name:'检查冲突与源文件'}).click();
  await wizard.getByRole('button',{name:'确认批量导入'}).click();
  await expect(wizard).toHaveCount(0);
  await expect(page.getByRole('heading',{name:'ROS'})).toBeVisible();
  const requests=await page.evaluate(()=>(window as any).__S13_REQUESTS__);
  const prepared=requests.find((r:any)=>r.action==='prepare');
  expect(prepared.target).toEqual({
    assayId:'assay-1',groupId:'group-1',replicateId:'rep-1',stage:'raw'
  });
  expect(prepared.aliases).toEqual(['ROS01_raw.tif']);
  expect(requests.some((r:any)=>r.action==='start')).toBe(true);
});
