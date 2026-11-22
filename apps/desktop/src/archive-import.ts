import {invoke,isTauri} from '@tauri-apps/api/core';
import type {ImportStage} from './import-jobs';

export type ArchiveLayout='project_theme_assay_group'|'theme_assay_group'|'assay_group'|'project_exp_data_experiment_assay_group';
export type ArchiveDecision='import'|'skip'|'review';
export type ArchiveStatus='scanned'|'review'|'prepared'|'running'|'paused'|'failed'|'completed'|'cancelled';
export type ArchiveAction=
  'scan'|'get'|'list'|'edit'|'prepare'|'bind'|'reconcile'|'resume'|'pause'|
  'templateSave'|'templateDelete';
export interface ArchiveRequest {
  action:ArchiveAction;rootPath?:string;layout?:ArchiveLayout;taskId?:string;
  targetProjectId?:string;targetThemeName?:string;fallbackGroup?:string;
  entryId?:string;decision?:ArchiveDecision;
  projectName?:string;themeName?:string;assayName?:string;groupName?:string;
  replicateName?:string;stage?:ImportStage;alias?:string;
  jobId?:string;entryIds?:string[];offset?:number;templateName?:string;
}
export interface ArchiveEntry{
  id:string;relativePath:string;sourcePath:string;sizeBytes:number;sha256:string;
  projectName:string;themeName:string;assayName:string;groupName:string;
  replicateName:string;stage:ImportStage|'review';decision:ArchiveDecision;
  alias:string;processTag:string;issue:string;status:string;jobId:string|null;
}
export interface ArchiveTask{
  id:string;rootName:string;layout:ArchiveLayout;targetProjectId:string|null;
  targetThemeName:string;fallbackGroup:string;status:ArchiveStatus;
  total:number;imported:number;skipped:number;review:number;failed:number;
  ready:number;entries:ArchiveEntry[];nextOffset:number|null;
}
export interface ArchiveTemplate{
  name:string;layout:ArchiveLayout;targetThemeName:string;fallbackGroup:string;
}
export interface ArchiveResponse{
  current:ArchiveTask|null;tasks:ArchiveTask[];templates:ArchiveTemplate[];
}
export type ArchiveApi=(request:ArchiveRequest)=>Promise<ArchiveResponse>;
declare global {interface Window{
  __S13_ARCHIVE_MOCK__?:ArchiveApi;
  __S13_PICK_FOLDER_MOCK__?:()=>Promise<string|null>;
}}
const valid=(v:unknown):v is Record<string,unknown>=>v!==null&&typeof v==='object'&&!Array.isArray(v);
const nonblank=(v:unknown):v is string=>typeof v==='string'&&v.length>0&&v.length<=32767;
const count=(v:unknown):v is number=>Number.isSafeInteger(v)&&typeof v==='number'&&v>=0;
const options:{stage:readonly string[];layout:readonly string[];decision:readonly string[];status:readonly string[]}={
  stage:['raw','processed','statistics','display','review'],
  layout:['project_theme_assay_group','theme_assay_group','assay_group','project_exp_data_experiment_assay_group'],
  decision:['import','skip','review'],
  status:['scanned','review','prepared','running','paused','failed','completed','cancelled']
};
function validateEntry(raw:unknown):ArchiveEntry{
  if(!valid(raw)||!nonblank(raw.id)||!nonblank(raw.relativePath)||!nonblank(raw.sourcePath)||
    !count(raw.sizeBytes)||typeof raw.sha256!=='string'||!/^[a-f0-9]{64}$/.test(raw.sha256)||
    !['projectName','themeName','assayName','groupName','replicateName','alias','processTag','issue','status']
      .every(key=>typeof raw[key]==='string')||
    !options.stage.includes(String(raw.stage))||!options.decision.includes(String(raw.decision))||
    !(raw.jobId===null||nonblank(raw.jobId)))
    throw Error('S13 native archive manifest entry invalid');
  return raw as unknown as ArchiveEntry;
}
function validateTask(raw:unknown):ArchiveTask{
  if(!valid(raw)||!nonblank(raw.id)||!nonblank(raw.rootName)||
    !options.layout.includes(String(raw.layout))||!options.status.includes(String(raw.status))||
    !(raw.targetProjectId===null||nonblank(raw.targetProjectId))||
    typeof raw.targetThemeName!=='string'||typeof raw.fallbackGroup!=='string'||
    !['total','imported','skipped','review','failed','ready'].every(k=>count(raw[k]))||
    !Array.isArray(raw.entries)||raw.entries.length>500||
    !(raw.nextOffset===null||count(raw.nextOffset)))
    throw Error('S13 native archive task invalid');
  const entries=raw.entries.map(validateEntry);
  if(new Set(entries.map(e=>e.id)).size!==entries.length)
    throw Error('S13 native archive duplicate entry identity');
  if(Number(raw.imported)+Number(raw.skipped)>Number(raw.total)||Number(raw.review)>Number(raw.total)||Number(raw.ready)>Number(raw.total))
    throw Error('S13 native archive totals inconsistent');
  return {...raw,entries} as ArchiveTask;
}
export function validateArchiveResponse(raw:unknown):ArchiveResponse{
  if(!valid(raw)||!Array.isArray(raw.tasks)||!Array.isArray(raw.templates)||
    !(raw.current===null||valid(raw.current)))throw Error('S13 invalid native response');
  const tasks=raw.tasks.map(validateTask),current=raw.current===null?null:validateTask(raw.current);
  if(new Set(tasks.map(t=>t.id)).size!==tasks.length)throw Error('S13 duplicate task identity');
  const templates=raw.templates.map((t:unknown)=>{
    if(!valid(t)||!nonblank(t.name)||!options.layout.includes(String(t.layout))||
      typeof t.targetThemeName!=='string'||typeof t.fallbackGroup!=='string')
      throw Error('S13 invalid rule template');
    return t as unknown as ArchiveTemplate;
  });
  return {current,tasks,templates};
}
export const archiveApi:ArchiveApi=async request=>{
  const raw=isTauri()?await invoke<ArchiveResponse>('archive_import',{request}):
    window.__S13_ARCHIVE_MOCK__?await window.__S13_ARCHIVE_MOCK__(request):
    (()=>{throw Error('请使用 Windows 桌面版扫描真实科研目录；浏览器必须显式注入 S13 测试服务。')})();
  return validateArchiveResponse(raw);
};
export async function pickArchiveFolder():Promise<string|null>{
  if(isTauri())return invoke<string|null>('archive_pick_folder');
  if(window.__S13_PICK_FOLDER_MOCK__)return window.__S13_PICK_FOLDER_MOCK__();
  throw Error('浏览器不能读取 Windows 文件夹真实路径，请使用桌面 EXE。');
}
export function archiveError(error:unknown):string{
  const code=error&&typeof error==='object'&&'code' in error&&
    typeof error.code==='string'?error.code:'';
  const known:Record<string,string>={
    UNSAFE_PATH:'目录包含符号链接、重解析点或非普通文件，已拒绝扫描。',
    SOURCE_IN_REPOSITORY:'源文件夹包含程序仓库或处于仓库内部；请选择独立的原始文件夹。',
    SOURCE_CHANGED:'扫描之后源文件发生变化，请重新扫描。',
    TOO_MANY_FILES:'超过单次扫描 50,000 个文件的安全上限，请分项目导入。',
    DIRECTORY_TOO_DEEP:'目录超过安全深度上限。',
    TARGET_PROJECT_REQUIRED:'请选择目标已有项目或使用项目根目录映射。',
    INVALID_THEME:'请确认上级主题名称。',
    INVALID_LAYOUT:'请选择正确的目录层级映射。',
    EMPTY_SOURCE:'目录中没有可导入的普通文件。',
    TASK_NOT_READY:'导入计划尚有待处理歧义，请完成检查后再执行。',
    TASK_LOCKED:'运行中的任务不能修改映射；先暂停后校对。',
    NAME_CONFLICT:'同名文件已存在，须调整新文件名或明确跳过。',
    QUEUE_UNAVAILABLE:'S13 持久导入队列不可访问，请检查权限。',
    SOURCE_UNAVAILABLE:'源文件不可读取，扫描已停止。',
  };
  return known[code]??(error instanceof Error?error.message:'S13 导入操作失败，请查看任务状态。');
}
export function reportArchive(task:ArchiveTask,entries:ArchiveEntry[]){
  return {
    format:'Data-M/S13/archive-import-report/v1',
    taskId:task.id,rootName:task.rootName,status:task.status,layout:task.layout,
    sourceFileCount:task.total,importedCount:task.imported,skippedCount:task.skipped,
    reviewCount:task.review,failedCount:task.failed,
    entries:entries.map(e=>({
      relativePath:e.relativePath,project:e.projectName,theme:e.themeName,assay:e.assayName,
      group:e.groupName,replicate:e.replicateName,stage:e.stage,alias:e.alias,processTag:e.processTag,
      sha256:e.sha256,status:e.status,decision:e.decision,issue:e.issue,jobId:e.jobId
    }))
  };
}
