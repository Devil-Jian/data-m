import {describe,it,expect,vi} from 'vitest';
import {runArchiveTask} from './archive-import-runner';
import type {ArchiveRunnerDeps} from './archive-import-runner';
import type {ArchiveEntry,ArchiveTask} from './archive-import';
import type {ImportJob} from './import-jobs';

// S14's test case is exactly the two-experiment research corpus (excluding
// four separately reviewed project-level paper/figure documents). Nothing
// in this test asserts or creates any Paper/Panel link.
function makeCorpus(){
  const entries:ArchiveEntry[]=[];
  const groups=[['Control','C'],['Model','M'],['Treatment','T']] as const;
  const assays=[['01_ROS检测','ROS'],['02_WesternBlot','WB'],['03_细胞活力','CV']] as const;
  const stages=[['raw_data','raw','raw','tif'],['process_data','processed','process','tif'],
    ['statistics_data','statistics','quant','png']] as const;
  let serial=0;
  for(let ei=1;ei<=2;ei++){
    for(const [assay,ac] of assays){
      for(const [group,gc] of groups){
        for(let ri=1;ri<=6;ri++){
          const sample=`E${ei}_${ac}_${gc}${String(ri).padStart(2,'0')}`;
          for(const [folder,stage,suffix,ext] of stages){
            const name=`${sample}_${suffix}.${ext}`;
            const relativePath=`exp_data/实验${ei}/${assay}/${group}/${folder}/${name}`;
            entries.push({
              id:`entry-${++serial}`,relativePath,sourcePath:`C:\\Research\\${relativePath}`,
              sizeBytes:serial,sha256:serial.toString(16).padStart(64,'0'),
              projectName:'DataM_Research_Test',themeName:`实验${ei}`,assayName:assay,
              groupName:group,replicateName:sample,stage,decision:'import',
              processTag:stage==='statistics'?'quant':'',alias:name,issue:'',
              status:'queued',jobId:null
            });
          }
        }
        const show=`E${ei}_${ac}_${group}_show.png`;
        const relativePath=`exp_data/实验${ei}/${assay}/${group}/show_data/${show}`;
        entries.push({
          id:`entry-${++serial}`,relativePath,sourcePath:`C:\\Research\\${relativePath}`,
          sizeBytes:serial,sha256:serial.toString(16).padStart(64,'0'),
          projectName:'DataM_Research_Test',themeName:`实验${ei}`,assayName:assay,
          groupName:group,replicateName:`E${ei}_${ac}_${group}`,stage:'display',
          decision:'import',processTag:'',alias:show,issue:'',status:'queued',jobId:null
        });
      }
    }
  }
  for(const [folder,files] of [['paper',['DataM_模拟论文.docx','DataM_模拟论文.pdf']],['figure',['Figure1.png','Figure2.png']]] as const){
    for(const filename of files){
      const relativePath=`${folder}/${filename}`;
      entries.push({id:`entry-${++serial}`,relativePath,
        sourcePath:`C:\\Research\\${relativePath}`,sizeBytes:serial,
        sha256:serial.toString(16).padStart(64,'0'),
        projectName:'DataM_Research_Test',themeName:'',assayName:'',groupName:'',replicateName:'',
        stage:folder,decision:'import',processTag:'',alias:filename,issue:'',status:'queued',jobId:null});
    }
  }
  return entries;
}
describe('S14 346-file multi-scope durable S3 import pipeline',()=>{
  it('creates 2 themes, 6 assays, 18 groups and 108 replicates without auto Paper/Panel linking',async()=>{
    const entries=makeCorpus();
    expect(entries).toHaveLength(346);
    const task:ArchiveTask={
      id:'s14-dual',rootName:'DataM_Research_Test',
      layout:'project_exp_data_experiment_assay_group',targetProjectId:null,
      targetThemeName:'',fallbackGroup:'',status:'prepared',
      total:346,imported:0,skipped:0,review:0,failed:0,ready:346,
      entries:entries.slice(),nextOffset:null
    };
    let sequence=0;
    const nodes:{id:string;name:string;kind:'project'|'theme'|'assay';parentId:string|null}[]=[];
    const groupsByAssay=new Map<string,Array<{id:string;name:string;replicates:Array<{id:string;label:string}>}>>();
    const jobs=new Map<string,ImportJob>();
    const deps:ArchiveRunnerDeps={
      archive:vi.fn(async request=>{
        if(request.action==='resume')task.status='running';
        if(request.action==='bind')for(const id of request.entryIds??[]){
          const entry=entries.find(e=>e.id===id)!;
          entry.jobId=request.jobId!;entry.status='running';
        }
        if(request.action==='reconcile'){
          for(const entry of entries){
            if(entry.jobId&&jobs.get(entry.jobId)?.status==='completed')entry.status='imported';
          }
          task.imported=entries.filter(x=>x.status==='imported').length;
          task.status=task.imported===task.total?'completed':'running';
        }
        return {current:{...task,entries:[...entries]},tasks:[],templates:[]};
      }),
      projects:vi.fn(async request=>{
        if(request.action==='create'){
          nodes.push({id:`node-${++sequence}`,name:request.name!,
            kind:request.kind! as 'project'|'theme'|'assay',parentId:request.parentId??null});
        }
        return {nodes:[...nodes]};
      }),
      experiments:vi.fn(async request=>{
        const assay=request.assayId!;
        let groups=groupsByAssay.get(assay);
        if(!groups){groups=[];groupsByAssay.set(assay,groups);}
        if(request.action==='create'&&request.kind==='group')
          groups.push({id:`group-${++sequence}`,name:request.name!,replicates:[]});
        if(request.action==='create'&&request.kind==='replicate'){
          const group=groups.find(g=>g.id===request.parentId)!;
          group.replicates.push({id:`rep-${++sequence}`,label:request.name!});
        }
        return {assayId:assay,groups:groups.map(g=>({...g,replicates:[...g.replicates]})),
          replicateCount:groups.reduce((n,g)=>n+g.replicates.length,0),
          stages:[
            {id:'raw' as const,label:'Raw',scope:'replicate' as const},
            {id:'processed' as const,label:'Processed',scope:'replicate' as const},
            {id:'statistics' as const,label:'Statistics',scope:'assay' as const},
            {id:'display' as const,label:'Display',scope:'assay' as const}
          ]};
      }),
      importer:vi.fn(async request=>{
        if(request.action==='prepare'){
          const jobId=`job-${++sequence}`;
          jobs.set(jobId,{id:jobId,target:request.target!,status:'prepared',
            totalBytes:0,copiedBytes:0,completedCount:0,errorCode:null,
            files:(request.aliases??[]).map((alias:string,index:number)=>({
              id:`file-${sequence}-${index}`,name:alias,sizeBytes:
                entries.find(e=>e.sourcePath===request.paths![index])!.sizeBytes,
              sha256:null,status:'queued',assetVersionId:null,errorCode:null
            }))});
        }
        if(request.action==='start'){
          const job=jobs.get(request.jobId!)!;
          job.status='completed';
          job.files=job.files.map(f=>({...f,status:'committed' as const}));
          job.completedCount=job.files.length;
        }
        return {jobs:[...jobs.values()]};
      }),
      sleep:vi.fn(async()=>{})
    };
    const progress=vi.fn();
    const result=await runArchiveTask(task,progress,new AbortController().signal,deps);
    expect(result.status).toBe('completed');
    expect(result.imported).toBe(346);
    expect(nodes.filter(x=>x.kind==='project')).toHaveLength(1);
    expect(nodes.filter(x=>x.kind==='theme')).toHaveLength(2);
    expect(nodes.filter(x=>x.kind==='assay')).toHaveLength(6);
    expect([...groupsByAssay.values()].reduce((sum,g)=>sum+g.length,0)).toBe(18);
    expect([...groupsByAssay.values()].flat().reduce((sum,g)=>sum+g.replicates.length,0)).toBe(108);
    const imported=[...jobs.values()].flatMap(j=>j.files);
    expect(imported).toHaveLength(346);
    expect([...jobs.values()].filter(j=>j.target.stage==='paper').flatMap(j=>j.files)).toHaveLength(2);
    expect([...jobs.values()].filter(j=>j.target.stage==='figure').flatMap(j=>j.files)).toHaveLength(2);
    expect(progress).toHaveBeenCalled();
  });
});
