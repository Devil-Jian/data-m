import React from 'react';
import {ChevronDown,ChevronRight,FlaskConical,Folder,FolderOpen,ShieldCheck} from 'lucide-react';

export interface ResearchThemeNode{
  id:string;name:string;assays:readonly {id:string;name:string}[];
}
/** Canonical sidebar for both Demo and Production.
 * The view receives only labels and callbacks, never database or demo fixtures.
 */
export function ResearchNavigationTree({
  themes,themeId,assayId,onTheme,onAssay,actions,footerText
}:{
  themes:readonly ResearchThemeNode[];themeId:string;assayId:string;
  onTheme:(id:string)=>void;onAssay:(id:string)=>void;
  actions?:React.ReactNode;footerText:string;
}){
  return <aside className="dm-sidebar">
    <div className="dm-sidebar-title"><span>研究项目 / 实验导航</span><Folder size={15}/></div>
    <div className="dm-nav-tree">
      {themes.map(t=><div key={t.id}>
        <button type="button" className={'dm-theme-row'+(t.id===themeId?' active':'')}
          onClick={()=>onTheme(t.id)}>
          {t.id===themeId?<FolderOpen size={16}/>:<Folder size={16}/>}
          <span>{t.name}</span>{t.id===themeId?<ChevronDown size={14}/>:<ChevronRight size={14}/>}
        </button>
        {themeId===t.id&&t.assays.map(a=><button key={a.id} type="button"
          className={'dm-assay-row'+(assayId===a.id?' active':'')}
          onClick={()=>onAssay(a.id)}>
          <FlaskConical size={14}/><span>{a.name}</span>
        </button>)}
      </div>)}
      {!themes.length&&<div className="dm-sidebar-empty">暂无研究主题。请新建主题及实验。</div>}
    </div>
    {actions}
    <div className="dm-sidebar-footer"><ShieldCheck size={15}/><span>{footerText}</span></div>
  </aside>;
}

export function WorkbenchStatusBar({
  status,summary,hint
}:{status:string;summary:string;hint:string}){
  return <footer className="dm-statusbar">
    <span><span className="dm-online-dot"/>{status}</span>
    <span>{summary}</span>
    <span>{hint}</span>
  </footer>;
}
