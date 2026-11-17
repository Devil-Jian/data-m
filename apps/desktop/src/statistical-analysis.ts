import {invoke,isTauri} from '@tauri-apps/api/core';
export type AnalysisAction='list'|'get'|'create'|'save'|'freeze'|'revise';
export interface AnalysisRequest {
 action:AnalysisAction;assayId:string;analysisId?:string;
 name?:string;notes?:string;replicateIds?:string[];
 inputVersionIds?:string[];resultVersionIds?:string[];
}
export interface AnalysisSummary {
 id:string;name:string;notes:string;frozenAt:string|null;updatedAt:string;includedN:number;
}
export interface InclusionRow {
 replicateId:string;groupId:string;groupName:string;label:string;included:boolean;
}
export interface VersionChoice {
 id:string;assetName:string;versionNumber:number;deleted:boolean;
}
export interface AnalysisResponse {
 assayId:string;selectedId:string|null;analyses:AnalysisSummary[];
 samples:InclusionRow[];availableVersions:VersionChoice[];
 inputVersionIds:string[];resultVersionIds:string[];
}
export type AnalysisApi=(request:AnalysisRequest)=>Promise<AnalysisResponse>;
declare global { interface Window { __S8_ANALYSIS_MOCK__?:AnalysisApi; } }
export const analysisApi:AnalysisApi=async request=>{
 if(window.__S8_ANALYSIS_MOCK__)return window.__S8_ANALYSIS_MOCK__(request);
 if(!isTauri())throw new Error('统计分析仅在桌面端使用真实 SQLite；浏览器需要明确 Mock。');
 return invoke<AnalysisResponse>('statistical_analysis',{request});
};
export function analysisError(error:unknown):string{
 if(error&&typeof error==='object'&&'code' in error) {
  const code=String(error.code);
  if(code==='FROZEN')return '已冻结分析不可直接修改；请创建修订。';
  if(code==='INVALID_VERSION')return '输入或结果文件版本不属于该实验。';
  if(code==='INVALID_REPLICATE')return '样本不存在或不属于当前实验。';
  if(code==='NOT_FOUND')return '分析或实验已不存在，请重新加载。';
 }
 return error instanceof Error?error.message:'统计分析操作失败，数据未确认写入。';
}
