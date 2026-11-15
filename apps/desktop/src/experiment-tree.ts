import {invoke,isTauri} from '@tauri-apps/api/core';

export type ExperimentKind='group'|'replicate';
export type ExperimentStageId='raw'|'processed'|'statistics'|'display';
export interface ExperimentReplicate {id:string;label:string}
export interface ExperimentGroup {id:string;name:string;replicates:ExperimentReplicate[]}
export interface ExperimentStage {id:ExperimentStageId;label:string;scope:'replicate'|'assay'}
export interface ExperimentResponse {assayId:string;groups:ExperimentGroup[];stages:ExperimentStage[];replicateCount:number}
export interface ExperimentRequest {
  action:'list'|'create'|'rename'|'delete';assayId:string;kind?:ExperimentKind;
  id?:string;parentId?:string;name?:string;requestId?:string;
}
export interface ExperimentStageTarget {assayId:string;stageId:ExperimentStageId;replicateId?:string}
export type ExperimentApi=(request:ExperimentRequest)=>Promise<ExperimentResponse>;
declare global {interface Window {__S2_EXPERIMENT_TREE_MOCK__?:ExperimentApi}}

const messages:Record<string,string>={
  UNAVAILABLE:'浏览器未连接原生实验存储。请在桌面应用中管理分组与样本。',
  INVALID_NAME:'名称不能为空，且须符合名称长度要求。',
  INVALID_PARENT:'所属分组或实验不可用，请重新加载后再试。',
  NOT_FOUND:'实验、分组或样本已不存在，请重新加载后再试。',
  NODE_IN_USE:'该节点有下级或关联资产，无法删除。',
  REQUEST_CONFLICT:'操作请求发生冲突，请取消后重新操作。',
  DATABASE_UNAVAILABLE:'暂时无法访问实验存储，请重试。',
  UNKNOWN_DATABASE:'实验存储格式无法识别，已拒绝打开。',
  INVALID_RESPONSE:'实验数据暂时无法读取，请重新加载。',
};
export class ExperimentError extends Error {
  constructor(public readonly code:string){super(messages[code]??'暂时无法完成操作，请重试或重新加载。');this.name='ExperimentError';}
}
/** Only locally authored error text reaches the renderer; backend paths are discarded. */
export function safeExperimentError(error:unknown):ExperimentError {
  if(error instanceof ExperimentError)return error;
  return new ExperimentError(error&&typeof error==='object'&&'code' in error&&typeof error.code==='string'?error.code:'UNKNOWN');
}
function record(value:unknown):value is Record<string,unknown>{return Boolean(value)&&typeof value==='object';}
export function validateExperimentResponse(value:unknown,assayId:string):ExperimentResponse {
  const fail=()=>{throw new ExperimentError('INVALID_RESPONSE');};
  if(!record(value)||value.assayId!==assayId||!Array.isArray(value.groups)||!Array.isArray(value.stages)||!Number.isSafeInteger(value.replicateCount))return fail();
  const ids=new Set<string>();let count=0;
  const takeId=(id:unknown)=>{if(typeof id!=='string'||!id||ids.has(id))return fail();ids.add(id);};
  for(const group of value.groups){
    if(!record(group)||typeof group.name!=='string'||!Array.isArray(group.replicates))return fail();
    takeId(group.id);
    for(const replicate of group.replicates){
      if(!record(replicate)||typeof replicate.label!=='string')return fail();
      takeId(replicate.id);count++;
    }
  }
  if(value.replicateCount!==count)return fail();
  const scopes:Record<string,string>={raw:'replicate',processed:'replicate',statistics:'assay',display:'assay'};
  const stageIds=new Set<string>();
  for(const stage of value.stages){
    if(!record(stage)||typeof stage.id!=='string'||!Object.hasOwn(scopes,stage.id)||stage.scope!==scopes[stage.id]||typeof stage.label!=='string'||stageIds.has(stage.id))return fail();
    stageIds.add(stage.id);
  }
  if(stageIds.size!==4)return fail();
  return value as unknown as ExperimentResponse;
}
export const experimentApi:ExperimentApi=async request=>{
  try{
    if(isTauri())return validateExperimentResponse(await invoke('experiment_tree',{request}),request.assayId);
    if(window.__S2_EXPERIMENT_TREE_MOCK__)return validateExperimentResponse(await window.__S2_EXPERIMENT_TREE_MOCK__(request),request.assayId);
    throw new ExperimentError('UNAVAILABLE');
  }catch(error){throw safeExperimentError(error);}
};
export function experimentMode(){
  return isTauri()?'原生 SQLite 实验存储':window.__S2_EXPERIMENT_TREE_MOCK__?'浏览器 Mock（不写入原生数据库）':'浏览器：原生实验存储不可用';
}
