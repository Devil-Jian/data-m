import React from 'react';
import {Grid2X2,LayoutGrid,List,Search,Table2} from 'lucide-react';
import type {UIKit} from './workbench';

export interface ExplorerStage<S extends string>{id:S;label:string}
/** Demo and Production use one Workbench toolbar without importing demo data.
 * Stage values may differ (demo 'stats' vs native 'statistics'); the adapter
 * always receives an explicit typed list.
 */
export function WorkbenchExplorerToolbar<S extends string>({
  ui:UI,mode,stage,stages,stageFilter,view,search,
  onMode,onStage,onStageFilter,onView,onSearch,searchExtras,filterExtras,viewOptions
}:{
  ui:UIKit;mode:'overview'|'stage';stage:S;stages:readonly ExplorerStage<S>[];
  stageFilter:readonly S[];view:'grid'|'list'|'table';search:string;
  onMode:(mode:'overview'|'stage')=>void;onStage:(stage:S)=>void;
  onStageFilter:(stages:S[])=>void;
  onView:(view:'grid'|'list'|'table')=>void;onSearch:(search:string)=>void;
  searchExtras?:React.ReactNode;filterExtras?:React.ReactNode;
  viewOptions?:readonly ('grid'|'list'|'table')[];
}){
  const available=viewOptions??['grid','list','table'];
  const icons={grid:LayoutGrid,list:List,table:Table2};
  return <>
    <div className="dm-workspace-toolbar">
      <div className="dm-mode-switch">
        <UI.Button small variant={mode==='overview'?'primary':'ghost'}
          onClick={()=>onMode('overview')}><Grid2X2 size={15}/> 全阶段全景</UI.Button>
        <UI.Button small variant={mode==='stage'?'primary':'ghost'}
          onClick={()=>onMode('stage')}><List size={15}/> 分阶段</UI.Button>
      </div>
      <div className="dm-toolbar-spacer"/>
      <span className="dm-search-icon"><Search size={15}/></span>
      <UI.Input value={search} onChange={onSearch} placeholder="搜索样本文件…" ariaLabel="搜索样本文件"/>
      {searchExtras}
    </div>
    <div className="dm-filter-toolbar">
      {mode==='overview'?<>
        <span className="dm-filter-label">展示阶段</span>
        {stages.map(s=><label key={s.id} className={'dm-stage-filter'+(stageFilter.includes(s.id)?' on':'')}>
          <UI.Check checked={stageFilter.includes(s.id)} onChange={checked=>
            onStageFilter(checked?[...new Set([...stageFilter,s.id])]:stageFilter.filter(id=>id!==s.id))}/>
          {s.label}
        </label>)}
      </>:stages.map(s=><UI.Button key={s.id} small variant={stage===s.id?'primary':'ghost'}
        onClick={()=>onStage(s.id)}>{s.label}</UI.Button>)}
      <div className="dm-toolbar-spacer"/>
      {filterExtras}
      <div className="dm-view-switch">
        {available.map(id=>{
          const Icon=icons[id];
          return <UI.Button key={id} small variant={view===id?'primary':'ghost'}
            onClick={()=>onView(id)} title={id}><Icon size={15}/></UI.Button>;
        })}
      </div>
    </div>
  </>;
}
