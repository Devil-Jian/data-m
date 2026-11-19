import React,{useCallback,useEffect,useMemo,useRef,useState} from 'react';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import {projectTreeApi,safeProjectTreeError} from './project-tree';
import type {ProjectTreeKind,ProjectTreeNode,ProjectTreeRequest} from './project-tree';
import {experimentApi,safeExperimentError} from './experiment-tree';
import type {ExperimentKind,ExperimentRequest} from './experiment-tree';
import {productionAdapter,PRODUCTION_STAGES} from './production-adapter';
import type {ProductionAssaySnapshot,ProductionProject,ProductionStage} from './production-adapter';
import type {AssetRow} from './asset-browser';
import './production-workbench.css';

const labels:Record<ProductionStage,string>={
  raw:'01_Raw',processed:'02_Processed',statistics:'03_Statistics',display:'04_Display'
};
type Editor={
  action:'create'|'rename'|'delete';
  kind:ProjectTreeKind|ExperimentKind;
  id?:string;parentId?:string;name:string;
  scope:'project'|'experiment';
  request?:ProjectTreeRequest|ExperimentRequest;
};
type View='grid'|'list'|'table';

/** A production entry point. No demo fixtures or simulated previews are imported. */
export function ProductionWorkbench(){
  const [projects,setProjects]=useState<ProductionProject[]>([]);
  const [projectId,setProjectId]=useState('');
  const [themeId,setThemeId]=useState('');
  const [assayId,setAssayId]=useState('');
  const [groupId,setGroupId]=useState('');
  const [snapshot,setSnapshot]=useState<ProductionAssaySnapshot|null>(null);
  const [mode,setMode]=useState<'overview'|'stage'>('overview');
  const [stage,setStage]=useState<ProductionStage>('raw');
  const [stageFilter,setStageFilter]=useState<ProductionStage[]>([...PRODUCTION_STAGES]);
  const [view,setView]=useState<View>('grid');
  const [search,setSearch]=useState('');
  const [selectedIds,setSelectedIds]=useState<string[]>([]);
  const [module,setModule]=useState<'experiment'|'paper'>('experiment');
  const [editor,setEditor]=useState<Editor|null>(null);
  const [busy,setBusy]=useState(false);
  const [loaded,setLoaded]=useState(false);
  const [error,setError]=useState('');
  const [detail,setDetail]=useState<AssetRow|null>(null);
  const requestVersion=useRef(0);
  const project=projects.find(p=>p.id===projectId);
  const theme=project?.themes.find(t=>t.id===themeId);
  const assay=theme?.assays.find(a=>a.id===assayId);
  const group=snapshot?.groups.find(g=>g.id===groupId);
  const rows=useMemo(()=>snapshot?.rows.filter(r=>r.groupId===groupId&&
    (!search||r.name.toLowerCase().includes(search.toLowerCase())||
     [...r.raw,...r.processed].some(f=>f.name.toLowerCase().includes(search.toLowerCase())))??[],
    [snapshot,groupId,search]);
  const assets=useMemo(()=>{
    if(!snapshot)return [];
    const source=stage==='raw'||stage==='processed'
      ?rows.flatMap(row=>row[stage]):snapshot.assayFiles[stage];
    return source.filter(f=>!search||f.name.toLowerCase().includes(search.toLowerCase()));
  },[snapshot,stage,rows,search]);
  const selectProject=(id:string)=>{
    const next=projects.find(p=>p.id===id);
    const t=next?.themes[0],a=t?.assays[0];
    setProjectId(id);setThemeId(t?.id??'');setAssayId(a?.id??'');
    setGroupId('');setSnapshot(null);setSelectedIds([]);setDetail(null);setSearch('');
  };
  const refreshProjects=useCallback(async()=>{
    const value=await productionAdapter.loadProjects();
    setProjects(value);setLoaded(true);
    return value;
  },[]);
  useEffect(()=>{
    let active=true;
    void productionAdapter.loadProjects().then(value=>{
      if(!active)return;
      setProjects(value);setLoaded(true);
      setProjectId(old=>value.some(p=>p.id===old)?old:value[0]?.id??'');
    }).catch(()=>{if(active){setLoaded(true);setError('无法读取真实项目。请确认 Windows 原生存储可用。');}});
    return ()=>{active=false;};
  },[]);
  useEffect(()=>{
    const current=projects.find(p=>p.id===projectId);
    if(!current){setThemeId('');setAssayId('');return;}
    const t=current.themes.find(t=>t.id===themeId)??current.themes[0];
    if(t?.id!==themeId)setThemeId(t?.id??'');
    const a=t?.assays.find(a=>a.id===assayId)??t?.assays[0];
    if(a?.id!==assayId)setAssayId(a?.id??'');
  },[projects,projectId,themeId,assayId]);
  const refreshAssay=useCallback(async(id:string)=>{
    const next=await productionAdapter.loadAssay(id);
    setSnapshot(next);
    setGroupId(old=>next.groups.some(g=>g.id===old)?old:next.groups[0]?.id??'');
  },[]);
  useEffect(()=>{
    const token=++requestVersion.current;
    setSnapshot(null);setGroupId('');setSelectedIds([]);setDetail(null);setSearch('');
    if(!assayId)return;
    void productionAdapter.loadAssay(assayId).then(data=>{
      if(token!==requestVersion.current)return;
      setSnapshot(data);setGroupId(data.groups[0]?.id??'');
    }).catch(()=>{if(token===requestVersion.current)setError('无法读取实验分组和四阶段资产。');});
    return ()=>{requestVersion.current++;};
  },[assayId]);
  const open=(scope:Editor['scope'],kind:Editor['kind'],action:Editor['action'],id?:string,parentId?:string,name='')=>{
    setError('');setEditor({scope,kind,action,id,parentId,name});
  };
  const submit=async()=>{
    if(!editor||busy)return;
    if(editor.action!=='delete'&&!editor.name.trim()){setError('名称不能为空');return;}
    const request=editor.request??{
      action:editor.action,kind:editor.kind,
      ...(editor.scope==='experiment'?{assayId}:{}),
      ...(editor.id?{id:editor.id}:{}),
      ...(editor.parentId?{parentId:editor.parentId}:{}),
      ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
      requestId:crypto.randomUUID()
    };
    setEditor({...editor,request});setBusy(true);setError('');
    try{
      if(editor.scope==='project'){
        await projectTreeApi(request as ProjectTreeRequest);
        const fresh=await refreshProjects();
        if(editor.action==='delete'&&editor.id===projectId&&!fresh.some(p=>p.id===projectId)){
          const first=fresh[0];setProjectId(first?.id??'');setThemeId(first?.themes[0]?.id??'');
          setAssayId(first?.themes[0]?.assays[0]?.id??'');
        }
      }else{
        await experimentApi(request as ExperimentRequest);
        await refreshAssay(assayId);
      }
      setEditor(null);
    }catch(reason){
      setError(editor.scope==='project'?safeProjectTreeError(reason).message:safeExperimentError(reason).message);
    }finally{setBusy(false);}
  };
  const chooseTheme=(id:string)=>{
    const t=project?.themes.find(x=>x.id===id);
    setThemeId(id);setAssayId(t?.assays[0]?.id??'');setSelectedIds([]);setDetail(null);
  };
  const chooseAssay=(id:string)=>{setAssayId(id);setSelectedIds([]);setDetail(null);};
  const chooseGroup=(id:string)=>{setGroupId(id);setSelectedIds([]);setDetail(null);setSearch('');};
  const toggle=(id:string,on:boolean)=>setSelectedIds(old=>on?[...new Set([...old,id])]:old.filter(x=>x!==id));
  const assetCell=(files:AssetRow[])=><div className="dm-real-asset-stack">
    {files.length===0?<span className="dm-muted">尚无文件</span>:files.map(f=>
      <button key={f.assetLinkId} type="button" className="dm-real-asset"
        onClick={()=>setDetail(f)} title={f.sha256}>
        <span>▣</span> <strong>{f.name}</strong><small>v{f.versionNumber}</small>
      </button>)}
  </div>;
  return <div className="dm-app dm-shadcn dm-production">
    <header className="dm-header">
      <div className="dm-logo"><span>BM</span><strong>科研实验与论文管理系统</strong></div>
      <div className="dm-history-controls" role="group" aria-label="目录导航历史">
        <button disabled title="导航历史将在后续阶段接入">←</button><button disabled>→</button>
      </div>
      <div className="dm-project-picker"><small>课题/论文项目</small>
        <UI.Select value={projectId} onChange={selectProject} ariaLabel="选择科研项目"
          options={projects.map(p=>({value:p.id,label:p.name}))}/></div>
      <nav className="dm-primary-nav" aria-label="核心模块">
        <UI.Button variant={module==='experiment'?'primary':'ghost'} onClick={()=>setModule('experiment')}>实验整理</UI.Button>
        <UI.Button variant={module==='paper'?'primary':'ghost'} onClick={()=>setModule('paper')}>论文管理</UI.Button>
      </nav><div className="dm-header-spacer"/>
      <UI.Button variant="secondary" onClick={()=>open('project','project','create')}>＋ 新建项目</UI.Button>
      <UI.Button disabled title="真实导入向导接入中">导入实验归档包</UI.Button>
    </header>
    <div className="dm-body">
      {module==='experiment'&&<aside className="dm-sidebar">
        <div className="dm-sidebar-title"><span>研究项目 / 实验导航</span>▣</div>
        <div className="dm-nav-tree">{project?.themes.map(t=><div key={t.id}>
          <button className={'dm-theme-row'+(themeId===t.id?' active':'')} onClick={()=>chooseTheme(t.id)}>
            <span>▣</span><span>{t.name}</span><span>⌄</span></button>
          {themeId===t.id&&t.assays.map(a=><button key={a.id}
            className={'dm-assay-row'+(assayId===a.id?' active':'')}
            onClick={()=>chooseAssay(a.id)}><span>♧</span><span>{a.name}</span></button>)}
        </div>)}</div>
        <div className="dm-real-sidebar-actions">
          <UI.Button small onClick={()=>open('project','theme','create',undefined,projectId)}
            disabled={!projectId}>＋ 主题</UI.Button>
          <UI.Button small onClick={()=>open('project','assay','create',undefined,themeId)}
            disabled={!themeId}>＋ 实验</UI.Button>
          {assay&&<UI.Button small onClick={()=>open('project','assay','rename',assayId,undefined,assay.name)}>重命名实验</UI.Button>}
        </div>
        <div className="dm-sidebar-footer">◈ <span>本地 SQLite / CAS · 真实数据</span></div>
      </aside>}
      <main className="dm-workspace">
      {module==='paper'?<div className="dm-empty">
        <h2>论文管理</h2><p>真实 Figure/Panel 卡片及固定版本溯源尚未完成接入；不会显示模拟论文。</p>
      </div>:!loaded?<div className="dm-empty">正在读取真实项目…</div>:!project?<div className="dm-empty">
        <h2>暂无科研项目</h2><p>使用顶部“新建项目”建立真实科研项目。</p>
      </div>:!assay?<div className="dm-empty">
        <h2>暂无实验</h2><p>从左侧新增主题和实验开始。</p>
      </div>:<>
        <div className="dm-workspace-head">
          <div className="dm-crumb">{project.name} › {theme?.name} › {assay.name}</div>
          <h1>{assay.name}</h1><p>真实实验 · {snapshot?.replicateCount??'…'} 个生物学重复</p>
          <div className="dm-group-tabs" role="group" aria-label="实验分组">
            {snapshot?.groups.map(g=><UI.Button key={g.id} small
              variant={groupId===g.id?'primary':'secondary'} onClick={()=>chooseGroup(g.id)}>
              {g.name} <span className="dm-group-count">{g.replicates.length}</span>
            </UI.Button>)}
            <UI.Button small variant="ghost" onClick={()=>open('experiment','group','create')}>＋ 新建分组</UI.Button>
            {group&&<UI.Button small variant="ghost"
              onClick={()=>open('experiment','replicate','create',undefined,groupId)}>＋ 新建样本</UI.Button>}
          </div>
        </div>
        <div className="dm-workspace-toolbar">
          <div className="dm-mode-switch">
            <UI.Button small variant={mode==='overview'?'primary':'ghost'} onClick={()=>setMode('overview')}>▦ 全阶段全景</UI.Button>
            <UI.Button small variant={mode==='stage'?'primary':'ghost'} onClick={()=>setMode('stage')}>☷ 分阶段</UI.Button>
          </div><div className="dm-toolbar-spacer"/>
          <UI.Input value={search} onChange={setSearch} placeholder="搜索样本文件…" ariaLabel="搜索样本文件"/>
        </div>
        <div className="dm-filter-toolbar">
          {mode==='overview'?<>
            <span className="dm-filter-label">展示阶段</span>
            {PRODUCTION_STAGES.map(s=><label key={s} className={'dm-stage-filter'+(stageFilter.includes(s)?' on':'')}>
              <input type="checkbox" checked={stageFilter.includes(s)} onChange={e=>setStageFilter(old=>
                e.target.checked?[...new Set([...old,s])]:old.filter(x=>x!==s))}/>{labels[s]}</label>)}
          </>:PRODUCTION_STAGES.map(s=><UI.Button key={s} small
            variant={stage===s?'primary':'ghost'} onClick={()=>{setStage(s);setSelectedIds([])}}>{labels[s]}</UI.Button>)}
          <div className="dm-toolbar-spacer"/>
          <div className="dm-view-switch">{(['grid','list','table'] as const).map(v=>
            <UI.Button key={v} small variant={view===v?'primary':'ghost'} onClick={()=>setView(v)}>{v==='grid'?'▦':v==='list'?'☷':'▤'}</UI.Button>)}</div>
        </div>
        <div className="dm-workspace-scroll dm-explorer-canvas" role="group" aria-label="文件资源管理器工作区" tabIndex={0}>
          <div className="dm-section-heading"><div><h2>{mode==='overview'?'全阶段归档总览':labels[stage]}
            <small>{mode==='overview'?'样本 × 数据阶段':'单阶段详细浏览'}</small></h2>
            <p>{group?.name??'选择分组'} · {rows.length} 个样本 · {snapshot?.groups.length??0} 个实验分组</p>
          </div><div className="dm-selection-actions">
            <UI.Badge tone="neutral">已选 {selectedIds.length} 个真实文件</UI.Badge>
            <UI.Button small variant="ghost" disabled title="复制移动交互将在后续阶段接入">复制/移动</UI.Button>
          </div></div>
          {!snapshot?<div className="dm-empty">正在读取真实文件…</div>:mode==='overview'?<>
            <div className="dm-matrix-scroll"><table className="dm-matrix"><thead><tr>
              <th>生物学重复</th>{PRODUCTION_STAGES.filter(s=>stageFilter.includes(s)).map(s=>
                <th key={s}>{labels[s]}<small>{s==='raw'||s==='processed'?'样本级':'实验级'}</small></th>)}
            </tr></thead><tbody>{rows.map(row=><tr key={row.id}>
              <td className="dm-sample-label"><strong>{row.name}</strong></td>
              {PRODUCTION_STAGES.filter(s=>stageFilter.includes(s)).map(s=><td key={s}>
                {s==='raw'||s==='processed'?assetCell(row[s]):
                  <div className="dm-asset-cell dm-compact">
                    <strong>实验级文件 {row[s].count} 项</strong>
                    <small>尚无可核验的本样本引用</small>
                    <button type="button" onClick={()=>{setMode('stage');setStage(s)}}>查看实验级文件</button>
                  </div>}
              </td>)}
            </tr>)}</tbody></table></div>
            {!rows.length&&<div className="dm-empty">当前分组暂无样本或没有匹配结果</div>}
          </>:<>
            <p className="dm-real-scope">{stage==='raw'||stage==='processed'?'当前分组内的样本级文件':'当前实验全部分组共享的实验级文件（不代表样本已经关联）'}</p>
            <div className={view==='grid'?'dm-real-file-grid':'dm-real-file-list'}>
              {assets.map(file=><div className="dm-content-card dm-real-file-card" key={file.assetLinkId}>
                <label><input type="checkbox" checked={selectedIds.includes(file.assetLinkId)}
                  onChange={e=>toggle(file.assetLinkId,e.target.checked)}/> 选择</label>
                <button className="dm-real-file-open" onClick={()=>setDetail(file)} type="button">
                  <span>▣</span><strong>{file.name}</strong></button>
                <small>v{file.versionNumber} · {file.sizeBytes} bytes · SHA-256 {file.sha256.slice(0,12)}…</small>
              </div>)}
            </div>
            {!assets.length&&<div className="dm-empty">当前阶段暂无文件</div>}
          </>}
          {snapshot?.incomplete&&<p role="alert" className="dm-real-warning">文件查询达到分页上限，当前矩阵并非全部资产；请勿将计数视为完整结果。</p>}
          <div className="dm-experiment-footnote">文件与数量来自真实后端。Statistics / Display 不自动推断样本级关联。</div>
        </div>
      </>}
      </main>
    </div>
    <footer className="dm-real-status" role="status">{busy?'正在提交并重新读取…':error||'正式工作区 · 真实存储'} · S12R 测试版本</footer>
    {editor&&<div className="dm-real-modal-backdrop"><section role="dialog" aria-modal="true" aria-label="编辑科研目录" className="dm-real-modal">
      <h2>{editor.action==='create'?'新建':editor.action==='rename'?'重命名':'删除'} {editor.kind}</h2>
      {editor.action==='delete'?<p>删除将由原生后端检查子节点与文件关联，不能撤销。</p>:
        <UI.Input value={editor.name} ariaLabel="名称" onChange={name=>setEditor({...editor,name,request:undefined})} placeholder="请输入名称"/>}
      {error&&<p role="alert">{error}</p>}
      <div className="dm-real-modal-actions"><UI.Button disabled={busy} onClick={()=>{setEditor(null);setError('')}}>取消</UI.Button>
        <UI.Button disabled={busy} variant={editor.action==='delete'?'danger':'primary'} onClick={()=>void submit()}>确认</UI.Button></div>
    </section></div>}
    {detail&&<aside className="dm-real-detail" role="dialog" aria-label="真实文件详情">
      <button type="button" onClick={()=>setDetail(null)}>关闭 ×</button>
      <h2>{detail.name}</h2><p>阶段：{labels[detail.stage]}</p><p>关联 ID：{detail.assetLinkId}</p>
      <p>固定版本 ID：{detail.assetVersionId}</p><p>版本：v{detail.versionNumber}</p>
      <p>大小：{detail.sizeBytes} bytes</p><p className="dm-real-hash">SHA-256：{detail.sha256}</p>
      <p>未显示模拟文件缩略图。</p>
    </aside>}
  </div>;
}
