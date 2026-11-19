import React from 'react';
import {ArrowLeft,ArrowRight,BookOpen,Download,FlaskConical,Plus,Upload} from 'lucide-react';
import type {UIKit} from './workbench';
import type {ModuleName} from './data';

/** Visual and keyboard contract shared by Demo and native Production adapters.
 * All actions are supplied by an adapter; this view never reads demo fixtures.
 */
export interface WorkbenchHeaderProps {
  ui:UIKit;
  projectId:string;
  projectOptions:{value:string;label:string}[];
  module:ModuleName;
  onProjectChange:(id:string)=>void;
  onModuleChange:(module:ModuleName)=>void;
  canBack:boolean;canForward:boolean;
  onBack:()=>void;onForward:()=>void;
  onNewProject:()=>void;
  onImport:()=>void;
  importDisabled?:boolean;
}
export function WorkbenchHeader({
  ui:UI,projectId,projectOptions,module,onProjectChange,onModuleChange,
  canBack,canForward,onBack,onForward,onNewProject,onImport,importDisabled=false
}:WorkbenchHeaderProps){
  return <header className="dm-header">
    <div className="dm-logo"><span>BM</span><strong>科研实验与论文管理系统</strong></div>
    <div className="dm-history-controls" role="group" aria-label="目录导航历史">
      <button type="button" aria-label="后退到上一目录" title="后退 (Alt+←)"
        disabled={!canBack} onClick={onBack}><ArrowLeft size={16}/></button>
      <button type="button" aria-label="前进到下一目录" title="前进 (Alt+→)"
        disabled={!canForward} onClick={onForward}><ArrowRight size={16}/></button>
    </div>
    <div className="dm-project-picker"><small>课题/论文项目</small>
      <UI.Select value={projectId} onChange={onProjectChange}
        options={projectOptions} ariaLabel="选择科研项目"/>
    </div>
    <nav className="dm-primary-nav" aria-label="核心模块">
      <UI.Button variant={module==='experiment'?'primary':'ghost'}
        onClick={()=>onModuleChange('experiment')}><FlaskConical size={15}/> 实验整理</UI.Button>
      <UI.Button variant={module==='paper'?'primary':'ghost'}
        onClick={()=>onModuleChange('paper')}><BookOpen size={15}/> 论文管理</UI.Button>
    </nav>
    <div className="dm-header-spacer"/>
    <UI.Button variant="secondary" onClick={onNewProject}><Plus size={15}/> 新建项目</UI.Button>
    <UI.Button variant="primary" disabled={importDisabled} onClick={onImport}>
      {module==='experiment'?<Upload size={15}/>:<Download size={15}/>}
      {module==='experiment'?'导入实验归档包':'导出投稿材料包'}
    </UI.Button>
  </header>;
}
