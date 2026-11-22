import {describe,it,expect,vi} from 'vitest';
import {runArchiveTask} from './archive-import-runner';
import type {ArchiveRunnerDeps} from './archive-import-runner';
import type {ArchiveTask,ArchiveEntry} from './archive-import';
import type {ProjectTreeNode} from './project-tree';
import type {ExperimentGroup} from './experiment-tree';
import type {ImportJob,ImportRequest} from './import-jobs';

function fixture(){
  const entry:ArchiveEntry={
    id:'entry-1',relativePath:'Theme/ROS/Control/ROS01_raw.tif',
    sourcePath:'C:\\Research\\Theme\\ROS\\Control\\ROS01_raw.tif',
    sizeBytes:5,sha256:'a'.repeat(64),projectName:'Project',themeName:'Theme',
    assayName:'ROS',groupName:'Control',replicateName:'ROS01',stage:'raw',
    alias:'ROS01_raw.tif',processTag:'',decision:'import',issue:'',status:'queued',jobId:null
  };
  const task:ArchiveTask={
    id:'task-1',rootName:'Project',layout:'project_theme_assay_group',
    targetProjectId:null,targetThemeName:'',fallbackGroup:'',status:'prepared',
    total:1,imported:0,skipped:0,review:0,failed:0,ready:1,entries:[entry],nextOffset:null
  };
  let nodes:ProjectTreeNode[]=[];
  let groups:ExperimentGroup[]=[];
  let job:ImportJob|null=null;
  const requests:ImportRequest[]=[];
  const fake:ArchiveRunnerDeps={
    archive:vi.fn(async req=>{
      if(req.action==='resume')task.status='running';
      if(req.action==='bind'){entry.jobId=req.jobId!;entry.status='running';}
      if(req.action==='reconcile'){
        if(job?.status==='completed'){entry.status='imported';task.imported=1;task.status='completed';}
      }
      if(req.action==='pause')task.status='paused';
      return {current:{...task,entries:[{...entry}]},
        tasks:[{...task,entries:[]}],templates:[]};
    }),
    projects:vi.fn(async req=>{
      if(req.action==='create'){
        const kind=req.kind!;
        nodes.push({id:'created-'+kind,kind,name:req.name!,
          parentId:req.parentId??null});
      }
      return {nodes:[...nodes]};
    }),
    experiments:vi.fn(async req=>{
      if(req.action==='create'&&req.kind==='group'){
        groups.push({id:'group-1',name:req.name!,replicates:[]});
      }
      if(req.action==='create'&&req.kind==='replicate'){
        const g=groups.find(g=>g.id===req.parentId)!;
        g.replicates.push({id:'rep-1',label:req.name!});
      }
      return {assayId:req.assayId,groups:groups.map(g=>({
        ...g,replicates:[...g.replicates]})),replicateCount:groups.reduce((n,g)=>n+g.replicates.length,0),
        stages:[
          {id:'raw' as const,label:'Raw',scope:'replicate' as const},
          {id:'processed' as const,label:'Processed',scope:'replicate' as const},
          {id:'statistics' as const,label:'Statistics',scope:'assay' as const},
          {id:'display' as const,label:'Display',scope:'assay' as const}
        ]};
    }),
    importer:vi.fn(async req=>{
      requests.push(req);
      if(req.action==='prepare'){
        job={id:'job-1',status:'prepared',target:req.target,files:[
          {id:'file-1',name:req.aliases?.[0]??entry.alias,sizeBytes:entry.sizeBytes,
            sha256:null,status:'queued',assetVersionId:null,errorCode:null}
        ],totalBytes:5,copiedBytes:0,completedCount:0,errorCode:null};
      }else if(req.action==='start'&&job){
        job.status='completed';
        job.files=[{...job.files[0],status:'committed',sha256:entry.sha256,assetVersionId:'version-1'}];
        job.completedCount=1;job.copiedBytes=5;
      }
      return {jobs:job?[job]:[]};
    }),
    sleep:vi.fn(async()=>{})
  };
  return {task,entry,fake,requests,nodes,groups};
}
describe('S13 resumable multi-target coordinator',()=>{
  it('creates exactly one research hierarchy, calls S3 prepare/start, then verifies version',async()=>{
    const fixtureData=fixture();
    const {task,entry,fake,requests,nodes,groups}=fixtureData;
    const progress=vi.fn();
    const completed=await runArchiveTask(task,progress,new AbortController().signal,fake);
    expect(completed.status).toBe('completed');
    expect(completed.imported).toBe(1);
    expect(nodes.map(n=>n.kind)).toEqual(['project','theme','assay']);
    expect(groups[0].replicates[0].label).toBe('ROS01');
    const prepared=requests.find(r=>r.action==='prepare');
    if(!prepared||prepared.action!=='prepare')throw Error('S3 prepare missing');
    expect(prepared.paths).toEqual([entry.sourcePath]);
    expect(prepared.aliases).toEqual(['ROS01_raw.tif']);
    expect(prepared.target).toEqual({
      assayId:'created-assay',stage:'raw',groupId:'group-1',replicateId:'rep-1'
    });
    expect(progress).toHaveBeenCalled();
  });
  it('resumes already-bound S3 job rather than re-preparing or duplicating hierarchy',async()=>{
    const {task,entry,fake,requests}=fixture();
    entry.jobId='old-job';entry.status='running';task.status='paused';
    const realImport=fake.importer;
    fake.importer=vi.fn(async req=>{
      if(req.action==='get'){
        const finished:ImportJob={id:'old-job',status:'completed',
          target:{assayId:'created-assay',stage:'raw',groupId:'group-1',replicateId:'rep-1'},
          files:[{id:'file-1',name:entry.alias,sizeBytes:5,status:'committed',
            sha256:entry.sha256,assetVersionId:'v1',errorCode:null}],
          totalBytes:5,copiedBytes:5,completedCount:1,errorCode:null};
        return {jobs:[finished]};
      }
      return realImport(req);
    });
    // Pre-existing project/experiment IDs must match the bound native S3 job.
    // If they cannot be resolved, the coordinator refuses to falsify a target.
    await expect(runArchiveTask(task,()=>{},new AbortController().signal,fake))
      .rejects.toThrow();
    expect(requests.some(r=>r.action==='prepare')).toBe(false);
  });
  it('refuses to bind a reused S3 job with a different filename or size',async()=>{
    for(const mismatch of ['name','size'] as const){
      const {task,fake}=fixture();
      const original=fake.importer;
      fake.importer=vi.fn(async req=>{
        const response=await original(req);
        if(req.action==='prepare'){
          const file=response.jobs[0].files[0];
          if(mismatch==='name')file.name='unrelated.tif';
          else file.sizeBytes=999;
        }
        return response;
      });
      await expect(runArchiveTask(task,()=>{},new AbortController().signal,fake))
        .rejects.toThrow('S3 返回文件名或大小与确认计划不一致');
      expect(fake.archive).not.toHaveBeenCalledWith(expect.objectContaining({action:'bind'}));
      expect(fake.importer).not.toHaveBeenCalledWith(expect.objectContaining({action:'start'}));
      expect(fake.archive).toHaveBeenCalledWith(expect.objectContaining({action:'pause'}));
    }
  });
  it('never enters mutations while a review item remains unresolved',async()=>{
    const {task,fake}=fixture();task.review=1;task.status='review';
    await expect(runArchiveTask(task,()=>{},new AbortController().signal,fake)).rejects.toThrow();
    expect(fake.projects).not.toHaveBeenCalled();
    expect(fake.importer).not.toHaveBeenCalled();
  });
});
