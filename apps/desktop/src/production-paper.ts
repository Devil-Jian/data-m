import {invoke,isTauri} from '@tauri-apps/api/core';

/** Native S9 data. A Panel always pins AssetVersionId, never filename or link. */
export interface PinnedAsset {
  assetVersionId:string;usage:string;versionNumber:number;name:string;
  sha256:string;stage:string;assayId:string;replicateId:string|null;
}
export interface PaperRepresentative {replicateId:string;assetVersionId:string|null}
export interface NativePanel {id:string;label:string;assets:PinnedAsset[];representatives:PaperRepresentative[]}
export interface NativeFigure {id:string;label:string;panels:NativePanel[]}
export interface NativePaper {id:string;title:string;status:string;notes:string;figures:NativeFigure[]}
export interface PaperCandidate {
  assetVersionId:string;assetId:string;versionNumber:number;name:string;sha256:string;
  stage:string;assayId:string;groupId:string|null;replicateId:string|null;
}
export interface PaperSnapshot {
  papers:NativePaper[];
  candidates:PaperCandidate[];
  candidateTotalCount?:number;
  candidateTruncated?:boolean;
}
export interface PaperRequest {
  action:'list'|'candidates'|'create'|'rename'|'delete'|'attach'|'detach'|
    'reorder'|'representative'|'clearRepresentative'|'metadata';
  projectId:string;requestId?:string;kind?:'paper'|'figure'|'panel';
  parentId?:string;id?:string;name?:string;
  candidateOffset?:number;candidateLimit?:number;candidateQuery?:string;candidateStage?:string;
  versionIds?:string[];versionId?:string;usage?:string;
  orderIds?:string[];replicateId?:string;status?:string;notes?:string;
}
export type PaperApi=(request:PaperRequest)=>Promise<PaperSnapshot>;
declare global {interface Window {__S12R_PAPER_MOCK__?:PaperApi}}
const obj=(x:unknown):x is Record<string,unknown>=>Boolean(x)&&typeof x==='object';
const validId=(x:unknown):x is string=>typeof x==='string'&&x.length>0&&x.length<=256;
const validSha=(x:unknown)=>typeof x==='string'&&/^[0-9a-f]{64}$/i.test(x);
export function validatePaperSnapshot(input:unknown):PaperSnapshot{
  if(!obj(input)||!Array.isArray(input.papers))throw Error('论文数据响应无效');
  const papers=input.papers as NativePaper[];
  const used=new Set<string>();
  for(const p of papers){
    if(!obj(p)||!validId(p.id)||typeof p.title!=='string'||typeof p.status!=='string'
      ||typeof p.notes!=='string'||!Array.isArray(p.figures))throw Error('论文记录无效');
    if(used.has(p.id))throw Error('论文 ID 重复');used.add(p.id);
    for(const f of p.figures){
      if(!obj(f)||!validId(f.id)||typeof f.label!=='string'||!Array.isArray(f.panels))throw Error('Figure 记录无效');
      if(used.has(f.id))throw Error('Figure ID 重复');used.add(f.id);
      for(const panel of f.panels){
        if(!obj(panel)||!validId(panel.id)||typeof panel.label!=='string'
          ||!Array.isArray(panel.assets)||!Array.isArray(panel.representatives))throw Error('Panel 记录无效');
        if(used.has(panel.id))throw Error('Panel ID 重复');used.add(panel.id);
        for(const a of panel.assets){
          if(!obj(a)||!validId(a.assetVersionId)||!validSha(a.sha256)||
            typeof a.usage!=='string'||typeof a.name!=='string'||typeof a.assayId!=='string'||
            !Number.isInteger(a.versionNumber)||a.versionNumber<1||
            !(a.replicateId===null||typeof a.replicateId==='string'))throw Error('固定版本引用无效');
        }
        for(const r of panel.representatives){
          if(!obj(r)||!validId(r.replicateId)||!(r.assetVersionId===null||validId(r.assetVersionId)))
            throw Error('代表样本关联无效');
        }
      }
    }
  }
  const candidates=Array.isArray(input.candidates)?input.candidates as PaperCandidate[]:[];
  for(const c of candidates){
    if(!obj(c)||!validId(c.assetVersionId)||!validSha(c.sha256)||
      !validId(c.assetId)||typeof c.name!=='string'||typeof c.assayId!=='string')
      throw Error('候选版本响应无效');
  }
  return {
    papers,candidates,
    ...(Number.isInteger(input.candidateTotalCount)&&Number(input.candidateTotalCount)>=0
      ?{candidateTotalCount:Number(input.candidateTotalCount)}:{}),
    ...(typeof input.candidateTruncated==='boolean'?{candidateTruncated:input.candidateTruncated}:{})
  };
}
export const productionPaperApi:PaperApi=async request=>{
  try{
    const value=isTauri()?await invoke<unknown>('paper_workspace',{request}):
      window.__S12R_PAPER_MOCK__?await window.__S12R_PAPER_MOCK__(request):
      (()=>{throw Error('未连接真实论文数据库')})();
    return validatePaperSnapshot(value);
  }catch{
    throw Error('真实论文查询或操作未得到可核验的响应；请检查本地仓库后重试。');
  }
};
