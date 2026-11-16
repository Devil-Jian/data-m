import {invoke,isTauri} from '@tauri-apps/api/core';

export type ImportStage='raw'|'processed'|'statistics'|'display';
export interface ImportTarget {assayId:string;stage:ImportStage;groupId?:string;replicateId?:string}
export type ImportJobStatus='prepared'|'running'|'completed'|'failed'|'cancelled'|'interrupted'|'integrity_failed';
export type ImportFileStatus='queued'|'copying'|'staged'|'blob_published'|'committed'|'failed'|'cancelled'|'interrupted';
export interface ImportFile {id:string;name:string;sizeBytes:number;sha256:string|null;status:ImportFileStatus;assetVersionId:string|null;errorCode:string|null}
export interface ImportJob {id:string;status:ImportJobStatus;target:ImportTarget;files:ImportFile[];totalBytes:number;copiedBytes:number;completedCount:number;errorCode:string|null}
export type ImportRequest={action:'prepare';paths:string[];target:ImportTarget;requestId:string}|{action:'start'|'get'|'cancel'|'retry';jobId:string}|{action:'list'};
export interface ImportResponse {jobs:ImportJob[]}
export type ImportApi=(request:ImportRequest)=>Promise<ImportResponse>;
export type ImportDropHandler=(paths:string[])=>void;
export type ImportDropRegistrar=(handler:ImportDropHandler|null)=>void;
declare global {interface Window {__S3_IMPORTS_MOCK__?:ImportApi}}

const messages:Record<string,string>={
  UNAVAILABLE:'浏览器未连接原生导入服务。请使用桌面应用和可丢弃测试文件。',
  INVALID_REQUEST:'导入请求无效，请重新加载任务后再试。',
  INVALID_TARGET:'导入目标不可用，请重新选择实验阶段或样本。',
  NOT_FOUND:'导入任务或目标已不存在，请重新加载。',
  REQUEST_CONFLICT:'导入请求发生冲突，请重新加载任务。',
  REPOSITORY_BUSY:'测试仓库正被占用，请稍后重试。',
  UNSAFE_PATH:'文件或仓库位置不符合安全检查要求，已拒绝导入。',
  SOURCE_UNAVAILABLE:'源文件不可读或被占用，请检查可丢弃测试源后重试。',
  SOURCE_CHANGED:'源文件已变化，请检查测试源文件后重试。',
  NAME_CONFLICT:'此目标已有同名文件，请检查名称冲突。',
  DISK_WRITE_FAILED:'测试仓库写入失败，请检查存储空间和权限后重试。',
  BLOB_INTEGRITY_FAILED:'已存储文件完整性检查失败，已阻止伪成功。请保留现场检查。',
  FILE_FAILED:'部分文件导入失败，请查看逐项错误后重试未完成文件。',
  CANCELLED:'任务已取消，已登记的文件将保留。',
  INTERRUPTED:'任务曾被中断，可重试尚未完成的文件。',
  DATABASE_UNAVAILABLE:'暂时无法访问导入任务存储，请重新加载。',
  UNKNOWN_DATABASE:'数据库无法识别，已拒绝导入。',
  INVALID_RESPONSE:'导入状态无法验证，请重新加载任务；尚未确认完成。',
  UNKNOWN:'暂时无法完成导入操作，请重新加载任务后再试。',
};
export class ImportError extends Error {
  constructor(public readonly code:string){super(messages[code]??messages.UNKNOWN);this.name='ImportError';}
}
export function importErrorMessage(code:string|null){return code?(messages[code]??messages.UNKNOWN):'';}
export function safeImportError(error:unknown):ImportError {
  if(error instanceof ImportError)return error;
  const raw=error&&typeof error==='object'&&'code' in error&&typeof error.code==='string'?error.code:'UNKNOWN';
  return new ImportError(Object.hasOwn(messages,raw)?raw:'UNKNOWN');
}
function record(value:unknown):value is Record<string,unknown>{return Boolean(value)&&typeof value==='object';}
function opaque(value:unknown):value is string{return typeof value==='string'&&value.length>0&&value.length<=256&&!/[\\/\u0000-\u001f:]/.test(value);}
function bytes(value:unknown):value is number{return typeof value==='number'&&Number.isSafeInteger(value)&&value>=0;}
function errorCode(value:unknown){return typeof value==='string'?(Object.hasOwn(messages,value)?value:'UNKNOWN'):null;}
const fail=():never=>{throw new ImportError('INVALID_RESPONSE');};
function target(value:unknown):ImportTarget {
  if(!record(value)||!opaque(value.assayId)||!['raw','processed','statistics','display'].includes(String(value.stage)))return fail();
  const result:ImportTarget={assayId:value.assayId,stage:value.stage as ImportStage};
  if(result.stage==='raw'||result.stage==='processed'){
    if(!opaque(value.replicateId))return fail();
    result.replicateId=value.replicateId;
    if(value.groupId!==undefined&&value.groupId!==null){if(!opaque(value.groupId))return fail();result.groupId=value.groupId;}
  }else if(value.replicateId!=null||value.groupId!=null)return fail();
  return result;
}
/** Copy only protocol fields. Extra metadata and untrusted OS text never enter UI state. */
export function validateImportResponse(value:unknown):ImportResponse {
  if(!record(value)||!Array.isArray(value.jobs))return fail();
  const jobIds=new Set<string>();
  return {jobs:value.jobs.map(raw=>{
    if(!record(raw)||!opaque(raw.id)||jobIds.has(raw.id)||!['prepared','running','completed','failed','cancelled','interrupted','integrity_failed'].includes(String(raw.status))||!Array.isArray(raw.files)||!bytes(raw.totalBytes)||!bytes(raw.copiedBytes)||!bytes(raw.completedCount))return fail();
    jobIds.add(raw.id);
    const fileIds=new Set<string>();
    const files:ImportFile[]=raw.files.map(file=>{
      if(!record(file)||!opaque(file.id)||fileIds.has(file.id)||typeof file.name!=='string'||!file.name||/[\\/\u0000-\u001f]/.test(file.name)||!bytes(file.sizeBytes)||!['queued','copying','staged','blob_published','committed','failed','cancelled','interrupted'].includes(String(file.status)))return fail();
      fileIds.add(file.id);
      const hash=file.sha256??null,version=file.assetVersionId??null;
      if(hash!==null&&(typeof hash!=='string'||!/^\p{ASCII_Hex_Digit}{64}$/u.test(hash)))return fail();
      if(version!==null&&!opaque(version))return fail();
      if(file.status==='committed'&&(!hash||!version))return fail();
      return {id:file.id,name:file.name,sizeBytes:file.sizeBytes,sha256:hash as string|null,status:file.status as ImportFileStatus,assetVersionId:version as string|null,errorCode:errorCode(file.errorCode)};
    });
    if(raw.totalBytes!==files.reduce((sum,file)=>sum+file.sizeBytes,0)||raw.completedCount!==files.filter(file=>file.status==='committed').length)return fail();
    if(raw.status==='completed'&&(!files.length||raw.completedCount!==files.length))return fail();
    return {id:raw.id,status:raw.status as ImportJobStatus,target:target(raw.target),files,totalBytes:raw.totalBytes,copiedBytes:raw.copiedBytes,completedCount:raw.completedCount,errorCode:errorCode(raw.errorCode)};
  })};
}
export const importApi:ImportApi=async request=>{
  try{
    if(isTauri())return validateImportResponse(await invoke('import_jobs',{request}));
    if(window.__S3_IMPORTS_MOCK__)return validateImportResponse(await window.__S3_IMPORTS_MOCK__(request));
    throw new ImportError('UNAVAILABLE');
  }catch(error){throw safeImportError(error);}
};
export function importMode(){return isTauri()?'原生测试库导入（S3）':window.__S3_IMPORTS_MOCK__?'浏览器 Mock（不写入原生数据库）':'浏览器：原生导入服务不可用';}
