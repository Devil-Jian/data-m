import {invoke,isTauri} from '@tauri-apps/api/core';
import type {AssetQuery,AssetRow} from './asset-browser.ts';

export type AssetStage='raw'|'processed'|'statistics'|'display';
export interface AssetScope {
  assayId:string;stage:AssetStage;groupId?:string|null;replicateId?:string|null;
}
export interface TransferSource extends AssetScope {
  assetLinkId:string;assetId:string;assetVersionId:string;
}
export interface TransferRequest {
  action:'copy'|'move';requestId:string;items:TransferSource[];target:AssetScope;
}
export interface TransferLink {
  sourceLinkId:string;resultLinkId:string;assetId:string;assetVersionId:string;
}
export interface TransferResult {
  requestId:string;action:'copy'|'move';target:AssetScope;links:TransferLink[];
}
export type TransferApi=(request:TransferRequest)=>Promise<TransferResult>;
declare global {interface Window {__S5_TRANSFER_MOCK__?:TransferApi}}

const messages:Record<string,string>={
  INVALID_REQUEST:'操作无效。请重新选择本页的文件。',
  INVALID_TARGET:'目标不是合法的实验阶段或活跃样本。请选择具体样本。',
  NOT_FOUND:'来源关联已不存在，请刷新列表。',
  STALE_SELECTION:'来源关联、所属范围或版本已变化，请刷新并重新选择。',
  NAME_CONFLICT:'目标已有同名文件；整批操作均未提交。',
  REQUEST_CONFLICT:'操作请求 ID 冲突，请重新准备操作。',
  DATABASE_UNAVAILABLE:'暂时无法访问仓库，请重试。',
  UNKNOWN_DATABASE:'仓库格式无法识别，操作已被阻止。',
  EMPTY_CLIPBOARD:'剪贴板中没有待复制或移动的资产。',
  INVALID_RESPONSE:'关联提交结果无法验证，请刷新后检查状态。',
  UNKNOWN:'资产操作未完成，请刷新检查并重试。',
};
export class TransferError extends Error {
  constructor(public readonly code:string){
    super(messages[code]??messages.UNKNOWN);
    this.name='TransferError';
  }
}
export function safeTransferError(value:unknown):TransferError {
  if(value instanceof TransferError)return value;
  const code=value&&typeof value==='object'&&'code' in value&&typeof value.code==='string'&&Object.hasOwn(messages,value.code)?value.code:'UNKNOWN';
  return new TransferError(code);
}
const validId=(v:unknown):v is string=>typeof v==='string'&&/^[\w-]{1,128}$/.test(v);
const validScope=(v:AssetScope)=>validId(v.assayId)&&['raw','processed','statistics','display'].includes(v.stage)
  &&(['raw','processed'].includes(v.stage)?validId(v.groupId)&&validId(v.replicateId):v.groupId==null&&v.replicateId==null);
function validateResult(request:TransferRequest,value:TransferResult):TransferResult {
  if(!value||value.requestId!==request.requestId||value.action!==request.action||!Array.isArray(value.links)
    ||value.links.length!==request.items.length||!value.target||JSON.stringify(value.target)!==JSON.stringify(request.target))throw new TransferError('INVALID_RESPONSE');
  for(let i=0;i<value.links.length;i++){
    const link=value.links[i],source=request.items[i];
    if(!validId(link?.sourceLinkId)||!validId(link?.resultLinkId)||link.sourceLinkId!==source.assetLinkId
      ||link.assetId!==source.assetId||link.assetVersionId!==source.assetVersionId
      ||(request.action==='move'&&link.resultLinkId!==link.sourceLinkId)
      ||(request.action==='copy'&&link.resultLinkId===link.sourceLinkId))throw new TransferError('INVALID_RESPONSE');
  }
  if(new Set(value.links.map(link=>link.resultLinkId)).size!==value.links.length)throw new TransferError('INVALID_RESPONSE');
  return value;
}
export const transferApi:TransferApi=async request=>{
  try{
    const response:TransferResult=isTauri()?await invoke('asset_transfer',{request})
      :window.__S5_TRANSFER_MOCK__?await window.__S5_TRANSFER_MOCK__(request)
      :(()=>{throw new TransferError('UNKNOWN');})();
    return validateResult(request,response);
  }catch(error){throw safeTransferError(error);}
};

/** Source identity is captured only from currently rendered rows, never from old pages. */
export function sourcesFromRows(query:AssetQuery,rows:AssetRow[]):TransferSource[] {
  if(!validId(query.assayId)||!rows.length||rows.length>100)throw new TransferError('INVALID_REQUEST');
  const result=rows.map(row=>{
    const source:TransferSource={
      assayId:query.assayId,stage:row.stage,groupId:row.groupId,replicateId:row.replicateId,
      assetLinkId:row.assetLinkId,assetId:row.assetId,assetVersionId:row.assetVersionId
    };
    if(!validId(source.assetLinkId)||!validId(source.assetId)||!validId(source.assetVersionId)
      ||!validScope(source)||query.stage!==row.stage
      ||(query.groupId!=null&&query.groupId!==row.groupId)
      ||(query.replicateId!=null&&query.replicateId!==row.replicateId))throw new TransferError('STALE_SELECTION');
    return Object.freeze(source);
  });
  if(new Set(result.map(row=>row.assetLinkId)).size!==result.length)throw new TransferError('INVALID_REQUEST');
  return result;
}
interface TransferSelection {action:'copy'|'move';items:TransferSource[]}
let clipboard:TransferSelection|null=null;
let dragged:TransferSource[]|null=null;
let pending:{signature:string;requestId:string}|null=null;
export function putClipboard(action:'copy'|'move',items:TransferSource[]) {
  if(!items.length)throw new TransferError('INVALID_REQUEST');
  clipboard={action,items:items.map(item=>({...item}))};
  pending=null;
  window.dispatchEvent(new Event('data-m-s5-clipboard'));
}
export function getClipboard():TransferSelection|null {
  return clipboard?{action:clipboard.action,items:clipboard.items.map(item=>({...item}))}:null;
}
export function beginAssetDrag(items:TransferSource[]) {dragged=items.map(item=>({...item}));}
export function endAssetDrag() {dragged=null;}
export function hasAssetDrag() {return Boolean(dragged?.length);}
function transfer(action:'copy'|'move',items:TransferSource[],target:AssetScope,api:TransferApi):Promise<TransferResult> {
  if(!validScope(target))return Promise.reject(new TransferError('INVALID_TARGET'));
  if(!items.length||items.length>100)return Promise.reject(new TransferError('INVALID_REQUEST'));
  const signature=JSON.stringify({action,items,target});
  const requestId=pending?.signature===signature?pending.requestId:crypto.randomUUID();
  pending={signature,requestId};
  return api({action,requestId,items,target}).then(result=>{
    pending=null;
    window.dispatchEvent(new Event('data-m-s5-changed'));
    return result;
  });
}
export async function pasteAssets(target:AssetScope,api:TransferApi=transferApi){
  if(!clipboard)throw new TransferError('EMPTY_CLIPBOARD');
  const snapshot=clipboard;
  const result=await transfer(snapshot.action,snapshot.items,target,api);
  if(snapshot.action==='move'&&clipboard===snapshot){clipboard=null;window.dispatchEvent(new Event('data-m-s5-clipboard'));}
  return result;
}
export function dropAssets(event:Pick<DragEvent,'ctrlKey'|'shiftKey'>,target:AssetScope,api:TransferApi=transferApi){
  if(!dragged)throw new TransferError('INVALID_REQUEST');
  const action=event.ctrlKey&&!event.shiftKey?'copy':event.shiftKey&&!event.ctrlKey?'move':null;
  if(!action)throw new TransferError('INVALID_REQUEST');
  return transfer(action,dragged,target,api);
}
