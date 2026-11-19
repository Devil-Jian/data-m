import React,{useCallback,useEffect,useRef,useState} from 'react';
import {isTauri,invoke} from '@tauri-apps/api/core';
import {ProvenanceView,WorkbenchFigureList} from '@data-m/core';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import {productionPaperApi} from './production-paper';
import type {NativePanel,PaperCandidate,PaperRequest,PaperSnapshot} from './production-paper';
import './production-paper.css';

type PaperView='grid'|'list'|'table';
type Editor={action:'create'|'rename'|'delete';kind:'paper'|'figure'|'panel';id?:string;parentId?:string;name:string;requestId:string};
const blank:PaperSnapshot={papers:[],candidates:[]};

/** S9/S10 native data in the same Figure/Panel card-and-drawer hierarchy as Demo. */
export interface PaperPosition{paperId:string;panelId:string|null}
export function ProductionPaperWorkbench({
  projectId,onJumpToAssay,initialPosition,onPositionChange
}:{projectId:string;onJumpToAssay:(assayId:string)=>void;
  initialPosition?:PaperPosition;onPositionChange?:(value:PaperPosition)=>void;
}){
  const [snapshot,setSnapshot]=useState<PaperSnapshot>(blank);
  const [loaded,setLoaded]=useState(false);
  const [paperId,setPaperId]=useState(initialPosition?.paperId??'');
  const [panelId,setPanelId]=useState<string|null>(initialPosition?.panelId??null);
  const [view,setView]=useState<PaperView>('grid');
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [notice,setNotice]=useState('');
  const [editor,setEditor]=useState<Editor|null>(null);
  const [candidates,setCandidates]=useState<PaperCandidate[]>([]);
  const [candidateTotal,setCandidateTotal]=useState<number|undefined>();
  const [candidateTruncated,setCandidateTruncated]=useState(false);
  const [candidateOffset,setCandidateOffset]=useState(0);
  const [candidateSearch,setCandidateSearch]=useState('');
  const [selectedVersions,setSelectedVersions]=useState<string[]>([]);
  const [usage,setUsage]=useState('display');
  const [paperStatus,setPaperStatus]=useState('draft');
  const [paperNotes,setPaperNotes]=useState('');
  const [exportFigureId,setExportFigureId]=useState('');
  const [exportPreview,setExportPreview]=useState<{figureId:string;panelCount:number;referenceCount:number;uniqueFileCount:number;uniqueBytes:number}|null>(null);
  const [exportRequestId,setExportRequestId]=useState<string|null>(null);
  const [exportStatus,setExportStatus]=useState('');
  const [exportBusy,setExportBusy]=useState(false);
  const serial=useRef(0);

  const paper=snapshot.papers.find(p=>p.id===paperId)??snapshot.papers[0];
  const panel=paper?.figures.flatMap(f=>f.panels).find(p=>p.id===panelId);
  const figure=paper?.figures.find(f=>f.id===exportFigureId)??paper?.figures[0];
  const panelOwner=paper?.figures.find(f=>f.panels.some(p=>p.id===panelId));
  useEffect(()=>{setPaperStatus(paper?.status??'draft');setPaperNotes(paper?.notes??'')},
    [paper?.id,paper?.status,paper?.notes]);

  const reload=useCallback(async(id:string)=>{
    const request=++serial.current;
    const result=await productionPaperApi({action:'list',projectId:id});
    if(request===serial.current){
      setSnapshot(result);setLoaded(true);
      setPaperId(old=>result.papers.some(p=>p.id===old)?old:result.papers[0]?.id??'');
    }
  },[]);
  useEffect(()=>{
    setLoaded(false);setSnapshot(blank);
    setPaperId(initialPosition?.paperId??'');setPanelId(initialPosition?.panelId??null);setError('');
    void reload(projectId).catch(e=>{setLoaded(true);setError(e instanceof Error?e.message:'论文查询失败')});
    return ()=>{serial.current++;};
  },[projectId,reload]);
  useEffect(()=>{
    if(paperId)onPositionChange?.({paperId,panelId});
  },[paperId,panelId,onPositionChange]);
  useEffect(()=>{
    setCandidates([]);setSelectedVersions([]);
    if(!projectId||!panelId)return;
    let active=true;
    void productionPaperApi({action:'candidates',projectId,candidateOffset,
      candidateLimit:100,candidateQuery:candidateSearch})
      .then(data=>{if(active){setCandidates(data.candidates);setCandidateTotal(data.candidateTotalCount);
        setCandidateTruncated(Boolean(data.candidateTruncated));}})
      .catch(()=>{if(active)setError('候选固定版本查询失败，请刷新。')});
    return ()=>{active=false;};
  },[projectId,panelId,candidateOffset,candidateSearch]);

  async function mutate(request:Omit<PaperRequest,'projectId'|'requestId'>,requestId=crypto.randomUUID()){
    if(busy||!projectId)return;
    setBusy(true);setError('');setNotice('');
    try{
      await productionPaperApi({...request,projectId,requestId});
      await reload(projectId);
      setNotice('已从真实数据库重新读取变更。');
      setEditor(null);setSelectedVersions([]);
    }catch(e){setError(e instanceof Error?e.message:'论文操作未确认');}
    finally{setBusy(false);}
  }
  function begin(kind:Editor['kind'],parentId?:string,action:Editor['action']='create',id?:string,name=''){
    setEditor({action,kind,id,parentId,name,requestId:crypto.randomUUID()});setError('');
  }
  function reorder(kind:'figure'|'panel',parentId:string,ids:string[],index:number,direction:-1|1){
    const target=index+direction;
    if(target<0||target>=ids.length)return;
    const next=[...ids];[next[index],next[target]]=[next[target],next[index]];
    void mutate({action:'reorder',kind,parentId,orderIds:next});
  }
  function openPanel(selected:NativePanel,figureId:string){
    setPanelId(selected.id);setExportFigureId(figureId);
    setCandidateOffset(0);setCandidateSearch('');setSelectedVersions([]);
  }
  async function previewExport(figureId:string){
    if(!paper||!figureId||!isTauri())return;
    setExportStatus('');setExportPreview(null);
    try{
      const result=await invoke<{status:string;preview:typeof exportPreview}>('figure_export',{
        request:{action:'preview',projectId,paperId:paper.id,figureId}
      });
      if(result.status!=='preview'||!result.preview||result.preview.figureId!==figureId)
        throw Error('导出范围无法核验');
      setExportPreview(result.preview);
    }catch{setExportStatus('预览失败；未导出文件。');}
  }
  async function exportPinnedFigure(){
    if(!paper||!figure||!exportPreview||exportPreview.figureId!==figure.id||!isTauri())return;
    if(!window.confirm('确认从固定版本 CAS 导出 '+exportPreview.uniqueFileCount+' 个唯一文件，并校验 SHA-256？'))return;
    const requestId=crypto.randomUUID();
    setExportRequestId(requestId);setExportBusy(true);setExportStatus('正在原生导出，最终以 SHA manifest 为准。');
    try{
      const result=await invoke<{status:string;manifest:{status:string;referenceCount:number;physicalFileCount:number}|null}>('figure_export',{
        request:{action:'export',projectId,paperId:paper.id,figureId:figure.id,confirm:true,requestId}
      });
      if(result.status!=='complete'||result.manifest?.status!=='complete')throw Error('导出尚未核验完成');
      setExportStatus('导出已校验：'+result.manifest.physicalFileCount+' 个文件 · '+result.manifest.referenceCount+' 条版本引用。');
    }catch{setExportStatus('原生导出失败或取消；未确认任何完整发布包。');}
    finally{setExportBusy(false);setExportRequestId(null);}
  }
  async function cancelExport(){
    if(!exportRequestId)return;
    try{const ok=await invoke<boolean>('figure_export_cancel',{requestId:exportRequestId});
      if(ok)setExportStatus('已请求取消，等待原生任务安全结束。');
    }catch{setExportStatus('取消确认失败，请检查任务结果。');}
  }
  return <section className="dm-production-paper" aria-label="论文 Figure 与 Panel 工作区">
    <div className="dm-workspace-head">
      <div className="dm-crumb">真实科研项目 › 论文管理</div>
      <h1>论文管理</h1><p>Paper / Figure / Panel · 固定 AssetVersionId</p>
      <div className="dm-paper-tabs">
        {snapshot.papers.map(p=><UI.Button key={p.id} small
          variant={paper?.id===p.id?'primary':'secondary'}
          onClick={()=>{setPaperId(p.id);setPanelId(null);setExportPreview(null);}}>
          {p.title}</UI.Button>)}
        <UI.Button small variant="ghost" onClick={()=>begin('paper')}>＋ 新建论文</UI.Button>
      </div>
    </div>
    {!loaded&&<div className="dm-empty">正在读取真实论文记录…</div>}
    {error&&<p role="alert" className="dm-paper-error">{error}</p>}
    {notice&&<p role="status" className="dm-paper-notice">{notice}</p>}
    {loaded&&!paper&&<div className="dm-empty">当前项目暂无论文。新建论文后添加 Figure 和 Panel。</div>}
    {paper&&<div className="dm-paper-content">
      <div className="dm-filter-toolbar">
        <strong>{paper.title}</strong><span>{paper.status}</span>
        <div className="dm-toolbar-spacer"/>
        <UI.Button small onClick={()=>begin('figure',paper.id)}>＋ 新建 Figure</UI.Button>
        <UI.Button small variant="ghost" onClick={()=>begin('paper',undefined,'rename',paper.id,paper.title)}>修改论文标题</UI.Button>
        <UI.Button small variant="ghost" disabled={paper.figures.length>0} onClick={()=>{
          if(window.confirm('确认归档当前空论文？'))void mutate({action:'delete',kind:'paper',id:paper.id});
        }}>归档空论文</UI.Button>
        {(['grid','list','table'] as const).map(v=>
          <UI.Button key={v} small variant={view===v?'primary':'ghost'} onClick={()=>setView(v)}>
            {v==='grid'?'网格':v==='list'?'列表':'表格'}</UI.Button>)}
      </div>
      <WorkbenchFigureList view={view}
        figures={paper.figures.map(figure=>({
          id:figure.id,label:figure.label,caption:`${figure.panels.length} 个真实 Panel`,
          panels:figure.panels.map(panel=>({
            id:panel.id,label:panel.label,title:panel.label,
            subtitle:`固定引用 ${panel.assets.length} · 代表来源 ${panel.representatives.length}`,
            preview:<div className="dm-real-panel-no-preview">
              <span>▣</span><small>真实文件固定版本 · 不显示示意图</small>
            </div>
          }))
        }))}
        onPanelSelect={id=>{
          const owner=paper.figures.find(f=>f.panels.some(panel=>panel.id===id));
          const panel=owner?.panels.find(p=>p.id===id);
          if(owner&&panel)openPanel(panel,owner.id);
        }}
        figureActions={f=><div className="dm-real-figure-actions">
          <UI.Button small variant="secondary" onClick={()=>begin('panel',f.id)}>＋ Panel</UI.Button>
          <UI.Button small variant="ghost" onClick={()=>begin('figure',paper.id,'rename',f.id,f.label)}>重命名</UI.Button>
          <UI.Button small variant="ghost" disabled={paper.figures.findIndex(x=>x.id===f.id)===0}
            onClick={()=>reorder('figure',paper.id,paper.figures.map(x=>x.id),paper.figures.findIndex(x=>x.id===f.id),-1)}>上移</UI.Button>
          <UI.Button small variant="ghost" disabled={!paper.figures.some((x,i)=>i>paper.figures.findIndex(y=>y.id===f.id))}
            onClick={()=>reorder('figure',paper.id,paper.figures.map(x=>x.id),paper.figures.findIndex(x=>x.id===f.id),1)}>下移</UI.Button>
          <UI.Button small variant="ghost" disabled={Boolean(paper.figures.find(x=>x.id===f.id)?.panels.length)}
            onClick={()=>{if(window.confirm('确认删除当前空 Figure？'))void mutate({action:'delete',kind:'figure',id:f.id})}}>删除空组</UI.Button>
          <UI.Button small disabled={!isTauri()}
            onClick={()=>{setExportFigureId(f.id);setExportPreview(null);void previewExport(f.id)}}>预览导出</UI.Button>
        </div>}/>
      <section className="dm-real-paper-metadata" aria-label="论文状态与备注">
        <label>论文状态
          <select aria-label="真实论文状态" value={paperStatus} onChange={e=>setPaperStatus(e.target.value)}>
            {['draft','in_progress','ready','submitted','published','archived'].map(v=><option key={v} value={v}>{v}</option>)}
          </select>
        </label>
        <label>备注<textarea aria-label="真实论文备注" maxLength={4000}
          value={paperNotes} onChange={e=>setPaperNotes(e.target.value)}/></label>
        <UI.Button small disabled={busy||paperStatus===paper.status&&paperNotes===paper.notes}
          onClick={()=>void mutate({action:'metadata',id:paper.id,status:paperStatus,notes:paperNotes})}>保存状态与备注</UI.Button>
      </section>
      {exportPreview&&<div className="dm-paper-export" role="region" aria-label="Figure 导出核验">
        <strong>{figure?.label} · 待确认导出</strong>
        <p>{exportPreview.panelCount} 个 Panel，{exportPreview.referenceCount} 条引用，
          {exportPreview.uniqueFileCount} 个唯一文件，{exportPreview.uniqueBytes} 字节。</p>
        <UI.Button disabled={exportBusy||exportPreview.uniqueFileCount===0} onClick={()=>void exportPinnedFigure()}>
          选择目录并验证导出</UI.Button>
        <UI.Button disabled={!exportBusy} onClick={()=>void cancelExport()}>取消导出</UI.Button>
      </div>}
      {exportStatus&&<p role="status">{exportStatus}</p>}
    </div>}
    {editor&&<div className="dm-real-modal-backdrop"><section role="dialog" aria-modal="true"
      aria-label="新增论文结构" className="dm-real-modal">
      <h2>{editor.action==='create'?'新建':editor.action==='rename'?'重命名':'删除'} {editor.kind}</h2>
      {editor.action==='delete'?<p>只能删除没有有效引用和依赖子节点的记录。</p>:
        <UI.Input ariaLabel="结构名称" placeholder="输入名称" value={editor.name}
          onChange={name=>setEditor({...editor,name})}/>} 
      {error&&<p role="alert">{error}</p>}
      <div className="dm-real-modal-actions">
        <UI.Button disabled={busy} onClick={()=>setEditor(null)}>取消</UI.Button>
        <UI.Button variant={editor.action==='delete'?'danger':'primary'}
          disabled={busy||(editor.action!=='delete'&&!editor.name.trim())}
          onClick={()=>void mutate({
            action:editor.action,kind:editor.kind,
            ...(editor.id?{id:editor.id}:{}),
            ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
            ...(editor.action==='create'&&editor.parentId?{parentId:editor.parentId}:{})
          },editor.requestId)}>确认{editor.action==='create'?'创建':editor.action==='rename'?'修改':'删除'}</UI.Button>
      </div>
    </section></div>}
    {panel&&<aside className="dm-real-detail dm-paper-drawer" role="dialog" aria-label="Panel 详情抽屉">
      <button type="button" onClick={()=>setPanelId(null)}>关闭 ×</button>
      <h2>{panel.label}</h2>
      <div className="dm-real-panel-actions">
        <UI.Button small onClick={()=>begin('panel',panelOwner?.id,'rename',panel.id,panel.label)}>修改标题</UI.Button>
        <UI.Button small disabled={busy||!panelOwner||panelOwner.panels[0]?.id===panel.id}
          onClick={()=>{if(!panelOwner)return;
            reorder('panel',panelOwner.id,panelOwner.panels.map(x=>x.id),
              panelOwner.panels.findIndex(x=>x.id===panel.id),-1);}}>上移</UI.Button>
        <UI.Button small disabled={busy||!panelOwner||panelOwner.panels.at(-1)?.id===panel.id}
          onClick={()=>{if(!panelOwner)return;
            reorder('panel',panelOwner.id,panelOwner.panels.map(x=>x.id),
              panelOwner.panels.findIndex(x=>x.id===panel.id),1);}}>下移</UI.Button>
        <UI.Button small disabled={busy||panel.assets.length>0||panel.representatives.length>0}
          onClick={()=>{if(window.confirm('确认删除没有任何固定引用的 Panel？'))
            void mutate({action:'delete',kind:'panel',id:panel.id});}}>删除空 Panel</UI.Button>
      </div>
      <p>本 Panel 的版本关系来自 S9 SQLite，不能通过文件名推断。</p>
      <h3>显示与统计固定版本</h3>
      {panel.assets.length===0?<p>尚未固定任何文件版本。</p>:panel.assets.map(a=>
        <article key={a.assetVersionId+'-'+a.usage} className="dm-paper-reference">
          <strong>{a.name}</strong><span>{a.stage} · {a.usage} · v{a.versionNumber}</span>
          <code>AssetVersionId: {a.assetVersionId}</code><code>SHA-256: {a.sha256}</code>
          <button type="button" onClick={()=>onJumpToAssay(a.assayId)}>查看来源实验</button>
          <button type="button" disabled={busy} onClick={()=>void mutate({
            action:'detach',id:panel.id,versionId:a.assetVersionId,usage:a.usage
          })}>仅移除本 Panel 引用</button>
        </article>)}
      <h3>代表性样本</h3>
      {panel.representatives.length?panel.representatives.map(r=>
        <div key={r.replicateId} className="dm-paper-reference">
          {r.replicateId} → {r.assetVersionId??'无固定版本'}
          <UI.Button small disabled={busy} onClick={()=>void mutate({
            action:'clearRepresentative',id:panel.id,replicateId:r.replicateId
          })}>取消代表来源</UI.Button></div>):
        <p>未设置有证据的代表样本。</p>}
      {panel.assets.filter(a=>a.replicateId).map(a=>
        <UI.Button key={a.assetVersionId} small disabled={busy} onClick={()=>void mutate({
          action:'representative',id:panel.id,replicateId:a.replicateId!,versionId:a.assetVersionId
        })}>设定代表来源：{a.replicateId} · v{a.versionNumber}</UI.Button>)}
      {isTauri()?<ProvenanceView key={projectId+panel.id} projectId={projectId} action="panel" id={panel.id}/>:
        <p>原生版本溯源仅在 Windows 后端可用。</p>}
      <h3>关联真实文件版本</h3>
      <UI.Input ariaLabel="搜索候选文件" placeholder="按文件名搜索" value={candidateSearch}
        onChange={v=>{setCandidateSearch(v);setCandidateOffset(0);}}/>
      <UI.Select ariaLabel="固定版本用途" value={usage} onChange={setUsage}
        options={['display','statistics','source','other'].map(id=>({value:id,label:id}))}/>
      <p>候选数 {candidateTotal??'待查询'} · 当前第 {candidateOffset+1} 条起，最多 100 条。</p>
      <div className="dm-paper-candidates">{candidates.map(c=><label key={c.assetVersionId}>
        <input type="checkbox" checked={selectedVersions.includes(c.assetVersionId)}
          onChange={e=>setSelectedVersions(old=>e.target.checked?[...new Set([...old,c.assetVersionId])]:
            old.filter(id=>id!==c.assetVersionId))}/>
        <strong>{c.name}</strong><small>v{c.versionNumber} · {c.assetVersionId}</small>
      </label>)}</div>
      <UI.Button small disabled={candidateOffset===0||busy} onClick={()=>{setSelectedVersions([]);setCandidateOffset(v=>Math.max(0,v-100))}}>上一页</UI.Button>
      <UI.Button small disabled={!candidateTruncated||busy} onClick={()=>{setSelectedVersions([]);setCandidateOffset(v=>v+100)}}>下一页</UI.Button>
      <UI.Button variant="primary" disabled={busy||!selectedVersions.length} onClick={()=>void mutate({
        action:'attach',id:panel.id,versionIds:selectedVersions,usage
      })}>固定关联所选 {selectedVersions.length} 个版本</UI.Button>
    </aside>}
  </section>;
}
