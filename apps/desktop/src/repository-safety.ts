import {invoke,isTauri} from '@tauri-apps/api/core';
import {listen} from '@tauri-apps/api/event';

export type SafetyAction='audit'|'list'|'backup'|'verify'|'restore';
export type SafetyRequest=
 | {action:'audit'|'list'|'backup'}
 | {action:'verify';snapshotId:string}
 | {action:'restore';snapshotId:string;restoreLabel:string;confirm:true};
export interface SafetyIssue{code:string;subject:string}
export interface SafetyReport{
 checkedBlobs:number;verifiedBlobs:number;issues:SafetyIssue[];truncated:boolean;
 tableCounts:Record<string,number>;
}
export interface SnapshotSummary{
 snapshotId:string;blobCount:number;versionCount:number;schemaVersion:number;createdAt:string;
}
export interface SafetyResponse{
 action:SafetyAction;status:'completed'|'clean'|'issues_found'|'verified'|'restored';
 snapshotId:string|null;restoreLabel:string|null;
 report:SafetyReport|null;snapshots:SnapshotSummary[];
}
export interface SafetyProgress{phase:string;completed:number;total:number}
export type SafetyApi=(request:SafetyRequest)=>Promise<SafetyResponse>;
export type ProgressListen=(callback:(progress:SafetyProgress)=>void)=>Promise<()=>void>;
declare global {interface Window{
  __S7_SAFETY_MOCK__?:SafetyApi;
}}
const messages:Record<string,string>={
 INVALID_REQUEST:'请选择有效快照并输入安全的恢复目录名称，确认后再恢复。',
 UNKNOWN_DATABASE:'仓库不存在或格式不受支持；审计和备份不会自动创建或修改仓库。',
 UNSAFE_PATH:'文件或目录存在安全问题（重解析点或非普通文件），操作已停止。',
 BACKUP_BUSY:'请先完成、取消或排查尚未完成的安全导入任务。',
 INTEGRITY_FAILED:'发现 SQLite、外键、Blob 或孤儿暂存数据异常；禁止封存快照。',
 SNAPSHOT_NOT_FOUND:'找不到所选快照，请重新刷新快照列表。',
 SNAPSHOT_CORRUPT:'快照的校验清单、数据库或文件已损坏，已拒绝恢复。',
 RESTORE_CONFLICT:'恢复目录已存在，未覆盖已有仓库。请更换标签。',
 DISK_WRITE_FAILED:'写入备份或恢复目录时失败，既有仓库与已封存快照不应受到覆盖。',
 INVALID_RESPONSE:'安全检查响应无效，无法确认结果，请重新查询。',
 UNAVAILABLE:'仅 Windows 客户端可访问真实仓库；浏览器展示必须明确注入测试 Mock。',
 UNKNOWN:'安全操作未能确认，请重新查询。'
};
export class SafetyError extends Error {
 constructor(readonly code:string){super(messages[code]??messages.UNKNOWN);this.name='SafetyError';}
}
export function safeSafetyError(error:unknown):SafetyError {
 if(error instanceof SafetyError)return error;
 const code=error&&typeof error==='object'&&'code' in error&&typeof error.code==='string'&&Object.hasOwn(messages,error.code)
  ?error.code:'UNKNOWN';
 return new SafetyError(code);
}
const validID=(id:unknown):id is string=>typeof id==='string'&&/^[a-f0-9]{32}$/.test(id);
export const safeLabel=(name:string):boolean=>/^[a-zA-Z0-9_-]{1,50}$/.test(name)
 &&!(/^(con|nul|prn|aux)$/i.test(name)||/^(com|lpt)/i.test(name));
function checkedResult(req:SafetyRequest,raw:SafetyResponse):SafetyResponse{
 if(!raw||raw.action!==req.action||!['completed','clean','issues_found','verified','restored'].includes(raw.status)
   ||!Array.isArray(raw.snapshots)||!(raw.snapshotId===null||validID(raw.snapshotId))
   ||!(raw.restoreLabel===null||typeof raw.restoreLabel==='string')
   ||!(raw.report===null||raw.report&&Array.isArray(raw.report.issues)
    &&Number.isSafeInteger(raw.report.checkedBlobs)&&Number.isSafeInteger(raw.report.verifiedBlobs)
    &&raw.report.checkedBlobs>=0&&raw.report.verifiedBlobs>=0
    &&raw.report.issues.every(issue=>typeof issue.code==='string'&&typeof issue.subject==='string')
    &&raw.report.tableCounts&&typeof raw.report.tableCounts==='object')
   ||raw.snapshots.some(s=>!validID(s.snapshotId)||!Number.isSafeInteger(s.blobCount)
    ||!Number.isSafeInteger(s.versionCount)||s.schemaVersion!==2||typeof s.createdAt!=='string')
   ||('snapshotId' in req&&raw.snapshotId!==req.snapshotId)
   ||('restoreLabel' in req&&raw.restoreLabel!==req.restoreLabel))
   throw new SafetyError('INVALID_RESPONSE');
 return raw;
}
export const safetyApi:SafetyApi=async request=>{
 try{
   const raw=isTauri()?await invoke<SafetyResponse>('repository_safety',{request})
     :window.__S7_SAFETY_MOCK__?await window.__S7_SAFETY_MOCK__(request)
     :(()=>{throw new SafetyError('UNAVAILABLE');})();
   return checkedResult(request,raw);
 }catch(error){throw safeSafetyError(error);}
};
export const safetyListen:ProgressListen=async callback=>{
 if(isTauri()){
   return listen<SafetyProgress>('data-m-s7-progress',event=>{
     const state=event.payload;
     if(state&&typeof state.phase==='string'&&Number.isSafeInteger(state.completed)&&Number.isSafeInteger(state.total)
      &&state.completed>=0&&state.total>=0&&state.completed<=state.total)callback(state);
   });
 }
 // Browser simulations are only allowed when the test backend is injected.
 if(!window.__S7_SAFETY_MOCK__)return ()=>{};
 const onEvent=(event:Event)=>{
   const state=(event as CustomEvent<SafetyProgress>).detail;
   if(state&&typeof state.phase==='string')callback(state);
 };
 window.addEventListener('data-m-s7-progress',onEvent);
 return ()=>window.removeEventListener('data-m-s7-progress',onEvent);
};
