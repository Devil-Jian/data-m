import {archiveApi} from './archive-import';
import type {ArchiveApi,ArchiveEntry,ArchiveTask} from './archive-import';
import {projectTreeApi} from './project-tree';
import type {ProjectTreeApi,ProjectTreeNode,ProjectTreeKind} from './project-tree';
import {experimentApi} from './experiment-tree';
import type {ExperimentApi,ExperimentGroup} from './experiment-tree';
import {importApi} from './import-jobs';
import type {ImportApi,ImportJob,ImportTarget} from './import-jobs';

export interface ArchiveRunnerDeps{
  archive:ArchiveApi;projects:ProjectTreeApi;experiments:ExperimentApi;importer:ImportApi;
  sleep:(ms:number)=>Promise<void>;
}
const live:ArchiveRunnerDeps={
  archive:archiveApi,projects:projectTreeApi,experiments:experimentApi,importer:importApi,
  sleep:ms=>new Promise(resolve=>setTimeout(resolve,ms))
};
export class ArchiveRunnerError extends Error{
  constructor(message:string){super(message);this.name='ArchiveRunnerError';}
}
function unique<T>(items:T[],message:string):T|null{
  if(items.length>1)throw new ArchiveRunnerError(message+'：数据库存在多个同名节点，必须先校对');
  return items[0]??null;
}
async function allEntries(taskId:string,api:ArchiveApi):Promise<ArchiveEntry[]>{
  let offset=0;
  const seen=new Set<string>(),entries:ArchiveEntry[]=[];
  for(;;){
    const current=(await api({action:'get',taskId,offset})).current;
    if(!current)throw new ArchiveRunnerError('导入任务不存在，请重新加载');
    for(const entry of current.entries){
      if(seen.has(entry.id))throw new ArchiveRunnerError('S13 分页重复条目，已停止导入');
      seen.add(entry.id);entries.push(entry);
    }
    if(current.nextOffset===null){
      if(entries.length!==current.total)throw new ArchiveRunnerError('S13 分页条目不完整，拒绝导入');
      return entries;
    }
    if(current.nextOffset<=offset)throw new ArchiveRunnerError('S13 分页游标不递增');
    offset=current.nextOffset;
  }
}
function sameName(a:string,b:string){return a.toLocaleLowerCase()===b.toLocaleLowerCase();}
function contextKey(t:ImportTarget){return [
  t.assayId,t.stage,t.groupId??'',t.replicateId??''
].join('\u0001')}
type Assignment={entry:ArchiveEntry;target:ImportTarget};

class StructureResolver{
  private nodes:ProjectTreeNode[]=[];
  private groups=new Map<string,ExperimentGroup[]>();
  constructor(private readonly deps:ArchiveRunnerDeps,private readonly task:ArchiveTask){}
  async init(){this.nodes=(await this.deps.projects({action:'list'})).nodes;}
  private find(kind:ProjectTreeKind,parent:string|null,name:string){
    return unique(this.nodes.filter(n=>n.kind===kind&&n.parentId===parent&&sameName(n.name,name)),
      kind+' '+name);
  }
  private async ensure(kind:ProjectTreeKind,parent:string|null,name:string):Promise<string>{
    if(!name.trim())throw new ArchiveRunnerError('缺少 '+kind+' 的有效名称');
    const old=this.find(kind,parent,name);
    if(old)return old.id;
    const result=await this.deps.projects({
      action:'create',kind,name,
      ...(parent?{parentId:parent}:{}),requestId:crypto.randomUUID()
    });
    this.nodes=result.nodes;
    const created=this.find(kind,parent,name);
    if(!created)throw new ArchiveRunnerError('创建 '+name+' 后无法重新查询节点');
    return created.id;
  }
  private async getGroups(assayId:string):Promise<ExperimentGroup[]>{
    let groups=this.groups.get(assayId);
    if(!groups){
      const data=await this.deps.experiments({action:'list',assayId});
      groups=data.groups;this.groups.set(assayId,groups);
    }
    return groups;
  }
  private async ensureGroup(assayId:string,name:string):Promise<string>{
    let groups=await this.getGroups(assayId);
    let group=unique(groups.filter(g=>sameName(g.name,name)),'分组 '+name);
    if(!group){
      const data=await this.deps.experiments({
        action:'create',kind:'group',assayId,name,requestId:crypto.randomUUID()
      });
      groups=data.groups;this.groups.set(assayId,groups);
      group=unique(groups.filter(g=>sameName(g.name,name)),'分组 '+name);
    }
    if(!group)throw new ArchiveRunnerError('分组 '+name+' 创建失败');
    return group.id;
  }
  private async ensureReplicate(assayId:string,groupId:string,label:string):Promise<string>{
    let groups=await this.getGroups(assayId);
    let group=groups.find(g=>g.id===groupId);
    let rep=unique(group?.replicates.filter(r=>sameName(r.label,label))??[],'样本 '+label);
    if(!rep){
      const data=await this.deps.experiments({
        action:'create',kind:'replicate',assayId,parentId:groupId,
        name:label,requestId:crypto.randomUUID()
      });
      groups=data.groups;this.groups.set(assayId,groups);
      group=groups.find(g=>g.id===groupId);
      rep=unique(group?.replicates.filter(r=>sameName(r.label,label))??[],'样本 '+label);
    }
    if(!rep)throw new ArchiveRunnerError('样本 '+label+' 创建失败');
    return rep.id;
  }
  async resolve(entry:ArchiveEntry):Promise<ImportTarget>{
    const projectId=this.task.targetProjectId??await this.ensure('project',null,entry.projectName);
    if(this.task.targetProjectId&&!this.nodes.some(n=>n.kind==='project'&&n.id===projectId))
      throw new ArchiveRunnerError('现有项目已删除，请重新检查导入映射');
    const themeId=await this.ensure('theme',projectId,entry.themeName);
    const assayId=await this.ensure('assay',themeId,entry.assayName);
    if(entry.stage==='raw'||entry.stage==='processed'){
      const groupId=await this.ensureGroup(assayId,entry.groupName);
      const replicateId=await this.ensureReplicate(assayId,groupId,entry.replicateName);
      return {assayId,stage:entry.stage,groupId,replicateId};
    }
    if(entry.stage==='statistics'||entry.stage==='display'){
      return {assayId,stage:entry.stage};
    }
    throw new ArchiveRunnerError('文件 '+entry.relativePath+' 阶段尚未确认');
  }
}
function oneJob(res:{jobs:ImportJob[]},expected:ImportTarget):ImportJob{
  const jobs=res.jobs.filter(job=>contextKey(job.target)===contextKey(expected));
  if(jobs.length!==1)throw new ArchiveRunnerError('S3 返回的任务目标与确认计划不一致');
  return jobs[0];
}
/** S3 workers persist each file's commit phase. A suspended UI never calls
 * "start" twice before interrogating the durable job state. */
async function finishJob(
  jobId:string,target:ImportTarget,deps:ArchiveRunnerDeps,signal:AbortSignal
):Promise<void>{
  for(;;){
    if(signal.aborted)throw new ArchiveRunnerError('已暂停界面调度，当前原生文件任务可继续执行');
    let job=oneJob(await deps.importer({action:'get',jobId}),target);
    if(job.status==='prepared')job=oneJob(await deps.importer({action:'start',jobId}),target);
    else if(['failed','cancelled','interrupted'].includes(job.status))
      job=oneJob(await deps.importer({action:'retry',jobId}),target);
    else if(job.status==='integrity_failed')
      throw new ArchiveRunnerError('S3 检测到 CAS 完整性异常，禁止自动重试');
    if(job.status==='completed')return;
    if(job.status==='integrity_failed')throw new ArchiveRunnerError('S3 CAS 完整性校验未通过');
    if(job.status==='failed')throw new ArchiveRunnerError('S3 任务失败，请查看原生任务后重新继续');
    await deps.sleep(700);
  }
}
export interface ArchiveProgress {
  taskId:string;completed:number;total:number;label:string;status:string;
}
export async function runArchiveTask(
  task:ArchiveTask,
  onProgress:(state:ArchiveProgress)=>void,
  signal:AbortSignal,
  deps:ArchiveRunnerDeps=live
):Promise<ArchiveTask>{
  if(!['prepared','paused','failed','running'].includes(task.status)||task.review>0)
    throw new ArchiveRunnerError('请先检查冲突和所有待确认的文件，再执行批量导入');
  const entries=await allEntries(task.id,deps.archive);
  if(entries.some(e=>e.decision==='review'))
    throw new ArchiveRunnerError('仍有待确认的文件，无法开始导入');
  await deps.archive({action:'resume',taskId:task.id});
  const resolver=new StructureResolver(deps,task);
  try{
    await resolver.init();
    const existing=new Map<string,{target:ImportTarget;entries:ArchiveEntry[]}>();
    const pending=new Map<string,{target:ImportTarget;entries:ArchiveEntry[]}>();
    for(const entry of entries.filter(e=>e.decision==='import'&&e.status!=='imported')){
      if(signal.aborted)throw new ArchiveRunnerError('用户暂停批量导入');
      const target=await resolver.resolve(entry);
      const key=entry.jobId??contextKey(target);
      const collection=entry.jobId?existing:pending;
      let group=collection.get(key);
      if(!group){group={target,entries:[]};collection.set(key,group);}
      else if(contextKey(group.target)!==contextKey(target))
        throw new ArchiveRunnerError('同一 S3 任务被映射至不同实验，拒绝继续');
      group.entries.push(entry);
    }
    let completed=task.imported+task.skipped;
    const total=task.total;
    for(const [jobId,group] of existing){
      if(signal.aborted)throw new ArchiveRunnerError('用户暂停批量导入');
      onProgress({taskId:task.id,completed,total,label:'恢复未完成的 S3 原生任务',status:'running'});
      await finishJob(jobId,group.target,deps,signal);
      const verified=await deps.archive({action:'reconcile',taskId:task.id});
      if(!verified.current)throw new ArchiveRunnerError('S13 无法核对已提交记录');
      completed=verified.current.imported+verified.current.skipped;
      onProgress({taskId:task.id,completed,total,label:'已核验旧任务固定版本',status:verified.current.status});
    }
    for(const group of pending.values()){
      for(let i=0;i<group.entries.length;i+=500){
        if(signal.aborted)throw new ArchiveRunnerError('用户暂停批量导入');
        const chunk=group.entries.slice(i,i+500);
        onProgress({taskId:task.id,completed,total,
          label:'准备 '+chunk.length+' 个文件 → '+chunk[0].assayName+' / '+group.target.stage,status:'running'});
        const requestId='s13-'+task.id+'-'+chunk[0].id;
        const response=await deps.importer({action:'prepare',
          paths:chunk.map(x=>x.sourcePath),aliases:chunk.map(x=>x.alias),
          target:group.target,requestId});
        const job=oneJob(response,group.target);
        // A reused S3 requestId can return an existing job. Never bind an
        // unrelated file manifest merely because its target and count match.
        const expectedFiles=new Map<string,number>();
        for(const item of chunk){
          const key=JSON.stringify([item.alias,item.sizeBytes]);
          expectedFiles.set(key,(expectedFiles.get(key)??0)+1);
        }
        if(job.files.length!==chunk.length)
          throw new ArchiveRunnerError('S3 目标文件数与已确认计划不一致');
        for(const file of job.files){
          const key=JSON.stringify([file.name,file.sizeBytes]);
          const remaining=expectedFiles.get(key)??0;
          if(!remaining)throw new ArchiveRunnerError('S3 返回文件名或大小与确认计划不一致，拒绝绑定');
          expectedFiles.set(key,remaining-1);
        }
        await deps.archive({action:'bind',taskId:task.id,
          entryIds:chunk.map(x=>x.id),jobId:job.id});
        await finishJob(job.id,group.target,deps,signal);
        const verified=await deps.archive({action:'reconcile',taskId:task.id});
        if(!verified.current)throw new ArchiveRunnerError('S13 任务回读失败');
        completed=verified.current.imported+verified.current.skipped;
        onProgress({taskId:task.id,completed,total,
          label:'已验证 '+chunk.length+' 个文件的 S3/CAS 固定版本',status:verified.current.status});
      }
    }
    const final=await deps.archive({action:'reconcile',taskId:task.id});
    if(!final.current||final.current.status!=='completed')
      throw new ArchiveRunnerError('部分文件尚未完成或存在原生错误，保留任务供恢复');
    return final.current;
  }catch(error){
    // UI pause only: never delete the original files, S3's staging or CAS.
    await deps.archive({action:'pause',taskId:task.id}).catch(()=>{});
    throw error;
  }
}
