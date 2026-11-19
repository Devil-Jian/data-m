import {describe,expect,it,vi} from 'vitest';
import {fetchProductionStagePage} from './use-production-stage-assets';
import type {AssetApi,AssetRow,AssetQuery} from './asset-browser';

function raw(n:number):AssetRow{return {
  assetId:'asset-'+n,assetLinkId:'link-'+n,assetVersionId:'version-'+n,versionNumber:1,
  name:'raw-'+n,stage:'raw',groupId:'group-1',replicateId:'rep-1',
  sha256:'a'.repeat(64),sizeBytes:100,referenced:false
}}
const scope={assayId:'assay-1',stage:'raw' as const,groupId:'group-1'};
describe('S12R live S4 cursor paging',()=>{
  it('continues past the former 1000-item snapshot limit',async()=>{
    const api:AssetApi={list:vi.fn(async (req:AssetQuery)=>{
      const off=Number(req.cursor??'0'),total=1024;
      return {items:Array.from({length:Math.min(100,total-off)},(_,i)=>raw(off+i)),
        total,nextCursor:off+100<total?String(off+100):null};
    }),detail:vi.fn()};
    let page=await fetchProductionStagePage(scope,null,api);
    while(page.cursor)page=await fetchProductionStagePage(scope,page,api);
    expect(page.items).toHaveLength(1024);
    expect(new Set(page.items.map(x=>x.assetLinkId)).size).toBe(1024);
    expect(api.list).toHaveBeenCalledTimes(11);
    expect(page.cursor).toBeNull();
  });
  it('rejects repeated cursors and cross-group data',async()=>{
    const loop:AssetApi={list:vi.fn(async()=>({items:[raw(1)],total:3,nextCursor:'100'})),detail:vi.fn()};
    const first=await fetchProductionStagePage(scope,null,loop);
    await expect(fetchProductionStagePage(scope,first,loop)).rejects.toThrow(/游标重复|重复 ID/);
    const wrong:AssetApi={list:vi.fn(async()=>({items:[{...raw(1),groupId:'other'}],
      total:1,nextCursor:null})),detail:vi.fn()};
    await expect(fetchProductionStagePage(scope,null,wrong)).rejects.toThrow(/作用域/);
  });
  it('blocks sample stage without group selection',async()=>{
    await expect(fetchProductionStagePage({assayId:'assay-1',stage:'processed'})).rejects.toThrow();
  });
});
