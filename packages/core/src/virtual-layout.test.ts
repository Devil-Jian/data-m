import {describe,expect,it} from 'vitest';
import {contentMarqueeHits,selectItem} from '@data-m/interaction';
import {makeVirtualStressAssets,VIRTUAL_ASSET_ROW_HEIGHT,virtualAssetRects} from './virtual-layout';

describe('TanStack Virtual logical asset layout',()=>{
  it('builds 10,000 distinct file IDs without a biological replicate',()=>{
    const assets=makeVirtualStressAssets(10_000,'group-demo');
    expect(assets).toHaveLength(10_000);
    expect(new Set(assets.map(a=>a.id)).size).toBe(10_000);
    expect(assets.every(a=>a.replicateId===null&&a.containerId==='group-demo')).toBe(true);
  });
  it('maps a deep offscreen range to the matching file IDs',()=>{
    const assets=makeVirtualStressAssets(10_000,'group-demo');
    const layout=virtualAssetRects(assets,900);
    expect(layout[0].rect).toEqual({left:20,right:900,top:0,bottom:VIRTUAL_ASSET_ROW_HEIGHT});
    expect(layout[9_999].rect.top).toBe(9_999*VIRTUAL_ASSET_ROW_HEIGHT);
    const hit=contentMarqueeHits({
      left:1,right:600,
      top:5_001*VIRTUAL_ASSET_ROW_HEIGHT+1,
      bottom:5_006*VIRTUAL_ASSET_ROW_HEIGHT-1
    },layout,new Set(assets.map(a=>a.id)));
    expect(hit).toEqual(assets.slice(5_001,5_006).map(a=>a.id));
  });
  it('Shift range can include virtual items that are not mounted',()=>{
    const ids=makeVirtualStressAssets(10_000,'g').map(a=>a.id);
    const initial=selectItem({selected:new Set(),anchor:null,focused:null},ids[10],ids);
    const ranged=selectItem(initial,ids[1_000],ids,{shiftKey:true});
    expect(ranged.selected.size).toBe(991);
    expect(ranged.selected.has(ids[500])).toBe(true);
  });
});
