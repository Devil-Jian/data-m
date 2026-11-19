import React,{useCallback,useEffect,useMemo,useRef,useState} from 'react';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import {VirtualAssetList,virtualAssetRects,WorkbenchHeader,ResearchNavigationTree,WorkbenchStatusBar,WorkbenchExplorerToolbar} from '@data-m/core';
import type {UIKit} from '@data-m/core';
import {projectTreeApi,safeProjectTreeError} from './project-tree';
import type {ProjectTreeKind,ProjectTreeRequest} from './project-tree';
import {experimentApi,safeExperimentError} from './experiment-tree';
import type {ExperimentKind,ExperimentRequest} from './experiment-tree';
import {productionAdapter,PRODUCTION_STAGES} from './production-adapter';
import type {ProductionAssaySnapshot,ProductionProject,ProductionStage} from './production-adapter';
import type {AssetRow} from './asset-browser';
import {useExplorerInteraction} from '@data-m/interaction';
import {lifecycleApi,lifecycleError,lifecycleRequest,currentLinks} from './asset-lifecycle';
import {safeTransferError,TransferError} from './asset-transfer';
import {captureProductionTransfer,validatedProductionTarget,commitProductionTransfer} from './production-transfer';
import type {ProductionTransferDraft} from './production-transfer';
import {ImportJobsPanel} from './import-jobs.tsx';
import {ProductionPaperWorkbench} from './production-paper';
import type {PaperPosition} from './production-paper';
import {displayEvidenceFromPapers,productionPaperApi} from './production-paper';
import {RepositorySafetyPanel} from './repository-safety.tsx';
import {ProductionAnalysisDrawer} from './production-analysis';
import {ProductionRecycleDrawer} from './production-recycle';
import type {AnalysisEvidence} from './production-analysis';
import {useProductionStageAssets} from './use-production-stage-assets';
import type {ImportDropRegistrar,ImportTarget} from './import-jobs.ts';
import './production-workbench.css';

const labels:Record<ProductionStage,string>={
  raw:'01_Raw',processed:'02_Processed',statistics:'03_Statistics',display:'04_Display'
};
type EditorBase={
  action:'create'|'rename'|'delete';
  id?:string;parentId?:string;name:string;
};
type ProjectEditor=EditorBase&{scope:'project';kind:ProjectTreeKind;request?:ProjectTreeRequest};
type ExperimentEditor=EditorBase&{scope:'experiment';kind:ExperimentKind;request?:ExperimentRequest};
type Editor=ProjectEditor|ExperimentEditor;
type View='grid'|'list'|'table';

/** A production entry point. No demo fixtures or simulated previews are imported. */
export function ProductionWorkbench({registerImportDrop,ui:UI=shadcnUI}:{registerImportDrop?:ImportDropRegistrar;ui?:UIKit}){
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
  const [module,setModule]=useState<'experiment'|'paper'>('experiment');
  const [paperPosition,setPaperPosition]=useState<PaperPosition|undefined>();
  const [paperReturn,setPaperReturn]=useState(false);
  const [paperExportSignal,setPaperExportSignal]=useState(0);
  const [editor,setEditor]=useState<Editor|null>(null);
  const [busy,setBusy]=useState(false);
  const [loaded,setLoaded]=useState(false);
  const [error,setError]=useState('');
  const [detail,setDetail]=useState<AssetRow|null>(null);
  const [importOpen,setImportOpen]=useState(false);
  const [safetyOpen,setSafetyOpen]=useState(false);
  const [recycleOpen,setRecycleOpen]=useState(false);
  const [analysisOpen,setAnalysisOpen]=useState(false);
  const [analysisEvidence,setAnalysisEvidence]=useState<AnalysisEvidence|null>(null);
  const [inclusionFilter,setInclusionFilter]=useState<'all'|'included'|'excluded'>('all');
  const [displayEvidence,setDisplayEvidence]=useState<Map<string,string[]>>(new Map());
  const [representativeFilter,setRepresentativeFilter]=useState<'all'|'representative'>('all');
  const [importLocked,setImportLocked]=useState(false);
  const [importReplicateId,setImportReplicateId]=useState('');
  const [transferDraft,setTransferDraft]=useState<ProductionTransferDraft|null>(null);
  const [transferStage,setTransferStage]=useState<ProductionStage>('raw');
  const [transferGroupId,setTransferGroupId]=useState('');
  const [transferReplicateId,setTransferReplicateId]=useState('');
  const requestVersion=useRef(0);
  const activeAssayRef=useRef(assayId);activeAssayRef.current=assayId;
  const project=projects.find(p=>p.id===projectId);
  const theme=project?.themes.find(t=>t.id===themeId);
  const assay=theme?.assays.find(a=>a.id===assayId);
  const group=snapshot?.groups.find(g=>g.id===groupId);
  const importReplicate=group?.replicates.find(r=>r.id===importReplicateId);
  const importTarget:ImportTarget|null=!assayId?null:
    stage==='raw'||stage==='processed'
      ?importReplicate?{assayId,stage,groupId,replicateId:importReplicate.id}:null
      :{assayId,stage};
  const importTargetLabel=`${assay?.name??''} / ${labels[stage]} / ${importReplicate?.label??'实验级'}`;
  const rows=useMemo(()=>snapshot?.rows.filter(r=>r.groupId===groupId&&
    (!search||r.name.toLowerCase().includes(search.toLowerCase())||
     [...r.raw,...r.processed].some(f=>f.name.toLowerCase().includes(search.toLowerCase())))&&
    (inclusionFilter==='all'||!analysisEvidence?.frozenAt?true:
      inclusionFilter==='included'?analysisEvidence.includedReplicateIds.includes(r.id):
      !analysisEvidence.includedReplicateIds.includes(r.id))&&
    (representativeFilter==='all'||displayEvidence.has(r.id)))??[],
    [snapshot,groupId,search,inclusionFilter,analysisEvidence,representativeFilter,displayEvidence]);
  const stageView=useProductionStageAssets({
    assayId,stage,
    ...(stage==='raw'||stage==='processed'?{groupId}:{}),
    search
  },module==='experiment'&&mode==='stage');
  const assets=useMemo(()=>{
    if(!snapshot)return [];
    if(mode==='stage')return stageView.items;
    const source=stage==='raw'||stage==='processed'
      ?rows.flatMap(row=>row[stage]):snapshot.assayFiles[stage];
    return source.filter(f=>!search||f.name.toLowerCase().includes(search.toLowerCase()));
  },[snapshot,mode,stage,rows,search,stageView.items]);
  const virtualAssets=useMemo(()=>assets.map(file=>({id:file.assetLinkId,filename:file.name,stage:file.stage,version:file.versionNumber,sha256:file.sha256})),[assets]);
  const visibleFiles=useMemo(()=>mode==='stage'?assets:rows.flatMap(r=>[...r.raw,...r.processed]),
    [mode,assets,rows]);
  const visibleIds=useMemo(()=>visibleFiles.map(f=>f.assetLinkId),[visibleFiles]);
  const explorer=useExplorerInteraction({
    visibleIds,enabled:module==='experiment'&&!editor&&!detail&&!busy&&!transferDraft,
    getVirtualItems:(host)=>virtualAssetRects(virtualAssets,host.clientWidth),
    onDrop:({itemIds,targetId,operation})=>beginTransfer(operation,itemIds,targetId),
    onCopy:ids=>beginTransfer('copy',ids),
    onCut:ids=>beginTransfer('move',ids),
    onPaste:(ids,operation)=>beginTransfer(operation,ids),
    onDelete:ids=>{void mutateAsset('soft_delete',ids);},
    onRename:id=>{void mutateAsset('rename',[id]);}
  });
  const selectedIds=explorer.selectedIds;
  const setSelectedIds=explorer.setSelectedIds;
  function beginTransfer(action:'copy'|'move',ids:string[],targetGroup?:string|null){
    if(busy||importLocked||!snapshot)return;
    try{
      const draft=captureProductionTransfer(assayId,visibleFiles,ids,action);
      setTransferDraft(draft);
      setTransferStage(mode==='stage'?stage:draft.items[0].stage);
      setTransferGroupId(targetGroup&&snapshot.groups.some(g=>g.id===targetGroup)?targetGroup:groupId);
      // The target Replicate must be chosen explicitly. A group is not a sample.
      setTransferReplicateId('');
      setError('');
    }catch(reason){setError(safeTransferError(reason).message);}
  }
  function changeTransferTarget(next:Partial<{stage:ProductionStage;groupId:string;replicateId:string}>){
    if(next.stage!==undefined)setTransferStage(next.stage);
    if(next.groupId!==undefined)setTransferGroupId(next.groupId);
    if(next.replicateId!==undefined)setTransferReplicateId(next.replicateId);
    setTransferDraft(old=>old?{...old,requestId:crypto.randomUUID()}:null);
    setError('');
  }
  async function submitTransfer(){
    if(!transferDraft||busy||!snapshot)return;
    const token=requestVersion.current;
    try{
      if(transferDraft.assayId!==assayId)throw new TransferError('STALE_SELECTION');
      if(transferStage==='raw'||transferStage==='processed'){
        const targetGroup=snapshot.groups.find(g=>g.id===transferGroupId);
        if(!targetGroup?.replicates.some(r=>r.id===transferReplicateId))
          throw new TransferError('INVALID_TARGET');
      }
      const target=validatedProductionTarget(
        assayId,transferStage,transferGroupId,transferReplicateId);
      setBusy(true);setError('');
      await commitProductionTransfer(transferDraft,target);
      if(token===requestVersion.current){
        await refreshAssay(assayId);
        stageView.refresh();
        setSelectedIds([]);setTransferDraft(null);
      }
    }catch(reason){
      setError(safeTransferError(reason).message+'；请检查最新资产状态后再重试。');
      if(token===requestVersion.current){
        try{await refreshAssay(assayId);stageView.refresh()}catch{/* retain actionable transfer error */}
      }
    }finally{setBusy(false);}
  }
  async function mutateAsset(action:'rename'|'soft_delete',ids:string[]){
    if(busy||ids.length===0||ids.length>100||!assayId)return;
    const scopeVersion=requestVersion.current;
    const rows=ids.map(id=>visibleFiles.find(f=>f.assetLinkId===id));
    if(rows.some(f=>!f)){setError('文件选择已过期，请刷新。');return;}
    const files=rows as AssetRow[];
    let newName:string|undefined;
    if(action==='rename'){
      if(files.length!==1)return;
      const input=window.prompt('新文件名（仅修改逻辑关联名称）',files[0].name);
      if(input===null||input===files[0].name)return;
      newName=input;
    }else if(!window.confirm('确认将选中的 '+files.length+' 个文件关联移入回收区？'))return;
    setBusy(true);setError('');
    try{
      const links=files.map(file=>currentLinks({assayId,stage:file.stage},[file])[0]);
      await lifecycleApi(lifecycleRequest(action,links,newName));
      if(scopeVersion===requestVersion.current){
        await refreshAssay(assayId);
        setSelectedIds([]);
      }
    }catch(reason){
      setError(lifecycleError(reason).message+' 请重新加载核验状态。');
      if(scopeVersion===requestVersion.current){
        try{await refreshAssay(assayId);stageView.refresh();}catch{/* preserve the primary operation error */}
      }
    }finally{setBusy(false);}
  }
  const selectProject=(id:string)=>{
    if(busy||importLocked||transferDraft)return;
    const next=projects.find(p=>p.id===id);
    const t=next?.themes[0],a=t?.assays[0];
    setProjectId(id);setThemeId(t?.id??'');setAssayId(a?.id??'');
    setGroupId('');setSnapshot(null);setSelectedIds([]);setDetail(null);setSearch('');
    setPaperPosition(undefined);setPaperReturn(false);
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
  useEffect(()=>{
    let active=true;
    setDisplayEvidence(new Map());setRepresentativeFilter('all');
    if(!projectId||!assayId||module!=='experiment')return;
    void productionPaperApi({action:'list',projectId}).then(data=>{
      if(active)setDisplayEvidence(displayEvidenceFromPapers(data.papers,assayId));
    }).catch(()=>{/* No Panel evidence is never permission to infer a link. */});
    return ()=>{active=false};
  },[projectId,assayId,module]);
  const refreshAssay=useCallback(async(id:string)=>{
    const next=await productionAdapter.loadAssay(id);
    if(activeAssayRef.current!==id)return;
    setSnapshot(next);
    setGroupId(old=>next.groups.some(g=>g.id===old)?old:next.groups[0]?.id??'');
  },[]);
  useEffect(()=>{
    const token=++requestVersion.current;
    setSnapshot(null);setGroupId('');setImportReplicateId('');setTransferDraft(null);setSelectedIds([]);
    setAnalysisOpen(false);setAnalysisEvidence(null);setInclusionFilter('all');
    setDisplayEvidence(new Map());setRepresentativeFilter('all');setDetail(null);setSearch('');
    if(!assayId)return;
    void productionAdapter.loadAssay(assayId).then(data=>{
      if(token!==requestVersion.current)return;
      setSnapshot(data);setGroupId(data.groups[0]?.id??'');
    }).catch(()=>{if(token===requestVersion.current)setError('无法读取实验分组和四阶段资产。');});
    return ()=>{requestVersion.current++;};
  },[assayId]);
  function open(scope:'project',kind:ProjectTreeKind,action:Editor['action'],id?:string,parentId?:string,name?:string):void;
  function open(scope:'experiment',kind:ExperimentKind,action:Editor['action'],id?:string,parentId?:string,name?:string):void;
  function open(scope:Editor['scope'],kind:Editor['kind'],action:Editor['action'],id?:string,parentId?:string,name=''){
    setError('');
    if(scope==='project')setEditor({scope,kind:kind as ProjectTreeKind,action,id,parentId,name});
    else setEditor({scope,kind:kind as ExperimentKind,action,id,parentId,name});
  }
  const submit=async()=>{
    if(!editor||busy)return;
    if(editor.action!=='delete'&&!editor.name.trim()){setError('名称不能为空');return;}
    setBusy(true);setError('');
    try{
      if(editor.scope==='project'){
        const request:ProjectTreeRequest=editor.request??{
          action:editor.action,kind:editor.kind,
          ...(editor.id?{id:editor.id}:{}),
          ...(editor.parentId?{parentId:editor.parentId}:{}),
          ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
          requestId:crypto.randomUUID()
        };
        setEditor({...editor,request});
        await projectTreeApi(request);
        const fresh=await refreshProjects();
        if(editor.action==='delete'&&editor.id===projectId&&!fresh.some(p=>p.id===projectId)){
          const first=fresh[0];setProjectId(first?.id??'');setThemeId(first?.themes[0]?.id??'');
          setAssayId(first?.themes[0]?.assays[0]?.id??'');
        }
      }else{
        if(!assayId)throw new Error('当前实验无效');
        const request:ExperimentRequest=editor.request??{
          action:editor.action,assayId,kind:editor.kind,
          ...(editor.id?{id:editor.id}:{}),
          ...(editor.parentId?{parentId:editor.parentId}:{}),
          ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
          requestId:crypto.randomUUID()
        };
        setEditor({...editor,request});
        await experimentApi(request);
        await refreshAssay(assayId);stageView.refresh();
      }
      setEditor(null);
    }catch(reason){
      setError(editor.scope==='project'?safeProjectTreeError(reason).message:safeExperimentError(reason).message);
    }finally{setBusy(false);}
  };
  const chooseTheme=(id:string)=>{
    if(busy||importLocked||transferDraft)return;
    const t=project?.themes.find(x=>x.id===id);
    setThemeId(id);setAssayId(t?.assays[0]?.id??'');setSelectedIds([]);setDetail(null);
  };
  const chooseAssay=(id:string)=>{if(busy||importLocked||transferDraft)return;setAssayId(id);setSelectedIds([]);setDetail(null);};
  const chooseGroup=(id:string)=>{if(busy||importLocked||transferDraft)return;setGroupId(id);setSelectedIds([]);setDetail(null);setSearch('');};
  const toggle=(id:string,on:boolean)=>explorer.toggle(id,on);
  useEffect(()=>{
    if(!group)return;
    setImportReplicateId(old=>group.replicates.some(r=>r.id===old)?old:'');
  },[groupId,snapshot]);
  const assetCell=(files:AssetRow[])=><div className="dm-real-asset-stack">
    {files.length===0?<span className="dm-muted">尚无文件</span>:files.map(f=>
      <button key={f.assetLinkId} type="button" data-explorer-id={f.assetLinkId} className={"dm-real-asset dm-selectable-asset"+(explorer.selectedSet.has(f.assetLinkId)?" selected":"") }
        onDoubleClick={()=>setDetail(f)} title={f.sha256+'（双击查看详情）'}>
        <span>▣</span> <strong>{f.name}</strong><small>v{f.versionNumber}</small>
      </button>)}
  </div>;
  return <div className="dm-app dm-shadcn dm-production">
    <WorkbenchHeader ui={UI} projectId={projectId}
      projectOptions={projects.map(p=>({value:p.id,label:p.name}))}
      module={module} onModuleChange={setModule} onProjectChange={selectProject}
      canBack={false} canForward={false} onBack={()=>{}} onForward={()=>{}}
      onNewProject={()=>open('project','project','create')}
      onImport={()=>{if(module==='paper')setPaperExportSignal(v=>v+1);
        else{setMode('stage');setImportOpen(true)}}}
      importDisabled={module==='paper'?!projectId:!assay||importLocked}/>
    {module==='experiment'&&paperReturn&&paperPosition?.panelId&&
      <div className="dm-trace-banner">
        <strong>版本溯源导航</strong>
        <span>从真实 Paper/Panel 固定版本定位到实验；当前项目上下文已保留。</span>
        <UI.Button small variant="secondary" onClick={()=>{setPaperReturn(false);setModule('paper')}}>返回原 Panel</UI.Button>
      </div>}
    <div className="dm-body">
      {module==='experiment'&&<ResearchNavigationTree
        themes={project?.themes??[]} themeId={themeId} assayId={assayId}
        onTheme={chooseTheme} onAssay={chooseAssay}
        footerText="本地 SQLite / CAS · 真实数据"
        actions={<>
        <div className="dm-real-sidebar-actions">
          <UI.Button small onClick={()=>open('project','theme','create',undefined,projectId)}
            disabled={!projectId}>＋ 主题</UI.Button>
          <UI.Button small onClick={()=>open('project','assay','create',undefined,themeId)}
            disabled={!themeId}>＋ 实验</UI.Button>
          {project&&<UI.Button small onClick={()=>open('project','project','rename',projectId,undefined,project.name)}>重命名项目</UI.Button>}
          {project&&<UI.Button small variant="ghost" onClick={()=>open('project','project','delete',projectId)}>删除项目</UI.Button>}
          {theme&&<UI.Button small onClick={()=>open('project','theme','rename',themeId,undefined,theme.name)}>重命名主题</UI.Button>}
          {theme&&<UI.Button small variant="ghost" onClick={()=>open('project','theme','delete',themeId)}>删除主题</UI.Button>}
          {assay&&<UI.Button small onClick={()=>open('project','assay','rename',assayId,undefined,assay.name)}>重命名实验</UI.Button>}
          {assay&&<UI.Button small variant="ghost" onClick={()=>open('project','assay','delete',assayId)}>删除实验</UI.Button>}
        </div>

        </>}/>}
      <main className="dm-workspace">
      {module==='paper'?<ProductionPaperWorkbench projectId={projectId}
        exportSignal={paperExportSignal} initialPosition={paperPosition} onPositionChange={setPaperPosition}
        onJumpToAssay={id=>{
          const owner=project?.themes.find(t=>t.assays.some(a=>a.id===id));
          if(!owner){setError('目标实验不在当前科研项目中，已阻止跨项目跳转。');return;}
          setPaperReturn(true);setThemeId(owner.id);setAssayId(id);setModule('experiment');
        }}/>:!loaded?<div className="dm-empty">正在读取真实项目…</div>:!project?<div className="dm-empty">
        <h2>暂无科研项目</h2><p>使用顶部“新建项目”建立真实科研项目。</p>
      </div>:!assay?<div className="dm-empty">
        <h2>暂无实验</h2><p>从左侧新增主题和实验开始。</p>
      </div>:<>
        <div className="dm-workspace-head">
          <div className="dm-crumb">{project.name} › {theme?.name} › {assay.name}</div>
          <h1>{assay.name}</h1><p>真实实验 · {snapshot?.replicateCount??'…'} 个生物学重复</p>
          <div className="dm-group-tabs" role="group" aria-label="实验分组">
            {snapshot?.groups.map(g=><span key={g.id} data-drop-container-id={g.id}
              className={'dm-drop-target'+(explorer.gesture.kind==='drag'&&
                explorer.gesture.targetId===g.id&&explorer.gesture.allowed?' dm-drop-allowed':'')}>
              <UI.Button small variant={groupId===g.id?'primary':'secondary'}
                onClick={()=>chooseGroup(g.id)}>
                {g.name} <span className="dm-group-count">{g.replicates.length}</span>
              </UI.Button>
            </span>)}
            <UI.Button small variant="ghost" onClick={()=>open('experiment','group','create')}>＋ 新建分组</UI.Button>
            {group&&<UI.Button small variant="ghost"
              onClick={()=>open('experiment','replicate','create',undefined,groupId)}>＋ 新建样本</UI.Button>}
            {group&&<UI.Button small variant="ghost"
              onClick={()=>open('experiment','group','rename',groupId,undefined,group.name)}>重命名当前分组</UI.Button>}
            {group&&<UI.Button small variant="ghost"
              onClick={()=>open('experiment','group','delete',groupId)}>删除当前分组</UI.Button>}
          </div>
        </div>
        <WorkbenchExplorerToolbar ui={UI} mode={mode} stage={stage}
          stages={PRODUCTION_STAGES.map(id=>({id,label:labels[id]}))}
          stageFilter={stageFilter} view={view} search={search}
          onMode={setMode} onStage={id=>{setStage(id);setSelectedIds([])}}
          onStageFilter={setStageFilter} onView={setView} onSearch={setSearch}
          searchExtras={<>
            <UI.Button small variant="secondary" disabled={!assayId||busy}
              onClick={()=>setAnalysisOpen(true)}>
              统计分析{analysisEvidence?.frozenAt?` · 已冻结 n=${analysisEvidence.includedN}`:''}
            </UI.Button>
            <UI.Button small variant="ghost" disabled={!assayId||busy}
              onClick={()=>setRecycleOpen(true)}>回收区</UI.Button>
          </>}
          filterExtras={mode==='overview'?<>
            {analysisEvidence?.frozenAt&&<UI.Select ariaLabel="按真实冻结分析筛选"
              value={inclusionFilter} onChange={v=>setInclusionFilter(v as typeof inclusionFilter)}
              options={[{value:'all',label:'全部生物学重复'},
                {value:'included',label:'本分析已纳入'},
                {value:'excluded',label:'本分析未纳入'}]}/>}
            <UI.Select ariaLabel="按固定版本代表来源筛选" value={representativeFilter}
              onChange={v=>setRepresentativeFilter(v as typeof representativeFilter)}
              options={[{value:'all',label:'全部样本'},
                {value:'representative',label:'仅有 Panel 固定来源'}]}/>
          </>:null}/>
        <div className="dm-workspace-scroll dm-explorer-canvas" data-source-container-id={groupId} role="group" aria-label="文件资源管理器工作区" tabIndex={0}
          onFocusCapture={explorer.onFocusCapture} onPointerDown={explorer.onPointerDown}
          onContextMenu={explorer.onContextMenu} onDragStart={e=>e.preventDefault()}>
          <div className="dm-section-heading"><div><h2>{mode==='overview'?'全阶段归档总览':labels[stage]}
            <small>{mode==='overview'?'样本 × 数据阶段':'单阶段详细浏览'}</small></h2>
            <p>{group?.name??'选择分组'} · {rows.length} 个样本 · {snapshot?.groups.length??0} 个实验分组</p>
          </div><div className="dm-selection-actions">
            <UI.Badge tone="neutral">已选 {selectedIds.length} 个真实文件</UI.Badge>
            <UI.Button small variant="ghost" disabled={!selectedIds.length||busy} onClick={()=>beginTransfer('copy',selectedIds)}>复制</UI.Button>
            <UI.Button small variant="ghost" disabled={!selectedIds.length||busy} onClick={()=>beginTransfer('move',selectedIds)}>移动</UI.Button>
          </div></div>
          {!snapshot?<div className="dm-empty">正在读取真实文件…</div>:mode==='overview'?<>
            {view==='list'?<div className="dm-real-sample-list">
              {rows.map(row=><article className="dm-content-card dm-real-sample-list-card" key={row.id}>
                <h3>{row.name}</h3>
                <div className="dm-real-sample-actions">
                  <button type="button" onClick={()=>open('experiment','replicate','rename',row.id,undefined,row.name)}>重命名样本</button>
                  <button type="button" onClick={()=>open('experiment','replicate','delete',row.id)}>删除样本</button>
                </div>
                <div className="dm-real-sample-list-stages">
                  {PRODUCTION_STAGES.filter(stageId=>stageFilter.includes(stageId)).map(stageId=>
                    <section key={stageId}>
                      <strong>{labels[stageId]}</strong>
                      {stageId==='raw'||stageId==='processed'?assetCell(row[stageId]):<>
                        <small>真实实验级文件 {row[stageId].count} 项</small>
                        <small>{stageId==='statistics'&&analysisEvidence?.frozenAt?
                          (analysisEvidence.includedReplicateIds.includes(row.id)?'本冻结分析已纳入':'本冻结分析未纳入'):
                          stageId==='display'&&displayEvidence.has(row.id)?
                            displayEvidence.get(row.id)!.join('、'):'本样本暂无固定引用证据'}</small>
                      </>}
                    </section>)}
                </div>
              </article>)}
            </div>:<div className={view==='grid'?'dm-matrix-scroll':'dm-table-outer'}>
              <table className={view==='grid'?'dm-matrix':'dm-data-table'}><thead><tr>
              <th>生物学重复</th>{PRODUCTION_STAGES.filter(s=>stageFilter.includes(s)).map(s=>
                <th key={s}>{labels[s]}<small>{s==='raw'||s==='processed'?'样本级':'实验级'}</small></th>)}
            </tr></thead><tbody>{rows.map(row=><tr key={row.id}>
              <td className="dm-sample-label"><strong>{row.name}</strong>
                <div className="dm-real-sample-actions">
                  <button type="button" aria-label={`重命名样本 ${row.name}`}
                    onClick={()=>open('experiment','replicate','rename',row.id,undefined,row.name)}>重命名</button>
                  <button type="button" aria-label={`删除样本 ${row.name}`}
                    onClick={()=>open('experiment','replicate','delete',row.id)}>删除</button>
                </div></td>
              {PRODUCTION_STAGES.filter(s=>stageFilter.includes(s)).map(s=><td key={s}>
                {s==='raw'||s==='processed'?assetCell(row[s]):
                  <div className="dm-asset-cell dm-compact">
                    <strong>实验级文件 {row[s].count} 项</strong>
                    {s==='statistics'&&analysisEvidence?.frozenAt?
                      <small>{analysisEvidence.includedReplicateIds.includes(row.id)
                        ?`冻结分析 ${analysisEvidence.analysisId}：已纳入`:'当前冻结分析未纳入本样本'}</small>:
                    s==='display'&&displayEvidence.has(row.id)?
                      <small>固定版本 Panel 来源：{displayEvidence.get(row.id)!.join('、')}</small>:
                      <small>尚无可核验的本样本引用</small>}
                    <button type="button" onClick={()=>{setMode('stage');setStage(s)}}>查看实验级文件</button>
                  </div>}
              </td>)}
            </tr>)}</tbody></table></div>}
            {!rows.length&&<div className="dm-empty">当前分组暂无样本或没有匹配结果</div>}
          </>:<>
            <p className="dm-real-scope">{stage==='raw'||stage==='processed'?'当前分组内的样本级文件':'当前实验全部分组共享的实验级文件（不代表样本已经关联）'}</p>
            {(view==='list'||assets.length>250)?<VirtualAssetList assets={virtualAssets} selected={explorer.selectedSet}
              focusedId={explorer.focusedId} onToggle={toggle}
              renderName={item=>item.filename}
              renderSubtitle={item=>`v${item.version} · SHA-256 ${item.sha256.slice(0,12)}… · 真实文件关联`}
              renderType={item=>labels[item.stage as ProductionStage]}/>:<div className={view==='grid'?'dm-real-file-grid':'dm-real-file-list'}>
              {assets.map(file=><div data-explorer-id={file.assetLinkId} className={"dm-content-card dm-real-file-card dm-selectable-asset"+(explorer.selectedSet.has(file.assetLinkId)?" selected":"")} key={file.assetLinkId}>
                <label><input type="checkbox" checked={selectedIds.includes(file.assetLinkId)}
                  onChange={e=>toggle(file.assetLinkId,e.target.checked)}/> 选择</label>
                <button data-explorer-no-gesture className="dm-real-file-open" onClick={()=>setDetail(file)} type="button">
                  <span>▣</span><strong>{file.name}</strong></button>
                <small>v{file.versionNumber} · {file.sizeBytes} bytes · SHA-256 {file.sha256.slice(0,12)}…</small>
              </div>)}
            </div>}
            <div className="dm-real-page-footer" role="status">
              已读取 {stageView.items.length} / {stageView.total} 个符合条件的真实文件
              {stageView.loading?' · 正在读取下一页…':''}
              {stageView.hasMore&&<UI.Button small disabled={stageView.loading} onClick={stageView.loadMore}>加载后续 100 个文件</UI.Button>}
              {stageView.error&&<p role="alert">{stageView.error}</p>}
            </div>
            {!stageView.loading&&!assets.length&&<div className="dm-empty">当前阶段暂无文件</div>}
          </>}
          {explorer.contextMenu&&<div className="dm-explorer-context-menu" data-explorer-context-menu role="menu"
            style={{position:"fixed",left:Math.min(explorer.contextMenu.x,window.innerWidth-230),top:Math.min(explorer.contextMenu.y,window.innerHeight-270)}}>
            <div className="dm-context-caption">已选择 {explorer.contextMenu.ids.length} 个文件关联</div>
            <button type="button" role="menuitem" onClick={()=>explorer.contextAction("copy")}>复制 Ctrl+C</button>
            <button type="button" role="menuitem" onClick={()=>explorer.contextAction("cut")}>剪切 Ctrl+X</button>
            <button type="button" role="menuitem" disabled={explorer.contextMenu.ids.length!==1} onClick={()=>explorer.contextAction("rename")}>重命名 F2</button>
            <button type="button" role="menuitem" onClick={()=>explorer.contextAction("delete")}>移入回收区 Delete</button>
            <button type="button" role="menuitem" disabled={!explorer.hasClipboard} onClick={()=>explorer.contextAction("paste")}>粘贴 Ctrl+V</button>
          </div>}
          {snapshot?.incomplete&&<p role="alert" className="dm-real-warning">文件查询达到分页上限，当前矩阵并非全部资产；请勿将计数视为完整结果。</p>}
          <div className="dm-experiment-footnote">文件与数量来自真实后端。Statistics / Display 不自动推断样本级关联。</div>
        </div>
      </>}
      </main>
    </div>
    {module==='experiment'&&explorer.gesture.kind==='marquee'&&<div className="dm-marquee" aria-hidden
      style={{left:explorer.gesture.rect.left,top:explorer.gesture.rect.top,
        width:explorer.gesture.rect.right-explorer.gesture.rect.left,
        height:explorer.gesture.rect.bottom-explorer.gesture.rect.top}}/>}
    {module==='experiment'&&explorer.gesture.kind==='drag'&&<div
      className={'dm-drag-preview'+(explorer.gesture.allowed?' is-valid':' is-invalid')}
      aria-hidden style={{left:explorer.gesture.x+14,top:explorer.gesture.y+14}}>
      {explorer.gesture.operation==='copy'?'＋ 复制':'↗ 移动'} {explorer.gesture.count} 项 ·
      {explorer.gesture.allowed?'松开后选择目标样本并确认':'目标不可放置'}
    </div>}
    <div className="dm-real-tool-launcher" role="group" aria-label="仓库工具">
      <UI.Button small variant="ghost" onClick={()=>setSafetyOpen(true)} disabled={busy||importLocked}>完整性 · 备份 · 隔离恢复</UI.Button>
      <span>本机快照不等于异地备份；操作仅限可丢弃测试库。</span>
    </div>
    <WorkbenchStatusBar
      status="Data-M · 本地真实科研数据 · S12R 未签收测试版"
      summary={module==='experiment'?`${snapshot?.replicateCount??0} 个生物学重复 · 当前已选 ${selectedIds.length} 个文件`:'论文数据来自 SQLite · 固定版本引用'}
      hint={busy?'正在提交后端并重新查询…':error||'Ctrl/Shift 多选 · 右键 · F2 · Delete · 拖放确认'}/>
    {editor&&<div className="dm-real-modal-backdrop"><section role="dialog" aria-modal="true" aria-label="编辑科研目录" className="dm-real-modal">
      <h2>{editor.action==='create'?'新建':editor.action==='rename'?'重命名':'删除'} {editor.kind}</h2>
      {editor.action==='delete'?<p>删除将由原生后端检查子节点与文件关联，不能撤销。</p>:
        <UI.Input value={editor.name} ariaLabel="名称" onChange={name=>setEditor({...editor,name,request:undefined})} placeholder="请输入名称"/>}
      {error&&<p role="alert">{error}</p>}
      <div className="dm-real-modal-actions"><UI.Button disabled={busy} onClick={()=>{setEditor(null);setError('')}}>取消</UI.Button>
        <UI.Button disabled={busy} variant={editor.action==='delete'?'danger':'primary'} onClick={()=>void submit()}>确认</UI.Button></div>
    </section></div>}
    {recycleOpen&&assay&&<ProductionRecycleDrawer assayId={assayId} groupId={groupId}
      onRecovered={()=>{stageView.refresh();void refreshAssay(assayId)}}
      onClose={()=>setRecycleOpen(false)}/>}
    {analysisOpen&&assay&&<ProductionAnalysisDrawer assayId={assayId} assayName={assay.name}
      onEvidence={setAnalysisEvidence} onClose={()=>setAnalysisOpen(false)}/>}
    {safetyOpen&&<div className="dm-real-modal-backdrop dm-real-safety-overlay">
      <section role="dialog" aria-modal="true" aria-label="仓库数据安全工具"
        className="dm-real-safety-modal">
        <RepositorySafetyPanel onClose={()=>setSafetyOpen(false)} closeLabel="关闭安全工具"/>
      </section>
    </div>}
    {transferDraft&&<div className="dm-real-modal-backdrop">
      <section role="dialog" aria-modal="true" aria-label="真实文件复制移动确认" className="dm-real-modal dm-real-transfer-modal">
        <h2>{transferDraft.action==='copy'?'复制':'移动'} {transferDraft.items.length} 条资产关联</h2>
        <p>保留真实 AssetVersionId；不会新增生物学重复，也不会修改原始 CAS 文件。</p>
        <label>目标阶段
          <UI.Select ariaLabel="复制移动目标阶段" value={transferStage}
            onChange={v=>changeTransferTarget({stage:v as ProductionStage,replicateId:''})}
            options={PRODUCTION_STAGES.map(id=>({value:id,label:labels[id]}))}/>
        </label>
        {(transferStage==='raw'||transferStage==='processed')&&<>
          <label>目标分组
            <UI.Select ariaLabel="复制移动目标分组" value={transferGroupId}
              onChange={id=>changeTransferTarget({groupId:id,replicateId:''})}
              options={(snapshot?.groups??[]).map(g=>({value:g.id,label:g.name}))}/>
          </label>
          <label>目标样本（必须明确选择）
            <UI.Select ariaLabel="复制移动目标样本" value={transferReplicateId||'__choose__'}
              onChange={id=>changeTransferTarget({replicateId:id==='__choose__'?'':id})}
              options={[{value:'__choose__',label:'请选择目标样本'},...(snapshot?.groups.find(g=>g.id===transferGroupId)?.replicates??[]).map(r=>({value:r.id,label:r.label}))]}/>
          </label>
        </>}
        {error&&<p role="alert">{error}</p>}
        <div className="dm-real-modal-actions">
          <UI.Button disabled={busy} onClick={()=>{setTransferDraft(null);setError('')}}>取消</UI.Button>
          <UI.Button variant="primary" disabled={busy||
            ((transferStage==='raw'||transferStage==='processed')&&!transferReplicateId)}
            onClick={()=>void submitTransfer()}>确认{transferDraft.action==='copy'?'复制':'移动'}</UI.Button>
        </div>
      </section>
    </div>}
    {importOpen&&assay&&<div className="dm-real-modal-backdrop dm-real-import-overlay">
      <section className="dm-real-import-modal" role="dialog" aria-modal="true" aria-label="受控安全导入">
        <div className="dm-real-import-target"><strong>选择导入目标</strong>
          <UI.Select ariaLabel="导入数据阶段" value={stage} onChange={v=>{if(!importLocked)setStage(v as ProductionStage)}}
            options={PRODUCTION_STAGES.map(s=>({value:s,label:labels[s]}))}/>
          {(stage==='raw'||stage==='processed')&&<>
            <UI.Select ariaLabel="导入目标分组" value={groupId}
              onChange={id=>chooseGroup(id)} options={(snapshot?.groups??[]).map(g=>({value:g.id,label:g.name}))}/>
            <UI.Select ariaLabel="导入目标样本" value={importReplicateId||'__none__'}
              onChange={v=>{if(!importLocked)setImportReplicateId(v==='__none__'?'':v)}}
              options={[{value:'__none__',label:'请明确选择目标样本'},
                ...(group?.replicates??[]).map(rep=>({value:rep.id,label:rep.label}))]}/>
          </>}
          <p>仅支持已解压的真实文件或文件夹；ZIP 不能直接解包。系统不会按文件名自动创建科研关联。</p>
        </div>
        <ImportJobsPanel assayId={assayId} target={importTarget} targetLabel={importTargetLabel}
          onLockChange={setImportLocked} registerImportDrop={registerImportDrop}
          onClose={()=>{setImportOpen(false);stageView.refresh();void refreshAssay(assayId).catch(()=>setError('导入后重新读取资产失败，请手动检查'));}}/>
      </section>
    </div>}
    {detail&&<aside className="dm-real-detail" role="dialog" aria-label="真实文件详情">
      <button type="button" onClick={()=>setDetail(null)}>关闭 ×</button>
      <h2>{detail.name}</h2><p>阶段：{labels[detail.stage]}</p><p>关联 ID：{detail.assetLinkId}</p>
      <p>固定版本 ID：{detail.assetVersionId}</p><p>版本：v{detail.versionNumber}</p>
      <p>大小：{detail.sizeBytes} bytes</p><p className="dm-real-hash">SHA-256：{detail.sha256}</p>
      <p>未显示模拟文件缩略图。</p>
    </aside>}
  </div>;
}
