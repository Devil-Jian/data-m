import React from 'react';
import {ChevronRight} from 'lucide-react';

export interface FigurePanelView{
  id:string;label:string;title:string;subtitle:string;preview?:React.ReactNode;
}
export interface FigureSectionView{
  id:string;label:string;caption:string;panels:readonly FigurePanelView[];
}
/** The original Demo Figure/Panel visual hierarchy. Adapter owns all data and mutations. */
export function WorkbenchFigureList({
  figures,view,onPanelSelect,figureActions
}:{
  figures:readonly FigureSectionView[];view:'grid'|'list'|'table';
  onPanelSelect:(panelId:string)=>void;
  figureActions?:(figure:FigureSectionView)=>React.ReactNode;
}){
  return <>
    {figures.map(figure=><section className="dm-figure-section" key={figure.id}>
      <div className="dm-figure-title"><strong>{figure.label}</strong>
        <span>{figure.caption}</span>{figureActions?.(figure)}</div>
      <div className={view==='grid'?'dm-panel-grid':'dm-panel-list'}>
        {figure.panels.map(panel=><button key={panel.id} type="button"
          className="dm-paper-panel" onClick={()=>onPanelSelect(panel.id)}>
          {panel.preview??<div className="dm-real-panel-no-preview">固定版本文件 · 无模拟科研图片</div>}
          <div className="dm-paper-panel-text">
            <span>{panel.label}</span><strong>{panel.title}</strong>
            <small>{panel.subtitle}</small>
          </div><ChevronRight size={16}/>
        </button>)}
      </div>
      {!figure.panels.length&&<div className="dm-empty">当前 Figure 尚无 Panel。</div>}
    </section>)}
  </>;
}
