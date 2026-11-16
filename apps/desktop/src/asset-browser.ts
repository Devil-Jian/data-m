import {invoke,isTauri} from '@tauri-apps/api/core';

export interface AssetRow {
  assetId:string;assetLinkId:string;assetVersionId:string;versionNumber:number;
  name:string;stage:'raw'|'processed'|'statistics'|'display';
  groupId:string|null;replicateId:string|null;sha256:string;sizeBytes:number;referenced:boolean;
}
export interface AssetQuery {
  assayId:string;stage?:AssetRow['stage'];groupId?:string;replicateId?:string;
  search?:string;sort?:'name'|'newest'|'size';cursor?:string;limit?:number;
}
export interface AssetPage {items:AssetRow[];total:number;nextCursor:string|null}
export interface AssetApi {
  list:(request:AssetQuery)=>Promise<AssetPage>;
  detail:(assetLinkId:string)=>Promise<{item:AssetRow}>;
}
declare global {interface Window {__S4_ASSETS_MOCK__?:AssetApi}}
const valid=(row:AssetRow)=>Boolean(row&&typeof row.assetId==='string'&&typeof row.assetLinkId==='string'&&typeof row.assetVersionId==='string'
  &&typeof row.name==='string'&&['raw','processed','statistics','display'].includes(row.stage)
  &&Number.isSafeInteger(row.versionNumber)&&row.versionNumber>0&&Number.isSafeInteger(row.sizeBytes)&&row.sizeBytes>=0
  &&/^[a-f0-9]{64}$/.test(row.sha256)&&typeof row.referenced==='boolean');
export function checkPage(value:AssetPage):AssetPage{
  if(!value||!Array.isArray(value.items)||!Number.isSafeInteger(value.total)||value.total<0
    ||value.items.length>100||!(value.nextCursor===null||/^\d+$/.test(value.nextCursor))
    ||!value.items.every(valid)||new Set(value.items.map(x=>x.assetLinkId)).size!==value.items.length)
    throw new Error('资产查询响应无效');
  return value;
}
export const assetApi:AssetApi={
  async list(request) {
    if(isTauri())return checkPage(await invoke<AssetPage>('list_assets',{request}));
    if(window.__S4_ASSETS_MOCK__)return checkPage(await window.__S4_ASSETS_MOCK__.list(request));
    throw new Error('浏览器无原生资产查询服务；请在 Windows 客户端打开真实资产。');
  },
  async detail(assetLinkId) {
    const result=isTauri()?await invoke<{item:AssetRow}>('get_asset',{request:{assetLinkId}})
      :window.__S4_ASSETS_MOCK__?await window.__S4_ASSETS_MOCK__.detail(assetLinkId):null;
    if(!result||!valid(result.item)||result.item.assetLinkId!==assetLinkId)throw new Error('文件详情查询失败或关联上下文发生变化');
    return result;
  }
};
