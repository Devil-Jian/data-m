import React, { useMemo, useState } from 'react';
import {
  ArrowLeft, BookOpen, Check, ChevronDown, ChevronRight, Copy, Download,
  FileArchive, FileImage, FileSpreadsheet, FileText, FlaskConical, Folder,
  FolderOpen, Grid2X2, Image, Info, LayoutGrid, List, MoreHorizontal, Plus,
  Search, ShieldCheck, Table2, Upload, X
} from 'lucide-react';
import { allPanels, findAssay, INITIAL_PROJECTS, STAGES, summary } from './data';
import { demoAssetId, selectableDemoAssets } from './demo-assets';
import { useExplorerInteraction } from '@data-m/interaction';
import type { Assay, DataFile, ModuleName, Panel, Project, Replica, Stage, ViewMode } from './data';

export interface ButtonProps {
  children?: React.ReactNode; onClick?: () => void;
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'; small?: boolean;
  disabled?: boolean; title?: string;
}
export interface SelectProps {
  value: string; onChange: (value: string) => void;
  options: { value: string; label: string }[]; ariaLabel?: string;
}
export interface InputProps {
  value: string; onChange: (value: string) => void;
  placeholder?: string; ariaLabel?: string;
}
export interface CheckProps { checked: boolean; onChange: (checked: boolean) => void; label?: string; }
export interface BadgeProps { children: React.ReactNode; tone?: 'blue'|'green'|'amber'|'neutral'; }
export interface UIKit {
  Button: React.ComponentType<ButtonProps>;
  Select: React.ComponentType<SelectProps>;
  Input: React.ComponentType<InputProps>;
  Check: React.ComponentType<CheckProps>;
  Badge: React.ComponentType<BadgeProps>;
}
export interface WorkbenchProps { ui: UIKit; variant: 'shadcn' | 'mantine' | 'fluent'; }

function Preview({type,large=false}:{type:string;large?:boolean}) {
  const bars = [32,72,48,84,59,91,63];
  if (type.includes('bar')) return <div className={'dm-preview dm-chart '+(large?'dm-preview-large':'')}>
    <div className="dm-bar-plot">{bars.slice(0,type==='bar_mda'?4:7).map((v,i)=><div key={i} className="dm-bar-wrap">
      <span className="dm-bar" style={{height:v+'%', opacity:0.55+i*.06}}/><small>{['C','M','L','H','1','2','3'][i]}</small>
    </div>)}</div><span className="dm-preview-watermark">统计示意</span></div>;
  if (type.startsWith('wb')) return <div className={'dm-preview dm-blot '+(large?'dm-preview-large':'')}>
    {[0,1,2].map(row=><div className="dm-blot-row" key={row}>{[0,1,2,3].map(col=>
      <span key={col} className="dm-blot-band" style={{opacity:0.35+((row*3+col)%5)*.12}} />)}</div>)}
    <span className="dm-preview-watermark">WB 示意</span></div>;
  if (type==='flow_scatter') return <div className={'dm-preview dm-flow '+(large?'dm-preview-large':'')}>
    <div className="dm-flow-axes">{Array.from({length:42},(_,i)=><i key={i} style={{
      left:(12+(i*37)%80)+'%',bottom:(10+(i*17+(i%5)*6)%80)+'%',opacity:.25+(i%5)*.12
    }}/>)}</div><span className="dm-preview-watermark">Flow 示意</span></div>;
  return <div className={'dm-preview dm-fluorescence '+(large?'dm-preview-large':'')}>
    {Array.from({length:type==='ros_low'?14:35},(_,i)=><i key={i} style={{
      left:(9+(i*43)%84)+'%',top:(9+(i*29)%78)+'%',
      width:(6+i%5*2)+'px',height:(6+i%5*2)+'px',
      opacity:type==='ros_low'?.26+(i%4)*.11:.38+(i%4)*.13
    }}/>)}
    <span className="dm-preview-watermark">图像示意</span></div>;
}

function meanText(assay:Assay, grpIndex:number) {
  const s=summary(assay.groups[grpIndex].replicates.filter(r=>r.inStats).map(r=>r.value));
  return s.n ? s.mean.toFixed(2)+' ± '+s.sd.toFixed(2) : '—';
}

export function Workbench({ui: UI,variant}: WorkbenchProps) {
  const [projects,setProjects]=useState<Project[]>(()=>structuredClone(INITIAL_PROJECTS));
  const [projectId,setProjectId]=useState('prj-01');
  const [themeId,setThemeId]=useState('theme-ox');
  const [assayId,setAssayId]=useState('assay-ros');
  const [groupId,setGroupId]=useState('ros-g1');
  const [module,setModule]=useState<ModuleName>('experiment');
  const [mode,setMode]=useState<'overview'|'stage'>('overview');
  const [stage,setStage]=useState<Stage>('raw');
  const [stageFilter,setStageFilter]=useState<Stage[]>(STAGES.map(s=>s.id));
  const [role,setRole]=useState<'all'|'rep'|'stats'>('all');
  const [view,setView]=useState<ViewMode>('grid');
  const [paperView,setPaperView]=useState<ViewMode>('grid');
  const [search,setSearch]=useState('');
  const [activePaperId,setActivePaperId]=useState('p1');
  const [activePanelId,setActivePanelId]=useState<string|null>(null);
  const [trace,setTrace]=useState<{projectId:string;paperId:string;panelId:string;label:string}|null>(null);
  const [newProjectOpen,setNewProjectOpen]=useState(false);
  const [newProjectName,setNewProjectName]=useState('');
  const [newThemeName,setNewThemeName]=useState('');
  const [notice,setNotice]=useState('');

  const project=projects.find(p=>p.id===projectId) || projects[0];
  const theme=project.themes.find(t=>t.id===themeId)||project.themes[0];
  const assay=theme.assays.find(a=>a.id===assayId)||theme.assays[0];
  const group=assay.groups.find(g=>g.id===groupId)||assay.groups[0];
  const paper=project.papers.find(p=>p.id===activePaperId)||project.papers[0];
  const panel=paper?.figures.flatMap(f=>f.panels).find(p=>p.id===activePanelId)||null;
  const panelAssay=panel ? findAssay(project,panel.assayId):undefined;

  const filtered=useMemo(()=>group.replicates.filter(r=>
    (role==='all'||(role==='rep'?r.isRep:r.inStats)) &&
    (!search||[r.name,r.procName,r.id].some(v=>v.toLowerCase().includes(search.toLowerCase())))
  ),[group,role,search]);
  // The Explorer interacts with file assets; one biological replicate may own multiple assets.
  const selectableAssets=useMemo(()=>selectableDemoAssets(assay,group,filtered,mode,stage,stageFilter),
    [assay,group,filtered,mode,stage,stageFilter]);
  const visibleIds=useMemo(()=>selectableAssets.map(a=>a.id),[selectableAssets]);
  const explorer=useExplorerInteraction({
    visibleIds,enabled:module==='experiment'&&!activePanelId&&!newProjectOpen,
    onDrop:({itemIds,targetId,operation})=>{
      const assets=itemIds.map(id=>selectableAssets.find(a=>a.id===id));
      if(assets.some(a=>!a)){notify('文件选择已变化，请重新选择');return;}
      if(assets.some(a=>a!.containerId!==group.id)){
        notify('统计/展示资产属于整个实验，不能直接拖入生物学分组');
        return;
      }
      // Demo only: never turn file copy into another biological replicate.
      notify('文件资产操作请求：'+(operation==='copy'?'复制':'移动')+' '+assets.length+' 个文件到 '+(assay.groups.find(g=>g.id===targetId)?.name||targetId)+'；真实操作待后端实现');
    },
    onCopy:(ids)=>notify('已选择复制 '+ids.length+' 个演示项'),
    onCut:(ids)=>notify('已选择剪切 '+ids.length+' 个演示项'),
    onPaste:(ids,operation)=>notify('请求'+(operation==='copy'?'复制':'移动')+' '+ids.length+' 项；需选择目标容器并连接后端'),
    onDelete:(ids)=>notify('请求删除 '+ids.length+' 个演示项；需后端引用保护'),
    onRename:(id)=>notify('重命名 '+id+' 需后端支持')
  });
  const selection=explorer.selectedIds;
  const setSelection=explorer.setSelectedIds;
  const selectedCount=selection.length;
  const stageName=(s:Stage)=>STAGES.find(a=>a.id===s)?.label||s;
  const notify=(s:string)=>{setNotice(s); window.setTimeout(()=>setNotice(old=>old===s?'':old),4300)};
  function chooseProject(id:string) {
    const p=projects.find(x=>x.id===id);if(!p)return;
    setProjectId(id);setThemeId(p.themes[0]?.id||'');setAssayId(p.themes[0]?.assays[0]?.id||'');
    setGroupId(p.themes[0]?.assays[0]?.groups[0]?.id||'');setActivePaperId(p.papers[0]?.id||'');
    setSelection([]);setActivePanelId(null);setTrace(null);
  }
  function chooseTheme(id:string) {
    const t=project.themes.find(x=>x.id===id);if(!t)return;
    setThemeId(id);setAssayId(t.assays[0]?.id||'');setGroupId(t.assays[0]?.groups[0]?.id||'');
    setSelection([]);setSearch('');
  }
  function chooseAssay(id:string) {
    const a=theme.assays.find(x=>x.id===id);if(!a)return;
    setAssayId(id);setGroupId(a.groups[0]?.id||'');setSelection([]);setSearch('');
  }
  function updateReplica(repId:string,key:'inStats'|'isRep') {
    setProjects(prev=>prev.map(p=>p.id!==projectId?p:{
      ...p,themes:p.themes.map(t=>t.id!==theme.id?t:{
        ...t,assays:t.assays.map(a=>a.id!==assay.id?a:{
          ...a,groups:a.groups.map(g=>({...g,replicates:g.replicates.map(r=>r.id!==repId?r:{...r,[key]:!r[key]})}))
        })
      })
    }));
  }
  function setSelected(id:string,checked:boolean) {
    explorer.toggle(id,checked);
  }
  function repAssetIds(rep:Replica) {
    const stages=mode==='overview'?stageFilter:[stage];
    return stages.filter((s):s is 'raw'|'processed'=>s==='raw'||s==='processed')
      .map(s=>demoAssetId(s,rep.id));
  }
  function selectRepAssets(rep:Replica,checked:boolean) {
    const ids=new Set(selection);
    for(const id of repAssetIds(rep)){if(checked)ids.add(id);else ids.delete(id);}
    setSelection([...ids]);
  }
  function exportManifest() {
    const header=['project','theme','assay','group','sample','raw','processed','included_in_stats','representative'];
    const data=filtered.map(r=>[project.code,theme.code,assay.code,group.name,r.id,r.name,r.procName,r.inStats,r.isRep]);
    const csv=[header,...data].map(row=>row.map(v=>'"'+String(v).replaceAll('"','""')+'"').join(',')).join('\r\n');
    const url=URL.createObjectURL(new Blob(['\ufeff'+csv],{type:'text/csv;charset=utf-8'}));
    const a=document.createElement('a');a.href=url;a.download='data-m-demo-manifest.csv';a.click();
    window.setTimeout(()=>URL.revokeObjectURL(url),1000);
    notify('已导出演示 CSV 清单；真实文件 ZIP 打包需后端支持');
  }
  function traceToAssay(p:Panel) {
    const found=project.themes.find(t=>t.assays.some(a=>a.id===p.assayId));
    const target=found?.assays.find(a=>a.id===p.assayId);
    if(!found||!target)return;
    setTrace({projectId, paperId:paper.id,panelId:p.id,label:p.label});
    setModule('experiment');setThemeId(found.id);setAssayId(target.id);
    const groupWithRep=target.groups.find(g=>g.replicates.some(r=>p.highlightRepIds.includes(r.id)));
    setGroupId(groupWithRep?.id||target.groups[0].id);
    setMode('overview');setStageFilter(STAGES.map(s=>s.id));
    setRole('all');setSearch('');setSelection([]);setActivePanelId(null);
  }
  function jumpToPaper(p:Panel) {
    const owner=project.papers.find(x=>x.figures.some(f=>f.panels.some(q=>q.id===p.id)));
    if(owner)setActivePaperId(owner.id);
    setModule('paper');setActivePanelId(p.id);
  }
  function createProject() {
    const name=newProjectName.trim();const topic=newThemeName.trim();
    if(!name||!topic){notify('请填写项目名称和研究主题');return}
    const id='prj-custom-'+Date.now();
    const newProject:Project={
      id,code:'PRJ-DEMO-'+String(projects.length+1).padStart(2,'0'),
      name,type:'个人科研项目',papers:[],
      themes:[{id:id+'-theme',name:topic,code:'THM-DEMO',assays:[{
        id:id+'-assay',code:'ASSAY-DEMO',name:'新实验单元（示例）',
        modality:'待设置',metricName:'待设置',metricUnit:'AU',
        groups:[{id:id+'-group',name:'Control',replicates:[]}],statFiles:[],displayFiles:[]
      }]}]
    };
    setProjects(prev=>[...prev,newProject]);setNewProjectOpen(false);
    setNewProjectName('');setNewThemeName('');chooseProjectAfterCreate(newProject);
    notify('已在当前会话创建演示项目（刷新后恢复示例数据）');
  }
  function chooseProjectAfterCreate(p:Project) {
    setProjectId(p.id);setThemeId(p.themes[0].id);setAssayId(p.themes[0].assays[0].id);
    setGroupId(p.themes[0].assays[0].groups[0].id);setActivePaperId('');
    setModule('experiment');setSelection([]);setSearch('');
  }
  const allPapers=allPanels(project);
  function refPanels(rep:Replica) {return allPapers.filter(p=>p.highlightRepIds.includes(rep.id));}
  function row(rep:Replica,s:Stage) {
    if(s==='raw'||s==='processed') {
      const name=s==='raw'?rep.name:rep.procName;
      const id=demoAssetId(s,rep.id);
      return <div data-explorer-id={id} title={'演示文件资产：'+name}
        className={'dm-asset-cell dm-selectable-asset'+(selection.includes(id)?' selected':'')}>
        <div className="dm-small-thumb"><Preview type={rep.visual}/></div>
        <div className="dm-asset-meta"><strong title={name}>{name}</strong>
          <span>{s==='raw'?'原始文件（演示）':'处理文件（演示）'}</span></div>
      </div>;
    }
    if(s==='stats')return <div className="dm-asset-cell dm-compact">
      <FileSpreadsheet size={16}/><div className="dm-asset-meta">
        <strong>{rep.inStats?'计入统计':'未纳入统计'}</strong>
        <span>{rep.inStats?assay.statFiles[0]?.name||'统计文件待上传':'该样本未参加本次汇总'}</span>
      </div>
    </div>;
    return <div className="dm-asset-cell dm-compact">
      <FileImage size={16}/><div className="dm-asset-meta">
        <strong>{rep.isRep?'论文代表图源':'非代表性样本'}</strong>
        <span>{rep.isRep?assay.displayFiles[0]?.name||'展示文件待上传':'保留原始/处理文件'}</span>
      </div>
    </div>;
  }
  function sampleBadges(rep:Replica) {
    return <span className="dm-badges">
      {rep.inStats&&<UI.Badge tone="green">统计计入</UI.Badge>}
      {rep.isRep&&<UI.Badge tone="blue">代表图源</UI.Badge>}
      {refPanels(rep).map(p=><button type="button" className="dm-ref-chip" key={p.id}
        title={'定位 '+p.label} onClick={()=>jumpToPaper(p)}>{p.label} ↗</button>)}
    </span>;
  }
  function sampleCard(rep:Replica) {
    const assetId=demoAssetId(stage,rep.id);
    return <div key={assetId} data-explorer-id={assetId} className={'dm-sample-card'+(selection.includes(assetId)?' selected':'')+
      (trace&&allPapers.some(p=>p.id===trace.panelId&&p.highlightRepIds.includes(rep.id))?' highlighted':'')}>
      <div className="dm-card-head">
        <UI.Check checked={selection.includes(assetId)} onChange={v=>setSelected(assetId,v)}/>
        <strong title={stage==='raw'?rep.name:rep.procName}>{stage==='raw'?rep.name:rep.procName}</strong>
        <span className="dm-card-muted">{rep.value.toFixed(2)} {assay.metricUnit}</span>
      </div>
      <Preview type={rep.visual}/>
      <div className="dm-card-tags">{sampleBadges(rep)}</div>
      <div className="dm-card-actions">
        <button type="button" onClick={()=>updateReplica(rep.id,'inStats')}>{rep.inStats?'移出统计':'计入统计'}</button>
        <button type="button" onClick={()=>updateReplica(rep.id,'isRep')}>{rep.isRep?'取消代表图':'设为代表图'}</button>
      </div>
    </div>;
  }
  function overviewMatrix() {
    const visibleStages=STAGES.filter(s=>stageFilter.includes(s.id));
    if(!visibleStages.length)return <div className="dm-empty">请选择至少一个数据阶段</div>;
    const selectedInRow=(rep:Replica)=>repAssetIds(rep).length>0&&repAssetIds(rep).every(id=>selection.includes(id));
    if(view==='grid')return <div className="dm-matrix-scroll"><table className="dm-matrix">
      <thead><tr><th>生物学重复（勾选本行文件）</th>
        {visibleStages.map(s=><th key={s.id}>{s.label}<small>{s.short}</small></th>)}</tr></thead>
      <tbody>{filtered.map(rep=><tr key={rep.id}>
        <td className="dm-sample-label"><div className="dm-sample-title">
          <UI.Check checked={selectedInRow(rep)} onChange={v=>selectRepAssets(rep,v)}/>
          <strong>{rep.name.replace(/\.[^.]+$/,'')}</strong>
        </div><small>{rep.value.toFixed(2)} {assay.metricUnit}</small>
        <div>{sampleBadges(rep)}</div></td>
        {visibleStages.map(s=><td key={s.id}>{row(rep,s.id)}</td>)}
      </tr>)}</tbody>
    </table></div>;
    return <div className={view==='list'?'dm-list':'dm-table-outer'}>
      <table className="dm-data-table"><thead><tr>
        <th><UI.Check checked={visibleIds.length>0&&visibleIds.every(id=>selection.includes(id))}
          onChange={v=>setSelection(v?visibleIds:[])}/></th>
        <th>生物学重复</th><th>测量值</th><th>角色</th>
        {visibleStages.map(s=><th key={s.id}>{s.label}</th>)}
      </tr></thead><tbody>{filtered.map(rep=><tr key={rep.id}>
        <td><UI.Check checked={selectedInRow(rep)} onChange={v=>selectRepAssets(rep,v)}/></td>
        <td className="dm-nowrap"><strong>{rep.name}</strong></td>
        <td>{rep.value.toFixed(2)}</td><td>{sampleBadges(rep)}</td>
        {visibleStages.map(s=><td key={s.id}>
          {s.id==='raw'||s.id==='processed'
            ?<div data-explorer-id={demoAssetId(s.id,rep.id)}
              className={'dm-selectable-asset dm-inline-asset'+(selection.includes(demoAssetId(s.id,rep.id))?' selected':'')}>
                {s.id==='raw'?rep.name:rep.procName}</div>
            :s.id==='stats'?(rep.inStats?'已纳入统计':'—'):(rep.isRep?'展示源':'—')}
        </td>)}
      </tr>)}</tbody></table>
    </div>;
  }
  function stageContent() {
    if(stage==='stats')return <div className="dm-stat-grid">
      <section className="dm-content-card"><h3><FileSpreadsheet size={17}/> 统计分析文件</h3>
        {assay.statFiles.map(f=><div
          className={'dm-file-line dm-selectable-asset'+(selection.includes(demoAssetId('stats',f.id))?' selected':'')}
          data-explorer-id={demoAssetId('stats',f.id)} key={f.id}>
          <FileSpreadsheet size={18}/><div><strong>{f.name}</strong><small>{f.note}</small></div>
          <UI.Badge tone="green">汇总文件</UI.Badge></div>)}
        {!assay.statFiles.length&&<p className="dm-muted">尚无统计文件</p>}
      </section>
      <section className="dm-content-card"><h3>分组统计概览</h3>
        <table className="dm-data-table"><thead><tr><th>分组</th><th>n</th><th>Mean ± SD</th></tr></thead>
          <tbody>{assay.groups.map((g,i)=><tr key={g.id}><td>{g.name}</td>
            <td>{g.replicates.filter(r=>r.inStats).length}</td>
            <td>{meanText(assay,i)}</td></tr>)}</tbody></table>
        <p className="dm-muted dm-note">演示数据描述统计；正式统计文件需要后端独立管理。</p>
      </section>
    </div>;
    if(stage==='display')return <div className="dm-display-grid">{assay.displayFiles.map(f=>
      <div className={'dm-content-card dm-selectable-asset'+(selection.includes(demoAssetId('display',f.id))?' selected':'')}
        data-explorer-id={demoAssetId('display',f.id)} key={f.id}>
        <Preview type={f.visual} large/><h3>{f.name}</h3><p className="dm-muted">{f.note}</p>
        {allPapers.filter(p=>p.displayIds.includes(f.id)).map(p=><button
          key={p.id} className="dm-ref-chip" onClick={()=>jumpToPaper(p)}>{p.label} ↗</button>)}
      </div>)}
      {!assay.displayFiles.length&&<div className="dm-empty">暂无展示文件</div>}</div>;
    return <div className="dm-cards">{filtered.map(sampleCard)}</div>;
  }
  const paperPanels=(paper?.figures||[]).flatMap(f=>f.panels);
  const statsForPaper=Array.from(new Set(paperPanels.flatMap(p=>p.statIds))).map(id=>
    project.themes.flatMap(t=>t.assays).flatMap(a=>a.statFiles).find(s=>s.id===id)).filter((x):x is DataFile=>Boolean(x));

  return <div className={'dm-app dm-'+variant}>
    <header className="dm-header">
      <div className="dm-logo"><span>BM</span><strong>科研实验与论文管理系统</strong></div>
      <div className="dm-project-picker"><small>课题/论文项目</small><UI.Select value={project.id}
        onChange={chooseProject} options={projects.map(p=>({value:p.id,label:p.name}))} ariaLabel="选择科研项目"/></div>
      <nav className="dm-primary-nav" aria-label="核心模块">
        <UI.Button variant={module==='experiment'?'primary':'ghost'} onClick={()=>setModule('experiment')}><FlaskConical size={15}/> 实验整理</UI.Button>
        <UI.Button variant={module==='paper'?'primary':'ghost'} onClick={()=>setModule('paper')}><BookOpen size={15}/> 论文管理</UI.Button>
      </nav>
      <div className="dm-header-spacer"/>
      <UI.Button variant="secondary" onClick={()=>setNewProjectOpen(true)}><Plus size={15}/> 新建项目</UI.Button>
      <UI.Button variant="primary" onClick={()=>notify(module==='experiment'?'演示模式：真实 ZIP 导入待后端接入':'演示模式：真实投稿材料打包待后端接入')}>
        {module==='experiment'?<Upload size={15}/>:<Download size={15}/>}
        {module==='experiment'?'导入实验归档包':'导出投稿材料包'}
      </UI.Button>
    </header>
    {trace&&<div className="dm-trace-banner"><ShieldCheck size={16}/><strong>溯源模式</strong>
      <span>正在查看 {trace.label} 关联的实验数据与全部生物学重复</span>
      <UI.Button small variant="secondary" onClick={()=>{setProjectId(trace.projectId);setActivePaperId(trace.paperId);
        setModule('paper');setActivePanelId(trace.panelId);setTrace(null)}}><ArrowLeft size={14}/> 返回论文</UI.Button></div>}
    <div className="dm-body">
      <aside className="dm-sidebar">
        <div className="dm-sidebar-title"><span>{module==='experiment'?'研究项目 / 实验导航':'论文导航'}</span><Folder size={15}/></div>
        {module==='experiment'?<div className="dm-nav-tree">
          {project.themes.map(t=><div key={t.id}>
            <button className={'dm-theme-row'+(theme.id===t.id?' active':'')} onClick={()=>chooseTheme(t.id)}>
              {theme.id===t.id?<FolderOpen size={16}/>:<Folder size={16}/>}<span>{t.name}</span>
              {theme.id===t.id?<ChevronDown size={14}/>:<ChevronRight size={14}/>}
            </button>
            {theme.id===t.id&&t.assays.map(a=><button key={a.id} className={'dm-assay-row'+(assay.id===a.id?' active':'')}
              onClick={()=>chooseAssay(a.id)}><FlaskConical size={14}/><span>{a.name}</span></button>)}
          </div>)}
        </div>:<div className="dm-nav-tree">
          {project.papers.map(p=><button key={p.id} className={'dm-theme-row'+(paper?.id===p.id?' active':'')}
            onClick={()=>{setActivePaperId(p.id);setActivePanelId(null)}}>
            <BookOpen size={16}/><span>{p.code}</span><ChevronRight size={14}/>
          </button>)}
          {!project.papers.length&&<div className="dm-sidebar-empty">暂无论文条目</div>}
        </div>}
        <div className="dm-sidebar-footer"><ShieldCheck size={15}/><span>本地单机 · 演示数据</span></div>
      </aside>
      <main className="dm-workspace">
        {module==='experiment'?<>
          <div className="dm-workspace-head">
            <div className="dm-crumb">{project.code}<ChevronRight size={13}/>{theme.name}<ChevronRight size={13}/>{assay.name}</div>
            <h1>{assay.name}</h1><p>{assay.code} · {assay.modality} · {assay.metricName}（{assay.metricUnit}）</p>
            <div className="dm-group-tabs" role="group" aria-label="实验分组">
              {assay.groups.map(g=><span key={g.id} data-drop-container-id={g.id}
                className={'dm-drop-target'+(explorer.gesture.kind==='drag'&&explorer.gesture.targetId===g.id&&explorer.gesture.allowed?' dm-drop-allowed':'')}>
                <UI.Button small variant={group.id===g.id?'primary':'secondary'} onClick={()=>{
                  setGroupId(g.id);setSelection([]);setSearch('')}}>{g.name} <span className="dm-group-count">{g.replicates.length}</span></UI.Button>
              </span>)}
            </div>
          </div>
          <div className="dm-workspace-toolbar">
            <div className="dm-mode-switch">
              <UI.Button small variant={mode==='overview'?'primary':'ghost'} onClick={()=>setMode('overview')}><Grid2X2 size={15}/> 全阶段全景</UI.Button>
              <UI.Button small variant={mode==='stage'?'primary':'ghost'} onClick={()=>setMode('stage')}><List size={15}/> 分阶段</UI.Button>
            </div>
            <div className="dm-toolbar-spacer"/>
            <span className="dm-search-icon"><Search size={15}/></span><UI.Input value={search} onChange={setSearch} placeholder="搜索样本文件…" ariaLabel="搜索样本文件"/>
            <UI.Select value={role} onChange={v=>setRole(v as 'all'|'rep'|'stats')}
              options={[{value:'all',label:'全部样本'},{value:'rep',label:'仅代表图'},{value:'stats',label:'仅计入统计'}]}/>
          </div>
          {mode==='overview'?<div className="dm-filter-toolbar">
            <span className="dm-filter-label">展示阶段</span>
            {STAGES.map(s=><label key={s.id} className={'dm-stage-filter'+(stageFilter.includes(s.id)?' on':'')}>
              <UI.Check checked={stageFilter.includes(s.id)} onChange={v=>setStageFilter(prev=>v?
                Array.from(new Set([...prev,s.id])):prev.filter(x=>x!==s.id))}/>{s.label}</label>)}
            <div className="dm-toolbar-spacer"/>
            <div className="dm-view-switch">
              {([['grid',LayoutGrid],['list',List],['table',Table2]] as const).map(([v,Icon])=>
                <UI.Button key={v} small variant={view===v?'primary':'ghost'} onClick={()=>setView(v)} title={v}><Icon size={15}/></UI.Button>)}
            </div>
          </div>:<div className="dm-filter-toolbar">
            {STAGES.map(s=><UI.Button key={s.id} small variant={stage===s.id?'primary':'ghost'} onClick={()=>setStage(s.id)}>
              {s.label}</UI.Button>)}
          </div>}
          <div className="dm-workspace-scroll dm-explorer-canvas" data-source-container-id={group.id}
            onPointerDown={explorer.onPointerDown} onDragStart={e=>e.preventDefault()}>
            <div className="dm-section-heading"><div><h2>{mode==='overview'?'全阶段归档总览':stageName(stage)}
              <small>{mode==='overview'?'样本 × 数据阶段':'单阶段详细浏览'}</small></h2>
              <p>{group.name} · {filtered.length} 个样本 · {assay.groups.length} 个实验分组</p></div>
              <div className="dm-selection-actions">
                {selectedCount>0&&<UI.Badge tone="blue">已选 {selectedCount} 个文件资产</UI.Badge>}
                <UI.Button small variant="secondary" onClick={exportManifest}><Download size={14}/> 导出清单</UI.Button>
                <UI.Button small variant="ghost" onClick={()=>notify('演示模式：复制和移动真实文件需接入本地后端')}><Copy size={14}/> 复制/移动</UI.Button>
              </div>
            </div>
            {mode==='overview'?overviewMatrix():stageContent()}
            {filtered.length===0&&mode==='overview'&&<div className="dm-empty">没有符合条件的样本</div>}
            <div className="dm-experiment-footnote"><Info size={14}/> 当前展示的是科研业务模拟数据及示意图，不包含真实科研文件。操作状态仅保留在本页会话内。</div>
          </div>
        </>:<div className="dm-paper-workspace">
          <div className="dm-workspace-head"><div className="dm-crumb">{project.code}<ChevronRight size={13}/>论文管理</div>
            <h1>论文管理 <UI.Badge tone="neutral">{project.papers.length} 篇</UI.Badge></h1>
            {paper&&<><p>{paper.title}</p><div className="dm-paper-meta">
              <UI.Badge tone="blue">{paper.journal}</UI.Badge><UI.Badge tone="amber">{paper.status}</UI.Badge>
              <span>{paper.figures.length} Figures · {paperPanels.length} Panels</span>
            </div></>}
          </div>
          {paper?<><div className="dm-workspace-toolbar"><h2>论文插图与统计数据</h2><div className="dm-toolbar-spacer"/>
            <div className="dm-view-switch">{([['grid',LayoutGrid],['list',List],['table',Table2]] as const).map(([v,Icon])=>
              <UI.Button key={v} small variant={paperView===v?'primary':'ghost'} onClick={()=>setPaperView(v)}><Icon size={15}/></UI.Button>)}</div>
            <UI.Button small variant="secondary" onClick={()=>notify('演示模式：投稿打包功能需要后端提供文件 ZIP')}><Download size={14}/> 投稿材料</UI.Button>
          </div>
          <div className="dm-workspace-scroll">
            {paper.figures.map(fig=><section className="dm-figure-section" key={fig.id}>
              <div className="dm-figure-title"><strong>{fig.figNum}</strong><span>{fig.caption}</span></div>
              <div className={paperView==='grid'?'dm-panel-grid':'dm-panel-list'}>{fig.panels.map(p=>
                <button key={p.id} className="dm-paper-panel" onClick={()=>setActivePanelId(p.id)}>
                  <Preview type={p.visual} large={paperView==='grid'}/>
                  <div className="dm-paper-panel-text"><span>{p.label}</span><strong>{p.title}</strong>
                    <small>{p.statIds.length} 份统计依据 · {p.highlightRepIds.length} 个代表性来源</small></div>
                  <ChevronRight size={16}/></button>)}</div>
            </section>)}
            <section className="dm-content-card dm-paper-stats"><h3><FileSpreadsheet size={16}/> 论文关联统计数据</h3>
              {statsForPaper.map(f=><div className="dm-file-line" key={f.id}><FileSpreadsheet size={18}/>
                <div><strong>{f.name}</strong><small>{f.note}</small></div><UI.Badge tone="green">已关联</UI.Badge></div>)}
            </section>
          </div></>:<div className="dm-empty">当前项目还没有论文。此处保留论文管理工作区布局。</div>}
        </div>}
      </main>
    </div>
    {module==='experiment'&&explorer.gesture.kind==='marquee'&&<div className="dm-marquee" aria-hidden
      style={{left:explorer.gesture.rect.left,top:explorer.gesture.rect.top,
        width:explorer.gesture.rect.right-explorer.gesture.rect.left,
        height:explorer.gesture.rect.bottom-explorer.gesture.rect.top}}/>}
    {module==='experiment'&&explorer.gesture.kind==='drag'&&<div className={'dm-drag-preview'+(explorer.gesture.allowed?' is-valid':' is-invalid')}
      aria-hidden style={{left:explorer.gesture.x+14,top:explorer.gesture.y+14}}>
      {explorer.gesture.operation==='copy'?'＋ 复制':'↗ 移动'} {explorer.gesture.count} 项
      {explorer.gesture.targetId?(explorer.gesture.allowed?' · 松开以提交':' · 不可放置'):' · 拖至上方分组'}
    </div>}
    <footer className="dm-statusbar"><span><span className="dm-online-dot"/> UI Comparison · {variant}</span>
      <span>{module==='experiment'?(assay.groups.reduce((n,g)=>n+g.replicates.length,0)+' 个实验样本'):(paperPanels.length+' 个论文子图')}</span>
      <span>{module==='experiment'?'左键框选 · Ctrl 多选 · Shift 连选 · Ctrl 拖拽复制':'演示模式 · 无后端连接'}</span></footer>
    {panel&&panelAssay&&<div className="dm-drawer-backdrop" onMouseDown={()=>setActivePanelId(null)}>
      <aside className="dm-drawer" onMouseDown={e=>e.stopPropagation()} role="dialog" aria-modal="true" aria-label="论文子图详情">
        <div className="dm-drawer-top"><div><UI.Badge tone="blue">{paper.code} · {panel.label}</UI.Badge><h2>{panel.title}</h2></div>
          <UI.Button small variant="ghost" onClick={()=>setActivePanelId(null)}><X size={18}/></UI.Button></div>
        <div className="dm-drawer-scroll">
          <h3>01 · 展示数据（04_Display）</h3><Preview type={panel.visual} large/>
          {panelAssay.displayFiles.filter(f=>panel.displayIds.includes(f.id)).map(f=><div className="dm-file-line" key={f.id}>
            <FileImage size={18}/><div><strong>{f.name}</strong><small>{f.note}</small></div></div>)}
          <h3>02 · 支撑统计数据（03_Statistics）</h3>
          {panelAssay.statFiles.filter(f=>panel.statIds.includes(f.id)).map(f=><div className="dm-file-line" key={f.id}>
            <FileSpreadsheet size={18}/><div><strong>{f.name}</strong><small>{f.note}</small></div></div>)}
          <table className="dm-data-table"><thead><tr><th>分组</th><th>n</th><th>Mean ± SD</th></tr></thead>
            <tbody>{panelAssay.groups.map((g,i)=><tr key={g.id}>
              <td>{g.name}</td><td>{g.replicates.filter(r=>r.inStats).length}</td><td>{meanText(panelAssay,i)}</td>
            </tr>)}</tbody></table>
          <h3>03 · 代表性样本</h3><p className="dm-muted">{panel.note}</p>
          <div className="dm-rep-list">{panelAssay.groups.flatMap(g=>g.replicates).filter(r=>panel.highlightRepIds.includes(r.id)).map(r=>
            <UI.Badge key={r.id} tone="blue">{r.name}</UI.Badge>)}</div>
          <UI.Button variant="primary" onClick={()=>traceToAssay(panel)}><ShieldCheck size={16}/> 溯源至实验单元全部归档文件</UI.Button>
        </div>
      </aside></div>}
    {newProjectOpen&&<div className="dm-modal-backdrop" onMouseDown={()=>setNewProjectOpen(false)}>
      <div className="dm-modal" role="dialog" aria-modal="true" aria-label="新建科研项目" onMouseDown={e=>e.stopPropagation()}>
        <h2><Plus size={18}/> 新建课题 / 论文项目</h2><p className="dm-muted">创建独立科研项目及初始研究主题（仅本次演示会话）。</p>
        <label>项目名称</label><UI.Input value={newProjectName} onChange={setNewProjectName} placeholder="例如：铁死亡调控机制研究"/>
        <label>初始研究主题</label><UI.Input value={newThemeName} onChange={setNewThemeName} placeholder="例如：GPX4 / SLC7A11 通路验证"/>
        <div className="dm-modal-actions"><UI.Button variant="secondary" onClick={()=>setNewProjectOpen(false)}>取消</UI.Button>
          <UI.Button variant="primary" onClick={createProject}>创建并切换</UI.Button></div>
      </div>
    </div>}
    {notice&&<div className="dm-toast" role="status"><Info size={16}/>{notice}
      <button onClick={()=>setNotice('')} aria-label="关闭通知"><X size={14}/></button></div>}
  </div>;
}
