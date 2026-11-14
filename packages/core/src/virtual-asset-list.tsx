import React, {useRef} from 'react';
import {useVirtualizer} from '@tanstack/react-virtual';
import type {DemoAsset} from './demo-assets';

/**
 * Fixed-height rows keep geometry identical between TanStack's rendered slice
 * and the Explorer marquee adapter's unmounted row hit testing.
 */
import {VIRTUAL_ASSET_ROW_HEIGHT} from './virtual-layout';
export interface VirtualAssetListProps{
  assets:readonly DemoAsset[];
  selected:ReadonlySet<string>;
  onToggle:(id:string,checked:boolean)=>void;
}
export function VirtualAssetList({assets,selected,onToggle}:VirtualAssetListProps){
  const scrollRef=useRef<HTMLDivElement>(null);
  const virtualizer=useVirtualizer({
    count:assets.length,
    getScrollElement:()=>scrollRef.current,
    getItemKey:index=>assets[index]?.id??index,
    estimateSize:()=>VIRTUAL_ASSET_ROW_HEIGHT,
    overscan:6
  });
  return <div ref={scrollRef} data-explorer-scroll-host data-explorer-virtual="true"
    className="dm-virtual-scroll" role="region" aria-label="虚拟文件资产列表"
    data-asset-count={assets.length}>
    <div className="dm-virtual-inner" style={{height:virtualizer.getTotalSize()}}>
      {virtualizer.getVirtualItems().map(item=>{
        const asset=assets[item.index];
        const checked=selected.has(asset.id);
        return <div key={item.key} data-index={item.index} data-explorer-id={asset.id}
          className={'dm-virtual-row dm-selectable-asset'+(checked?' selected':'')}
          style={{height:VIRTUAL_ASSET_ROW_HEIGHT,transform:`translateY(${item.start}px)`}}
          title={asset.filename}>
          <label className="dm-virtual-checkbox" data-explorer-no-gesture>
            <input type="checkbox" aria-label={`选择 ${asset.filename}`}
              checked={checked} onChange={e=>onToggle(asset.id,e.target.checked)}/>
          </label>
          <div className="dm-virtual-name">
            <strong>{asset.filename}</strong>
            <small>{asset.id} · {asset.stage.toUpperCase()} · 演示资产</small>
          </div>
          <span className="dm-virtual-type">{asset.stage==='raw'?'原始文件':'处理文件'}</span>
        </div>;
      })}
    </div>
  </div>;
}
