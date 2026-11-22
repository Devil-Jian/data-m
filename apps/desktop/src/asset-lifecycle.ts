import {invoke,isTauri} from '@tauri-apps/api/core';
import type {AssetRow,AssetQuery} from './asset-browser.ts';
import {sourcesFromRows} from './asset-transfer.ts';
import type {TransferSource} from './asset-transfer.ts';

export type ExpectedLink=TransferSource;
export type LifecycleAction='rename'|'soft_delete'|'restore';
export type LifecycleRequest=
 | {action:'rename';requestId:string;item:ExpectedLink;newName:string}
 | {action:'soft_delete'|'restore';requestId:string;items:ExpectedLink[]};
export type LifecycleResponse={requestId:string;action:LifecycleAction;results:Array<{
 assetLinkId:string;assetId:string;assetVersionId:string;name:string;deletedAt:string|null}>};
export type LifecycleApi=(request:LifecycleRequest)=>Promise<LifecycleResponse>;
export type RecycleQuery=Pick<AssetQuery,'assayId'|'stage'|'groupId'|'replicateId'|'search'|'cursor'|'limit'>;
export type RecycledRow=ExpectedLink&{
 name:string;deletedAt:string;sha256:string;referenced:boolean;canRestore:boolean;restoreBlockReason:string|null
};
export type RecyclePage={items:RecycledRow[];total:number;nextCursor:string|null};
export type RecycleApi=(request:RecycleQuery)=>Promise<RecyclePage>;
declare global {interface Window {
 __S6_LIFECYCLE_MOCK__?:LifecycleApi;
 __S6_RECYCLE_MOCK__?:RecycleApi;
}}
const errors:Record<string,string>={
 INVALID_REQUEST:'请选择当前页的有效文件关联；批量最多 100 条。',
 INVALID_NAME:'文件名称不符合 Windows 规则：不能使用设备保留名、非法字符或尾随空格/句点。',
 NOT_FOUND:'文件关联不存在，请刷新。',
 STALE_SELECTION:'资产关联、版本或原位置已变化，请刷新后重新选择。',
 INVALID_TARGET:'原实验、分组或样本已失效，不能直接恢复。',
 NAME_CONFLICT:'该位置已有同名文件；未覆盖、未执行部分提交。',
 REFERENCE_PROTECTED:'论文引用保护：不能回收已被引用资产的最后一个活动关联。',
 ALREADY_DELETED:'关联已经进入回收区，请刷新。',
 ALREADY_ACTIVE:'关联已经恢复，请刷新。',
 REQUEST_CONFLICT:'请求标识冲突，请检查首次提交结果。',
 DATABASE_UNAVAILABLE:'数据库不可用，提交结果待确认，请刷新检查后重试。',
 UNKNOWN_DATABASE:'数据库不属于受控测试工作区，已阻止操作。',
 INVALID_RESPONSE:'提交响应无法核验，请刷新检查状态。',
 UNKNOWN:'操作未确认，请检查当前状态后重试。',
};
export class LifecycleError extends Error {
 constructor(readonly code:string){super(errors[code]??errors.UNKNOWN);this.name='LifecycleError';}
}
export function lifecycleError(reason:unknown):LifecycleError {
 if(reason instanceof LifecycleError)return reason;
 const code=reason&&typeof reason==='object'&&'code' in reason&&typeof reason.code==='string'&&Object.hasOwn(errors,reason.code)?reason.code:'UNKNOWN';
 return new LifecycleError(code);
}
const validId=(v:unknown)=>typeof v==='string'&&/^[a-zA-Z0-9_-]{1,128}$/.test(v);
export function validateLifecycleResponse(req:LifecycleRequest,r:LifecycleResponse):LifecycleResponse {
 const items=req.action==='rename'?[req.item]:req.items;
 if(!r||r.requestId!==req.requestId||r.action!==req.action||!Array.isArray(r.results)
  ||r.results.length!==items.length)throw new LifecycleError('INVALID_RESPONSE');
 for(let i=0;i<items.length;i++){
  const row=r.results[i],item=items[i];
  if(!row||row.assetLinkId!==item.assetLinkId||row.assetId!==item.assetId
   ||row.assetVersionId!==item.assetVersionId||typeof row.name!=='string'
   ||!(row.deletedAt===null||typeof row.deletedAt==='string')
   ||(req.action==='rename'&&row.name!==req.newName)
   ||(req.action==='soft_delete'&&row.deletedAt===null)
   ||(req.action==='restore'&&row.deletedAt!==null))throw new LifecycleError('INVALID_RESPONSE');
 }
 return r;
}
export const lifecycleApi:LifecycleApi=async req=>{
 try{
  const response=isTauri()?await invoke<LifecycleResponse>('asset_lifecycle',{request:req})
   :window.__S6_LIFECYCLE_MOCK__?await window.__S6_LIFECYCLE_MOCK__(req)
   :(()=>{throw new LifecycleError('UNKNOWN');})();
  return validateLifecycleResponse(req,response);
 }catch(e){throw lifecycleError(e);}
};
export const recycleApi:RecycleApi=async req=>{
 try{
  const data=isTauri()?await invoke<RecyclePage>('list_recycled_assets',{request:req})
   :window.__S6_RECYCLE_MOCK__?await window.__S6_RECYCLE_MOCK__(req)
   :(()=>{throw new LifecycleError('UNKNOWN');})();
  if(!data||!Array.isArray(data.items)||!Number.isSafeInteger(data.total)||data.total<0
    ||!(data.nextCursor===null||typeof data.nextCursor==='string')||
    !data.items.every(row=>validId(row.assetLinkId)&&validId(row.assetId)&&validId(row.assetVersionId)
      &&row.assayId===req.assayId&&typeof row.name==='string'
      &&typeof row.deletedAt==='string'&&typeof row.canRestore==='boolean'
      &&typeof row.referenced==='boolean'&&/^[a-f0-9]{64}$/.test(row.sha256)))
   throw new LifecycleError('INVALID_RESPONSE');
  return data;
 }catch(e){throw lifecycleError(e);}
};
export function currentLinks(query:AssetQuery,rows:AssetRow[]):ExpectedLink[]{
 return sourcesFromRows(query,rows);
}
export function recycledLinks(rows:RecycledRow[]):ExpectedLink[]{
 return rows.map(({assetLinkId,assetId,assetVersionId,assayId,stage,groupId,replicateId})=>
  ({assetLinkId,assetId,assetVersionId,assayId,stage,groupId,replicateId}));
}
export function lifecycleRequest(action:LifecycleAction,items:ExpectedLink[],newName?:string):LifecycleRequest {
 const requestId=crypto.randomUUID();
 if(action==='rename'){
  if(items.length!==1||newName===undefined)throw new LifecycleError('INVALID_REQUEST');
  return {action,requestId,item:items[0],newName};
 }
 if(!items.length||items.length>100)throw new LifecycleError('INVALID_REQUEST');
 return {action,requestId,items};
}
