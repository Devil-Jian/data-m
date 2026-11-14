import type {DemoAsset} from './demo-assets';
import type {PositionedItem} from '@data-m/interaction';
export const VIRTUAL_ASSET_ROW_HEIGHT=56;

/**
 * All logical rows have exact fixed-height bounds, including unmounted rows.
 * No biological replicate is synthesized while building this layout.
 */
export function virtualAssetRects(
  assets:readonly DemoAsset[], width:number
):PositionedItem[]{
  return assets.map((asset,index)=>({
    id:asset.id,
    rect:{
      left:20,right:Math.max(width,20),
      top:index*VIRTUAL_ASSET_ROW_HEIGHT,
      bottom:(index+1)*VIRTUAL_ASSET_ROW_HEIGHT
    }
  }));
}

/** Dev-only stress fixtures: independent file assets, NOT new biological samples. */
export function makeVirtualStressAssets(
  count:number,containerId:string
):DemoAsset[]{
  return Array.from({length:count},(_,index)=>({
    id:`asset:raw:virtual-perf-${index}`,
    sourceId:`virtual-perf-${index}`,
    replicateId:null,
    containerId,
    stage:'raw' as const,
    filename:`Virtual_Asset_${String(index).padStart(5,'0')}.tif`
  }));
}
