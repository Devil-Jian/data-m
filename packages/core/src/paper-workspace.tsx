import React, {useCallback, useEffect, useMemo, useRef, useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {listen} from '@tauri-apps/api/event';
import './paper-workspace.css';
import {ProvenanceView} from './provenance-view';

type Asset={assetVersionId:string;usage:string;versionNumber:number;name:string;sha256:string;stage:string;assayId:string;replicateId:string|null};
type Rep={replicateId:string;assetVersionId:string|null};
type Panel={id:string;label:string;assets:Asset[];representatives:Rep[]};
type Figure={id:string;label:string;panels:Panel[]};
type Paper={id:string;title:string;status:string;notes:string;figures:Figure[]};
type Candidate={assetVersionId:string;assetId:string;versionNumber:number;name:string;sha256:string;stage:string;assayId:string;groupId:string|null;replicateId:string|null};
type ProjectDocument={id:string;name:string;kind:'paper'|'figure';assetVersionId:string;sha256:string};
type Snapshot={papers:Paper[];documents?:ProjectDocument[];candidates:Candidate[];candidateTotalCount?:number;candidateTruncated?:boolean};
type ProjectNode={id:string;kind:string;name:string};
type ExportPreview={figureId:string;panelCount:number;referenceCount:number;uniqueFileCount:number;uniqueBytes:number};
type ExportResult={status:string;preview:ExportPreview|null;folderPath:string|null;manifest:{status:string;referenceCount:number;physicalFileCount:number}|null};
type ExportProgress={requestId:string;completed:number;total:number};
const empty:Snapshot={papers:[],candidates:[]};
type Kind='paper'|'figure'|'panel';
type Action='create'|'rename'|'delete'|'reorder'|'attach'|'detach'|'representative'|'clearRepresentative'|'metadata';
function errorText(error:unknown):string {
  if(error && typeof error==='object' && 'message' in error) return String((error as {message:unknown}).message);
  return String(error);
}
export function PaperWorkspace({onClose}:{onClose?:()=>void}={}) {
 const [projects,setProjects]=useState<ProjectNode[]>([]);
 const [projectId,setProjectId]=useState('');
 const [snapshot,setSnapshot]=useState<Snapshot>(empty);
 const [selectedPaper,setSelectedPaper]=useState('');
 const [selectedPanel,setSelectedPanel]=useState('');
 const [reverseVersion,setReverseVersion]=useState('');
 const [name,setName]=useState('');
 const [search,setSearch]=useState('');
 const [stage,setStage]=useState('');
 const [candidateOffset,setCandidateOffset]=useState(0);
 const [candidateLoading,setCandidateLoading]=useState(false);
 const [usage,setUsage]=useState('display');
 const [paperStatus,setPaperStatus]=useState('draft');
 const [paperNotes,setPaperNotes]=useState('');
 const [selectedVersions,setSelectedVersions]=useState<string[]>([]);
 const [busy,setBusy]=useState(false);
 const [error,setError]=useState('');
 const [notice,setNotice]=useState('');
 const [ready,setReady]=useState(false);
 const [exportFigureId,setExportFigureId]=useState('');
 const [exportPreview,setExportPreview]=useState<ExportPreview|null>(null);
 const [exportResult,setExportResult]=useState<ExportResult|null>(null);
 const [exportError,setExportError]=useState('');
 const [exportBusy,setExportBusy]=useState(false);
 const [exportRequestId,setExportRequestId]=useState<string|null>(null);
 const [exportProgress,setExportProgress]=useState<ExportProgress|null>(null);
 const refreshGeneration=useRef(0);
 const refresh=useCallback(async(project:string)=>{
   const generation=++refreshGeneration.current;
   if(!project){setSnapshot(empty);return}
   const current=await invoke<Snapshot>('paper_workspace',{request:{action:'list',projectId:project}});
   // Async responses from the previous Project must not repopulate the UI.
   if(refreshGeneration.current===generation)
     setSnapshot(old=>({...old,papers:current.papers}));
 },[]);
 useEffect(()=>{
   let cancelled=false;
   invoke<{nodes:ProjectNode[]}>('project_tree',{request:{action:'list'}})
     .then(result=>{if(cancelled)return;const items=result.nodes.filter(n=>n.kind==='project');setProjects(items);setProjectId(id=>id||items[0]?.id||'');setReady(true)})
     .catch(e=>{if(!cancelled){setError('无法连接真实项目数据库：'+errorText(e));setReady(true)}});
   return()=>{cancelled=true};
 },[]);
 useEffect(()=>{setError('');setSelectedPaper('');setSelectedPanel('');setReverseVersion('');setSelectedVersions([]);setExportFigureId('');setExportPreview(null);setExportResult(null);setSnapshot(empty);refresh(projectId).catch(e=>setError(errorText(e)))},[projectId,refresh]);
 useEffect(()=>{setCandidateOffset(0)},[projectId,search,stage]);
 useEffect(()=>{
   let live=true;
   if(!projectId)return;
   setCandidateLoading(true);
   void invoke<Snapshot>('paper_workspace',{request:{action:'candidates',projectId,
     candidateQuery:search.trim(),...(stage?{candidateStage:stage}:{}),
     candidateOffset,candidateLimit:100}})
    .then(data=>{if(live)setSnapshot(old=>({...old,candidates:data.candidates,
       candidateTotalCount:data.candidateTotalCount,candidateTruncated:data.candidateTruncated}));})
    .catch(e=>{if(live)setError(errorText(e))})
    .finally(()=>{if(live)setCandidateLoading(false)});
   return()=>{live=false};
 },[projectId,search,stage,candidateOffset]);
 const paper=snapshot.papers.find(p=>p.id===selectedPaper)||snapshot.papers[0];
 const panel=paper?.figures.flatMap(f=>f.panels).find(p=>p.id===selectedPanel);
 useEffect(()=>{setPaperStatus(paper?.status||'draft');setPaperNotes(paper?.notes||'')},[paper?.id,paper?.status,paper?.notes]);
 const candidates=useMemo(()=>snapshot.candidates,[snapshot.candidates]);
 const mutate=useCallback(async(action:Action,more:Record<string,unknown>)=>{
   if(!projectId)return;
   setBusy(true);setError('');setNotice('');
   try{
     const result=await invoke<Snapshot>('paper_workspace',{request:{action,projectId,requestId:crypto.randomUUID(),...more}});
     setSnapshot(old=>({...old,papers:result.papers}));
     await refresh(projectId);
     setName('');setSelectedVersions([]);setNotice('更改已保存至本地数据库');
   }catch(e){setError(errorText(e))}
   finally{setBusy(false)}
 },[projectId,refresh]);
 async function create(kind:Kind,parentId?:string){
   if(!name.trim()){setError('先填写名称');return}
   await mutate('create',{kind,name:name.trim(),...(parentId?{parentId}:{})});
 }
 const selectedFigure=paper?.figures.find(f=>f.panels.some(p=>p.id===selectedPanel));
 const exportFigure=paper?.figures.find(f=>f.id===exportFigureId)||paper?.figures[0];
 useEffect(()=>{
   setExportPreview(null);setExportResult(null);setExportError('');
 },[projectId,paper?.id,exportFigure?.id]);
 useEffect(()=>{
   if(!exportRequestId)return;
   let cleanup:(()=>void)|undefined;
   let disposed=false;
   void listen<ExportProgress>('s10-figure-export-progress',event=>{
     if(event.payload.requestId===exportRequestId)setExportProgress(event.payload);
   }).then(unlisten=>{if(disposed)unlisten();else cleanup=unlisten})
     .catch(()=>{});
   return()=>{disposed=true;cleanup?.()};
 },[exportRequestId]);
 async function previewExport(){
   if(!paper||!exportFigure)return;
   setExportError('');setExportResult(null);setExportPreview(null);
   try {
     const result=await invoke<ExportResult>('figure_export',{request:{
       action:'preview',projectId,paperId:paper.id,figureId:exportFigure.id
     }});
     if(result.status!=='preview'||!result.preview)throw Error('无法获取可信 Figure 导出范围');
     setExportPreview(result.preview);
   }catch(e){setExportError(errorText(e))}
 }
 async function beginExport(){
   if(!paper||!exportFigure||!exportPreview||exportPreview.figureId!==exportFigure.id)return;
   if(!window.confirm('将从固定版本 CAS 导出 '+exportPreview.uniqueFileCount+' 个文件（'+exportPreview.referenceCount+' 条引用），并逐文件校验 SHA-256。选择全新目标目录，是否继续？'))return;
   const requestId=crypto.randomUUID();
   setExportBusy(true);setExportRequestId(requestId);setExportError('');setExportResult(null);
   setExportProgress({requestId,completed:0,total:exportPreview.uniqueFileCount});
   try{
     const result=await invoke<ExportResult>('figure_export',{request:{
       action:'export',projectId,paperId:paper.id,figureId:exportFigure.id,confirm:true,requestId
     }});
     if(result.status!=='complete'||result.manifest?.status!=='complete')
       throw Error('导出未获得完整 SHA 校验确认');
     setExportResult(result);
   }catch(e){setExportError(errorText(e))}
   finally{setExportBusy(false);setExportRequestId(null)}
 }
 async function cancelExport(){
   if(!exportRequestId)return;
   try {
     const accepted=await invoke<boolean>('figure_export_cancel',{requestId:exportRequestId});
     if(accepted)setExportError('已请求取消，正在安全结束文件复制；未完成包不会正式发布。');
   }catch(e){setExportError(errorText(e))}
 }
 const grouped=candidates.reduce<Record<string,Candidate[]>>((out,c)=>{const k=c.assayId+' / '+(c.groupId||'未分组')+' / '+(c.replicateId||'未关联样本');(out[k]??=[]).push(c);return out},{});
 return <div className="s9-shell" aria-label="S9 真实论文管理工作区">
   <div className="s9-top"><div><h1>论文管理</h1><small>Paper / Figure / Panel · 固定 AssetVersion · 真实 SQLite 持久化</small></div>
     {onClose&&<button disabled={exportBusy} onClick={onClose}>返回项目管理</button>}
     <label>科研项目 <select disabled={exportBusy} aria-label="选择真实科研项目" value={projectId} onChange={e=>setProjectId(e.target.value)}>{projects.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
   </div>
   {error&&<div className="s9-error" role="alert">{error}</div>}
   {notice&&<div className="s9-notice" role="status">{notice}</div>}
   {!ready&&<p>读取本地项目…</p>}
   {ready&&!projects.length&&<p>没有可用的真实项目。请先在项目管理工作区创建科研项目；演示论文不会写入数据库。</p>}
   {!!projects.length&&<div className="s9-columns">
    <section className="s9-structure"><h3>论文结构</h3>
     <section aria-label="项目级论文与组图原件">
       <h4>已导入项目原件（尚未绑定 Panel）</h4>
       {(snapshot.documents??[]).map(doc=><div key={doc.id} className="s9-node">
         <strong>{doc.kind==='paper'?'论文原件':'Figure 组图'}：{doc.name}</strong>
         <small>AssetVersion {doc.assetVersionId.slice(0,12)} · SHA-256 {doc.sha256.slice(0,12)}…</small>
       </div>)}
       {!(snapshot.documents?.length)&&<p>暂无项目级论文/组图文件。</p>}
       <small>原件已按项目保存，不自动创建 Paper、Figure、Panel 或 ShowData 关系。</small>
     </section>
     <div className="s9-new"><input aria-label="新增或修改名称" value={name} onChange={e=>setName(e.target.value)} placeholder="论文 / Figure / Panel 名称"/>
       <button disabled={busy} onClick={()=>create('paper')}>新建论文</button></div>
     {snapshot.papers.map(p=><div key={p.id} className="s9-tree-paper">
       <button className={paper?.id===p.id?'s9-active':''} onClick={()=>{setSelectedPaper(p.id);setSelectedPanel('');setExportFigureId(p.figures[0]?.id||'')}}>{p.title}</button>
       {paper?.id===p.id&&<>
         <label className="s9-meta">论文状态 <select aria-label="论文状态" value={paperStatus} onChange={e=>setPaperStatus(e.target.value)}>
           {['draft','in_progress','ready','submitted','published','archived'].map(status=><option key={status} value={status}>{status}</option>)}
         </select></label>
         <textarea className="s9-notes" aria-label="论文备注" placeholder="备注（保存在本地审计日志）" value={paperNotes} onChange={e=>setPaperNotes(e.target.value)} maxLength={4000}/>
         <button disabled={busy} onClick={()=>mutate('metadata',{id:p.id,status:paperStatus,notes:paperNotes})}>保存论文状态与备注</button>
         <div className="s9-actions">
          <button disabled={busy} onClick={()=>create('figure',p.id)}>+ Figure</button>
          <button disabled={busy||!name.trim()} onClick={()=>mutate('rename',{kind:'paper',id:p.id,name})}>改论文名</button>
          <button disabled={busy||p.figures.length>0} onClick={()=>mutate('delete',{kind:'paper',id:p.id})}>归档空论文</button>
         </div>
         {p.figures.map((f,index)=><div key={f.id} className="s9-tree-figure">
          <div className="s9-node"><strong>{f.label}</strong><small>{f.panels.length} Panels</small></div>
          <div className="s9-actions">
           <button disabled={busy} onClick={()=>create('panel',f.id)}>+ Panel</button>
           <button disabled={busy||!name.trim()} onClick={()=>mutate('rename',{kind:'figure',id:f.id,name})}>改名</button>
           <button disabled={busy||index===0} onClick={()=>mutate('reorder',{kind:'figure',parentId:p.id,orderIds:[...p.figures.slice(0,index-1).map(x=>x.id),f.id,p.figures[index-1].id,...p.figures.slice(index+1).map(x=>x.id)]})}>↑</button>
           <button disabled={busy||f.panels.length>0} onClick={()=>mutate('delete',{kind:'figure',id:f.id})}>删除空组</button>
          </div>
          {f.panels.map((x,i)=><div key={x.id} className="s9-tree-panel">
            <button className={panel?.id===x.id?'s9-active':''} onClick={()=>{setSelectedPanel(x.id);setExportFigureId(f.id)}}>{x.label} · {x.assets.length} 份</button>
            <div className="s9-actions">
             <button disabled={busy||!name.trim()} onClick={()=>mutate('rename',{kind:'panel',id:x.id,name})}>改名</button>
             <button disabled={busy||i===0} onClick={()=>mutate('reorder',{kind:'panel',parentId:f.id,orderIds:[...f.panels.slice(0,i-1).map(z=>z.id),x.id,f.panels[i-1].id,...f.panels.slice(i+1).map(z=>z.id)]})}>↑</button>
             <button disabled={busy||x.assets.length>0||x.representatives.length>0} onClick={()=>mutate('delete',{kind:'panel',id:x.id})}>删除空Panel</button>
            </div>
          </div>)}
         </div>)}
       </>}
     </div>)}
    </section>
    <section className="s9-detail"><h3>{panel?panel.label:'选择 Panel'}</h3>
     <section className="s9-asset" aria-label="S10 Figure 无损导出">
       <h4>Figure 无损导出 · 固定版本</h4>
       <label>选择 Figure <select aria-label="选择导出 Figure" value={exportFigure?.id||''}
          disabled={exportBusy} onChange={e=>setExportFigureId(e.target.value)}>
         {paper?.figures.map(f=><option key={f.id} value={f.id}>导出目标：{f.label}</option>)}
       </select></label>
       <div className="s9-actions">
        <button disabled={!paper||!exportFigure||exportBusy} onClick={()=>void previewExport()}>预览导出范围</button>
        <button disabled={!exportPreview||exportPreview.figureId!==exportFigure?.id||exportPreview.uniqueFileCount===0||exportBusy}
          onClick={()=>void beginExport()}>选择目录并无损导出</button>
        <button disabled={!exportBusy||!exportRequestId} onClick={()=>void cancelExport()}>取消导出</button>
       </div>
       {exportPreview&&<p role="status">Panel {exportPreview.panelCount} 个 · 逻辑引用 {exportPreview.referenceCount} 条 · 唯一文件 {exportPreview.uniqueFileCount} 个 · {exportPreview.uniqueBytes} 字节（尚未验证原字节）</p>}
       {exportBusy&&<p role="status">原生导出正在执行：已验证 {exportProgress?.completed??0} / {exportProgress?.total??0} 个文件。可取消。</p>}
       {exportError&&<p role="alert" className="s9-error">{exportError}</p>}
       {exportResult?.status==='complete'&&exportResult.manifest?.status==='complete'&&
        <p role="status">无损导出完成：{exportResult.folderPath} · Manifest 记录 {exportResult.manifest.referenceCount} 条引用 / {exportResult.manifest.physicalFileCount} 个 SHA-256 已验证文件。</p>}
     </section>
     <p className="s9-muted">{selectedFigure?.label||'从左侧选择具体 Panel'} · 关联只锁定文件版本 ID，重命名/移动不会替换版本。</p>
     {!panel?<p>选择 Panel 查看其固定文件版本与来源。</p>:<>
       {panel.assets.length===0&&<p>尚无版本关联，可从右侧选择文件。</p>}
       {panel.assets.map(a=><article className="s9-asset" key={a.assetVersionId+'-'+a.usage}>
         <strong>{a.name}</strong><small>{a.assayId} · {a.stage} · {a.usage} · v{a.versionNumber}</small>
         <code title={a.assetVersionId}>Version ID: {a.assetVersionId}</code>
         <code title={a.sha256}>SHA-256: {a.sha256}</code>
         <div className="s9-actions"><span>已固定引用</span><button disabled={busy} onClick={()=>mutate('detach',{id:panel.id,versionId:a.assetVersionId,usage:a.usage})}>仅移除引用</button></div>
       </article>)}
       <ProvenanceView key={projectId+'-'+panel.id} projectId={projectId} action="panel" id={panel.id}/>
       <h4>反向查找固定文件版本</h4>
       <label>被哪些论文和统计引用 <select aria-label="选择反向查询版本" value={reverseVersion} onChange={e=>setReverseVersion(e.target.value)}>
        <option value="">选择文件版本</option>
        {panel.assets.map(a=><option key={a.assetVersionId} value={a.assetVersionId}>{a.name} · v{a.versionNumber}</option>)}
       </select></label>
       {reverseVersion&&<ProvenanceView key={projectId+'-'+reverseVersion} projectId={projectId} action="version" id={reverseVersion}/>}
       <h4>代表性生物学重复</h4>
       {panel.representatives.map(r=><div key={r.replicateId} className="s9-actions"><span>{r.replicateId} → {r.assetVersionId}</span>
         <button disabled={busy} onClick={()=>mutate('clearRepresentative',{id:panel.id,replicateId:r.replicateId})}>移除代表来源</button></div>)}
       {panel.assets.filter(a=>a.replicateId).map(a=><button key={a.assetVersionId} disabled={busy} onClick={()=>mutate('representative',{id:panel.id,replicateId:a.replicateId,versionId:a.assetVersionId})}>设置 {a.replicateId} 的代表版本 v{a.versionNumber}</button>)}
     </>}
    </section>
    <section className="s9-picker"><h3>选择固定版本</h3>
      <input aria-label="搜索真实文件版本" value={search} onChange={e=>setSearch(e.target.value)} placeholder="搜索文件、实验或样本"/>
      <select aria-label="按阶段筛选" value={stage} onChange={e=>setStage(e.target.value)}>
       <option value="">全部阶段</option>{['raw','processed','statistics','display'].map(s=><option key={s}>{s}</option>)}
      </select>
      <label>引用用途 <select aria-label="选择引用用途" value={usage} onChange={e=>setUsage(e.target.value)}>
       {['display','statistics','source','other'].map(s=><option key={s}>{s}</option>)}
      </select></label>
      <p role="status">服务端筛选：共 {snapshot.candidateTotalCount??'…'} 条候选，当前第 {candidateOffset+1} 条起；每页最多 100 条。{candidateLoading?'正在查询…':''}</p>
      <div className="s9-candidates">
       {Object.entries(grouped).map(([group,items])=><div key={group}><h4>{group}</h4>
        {items.map(c=><label className="s9-candidate" key={c.assetVersionId+'-'+group}>
         <input type="checkbox" checked={selectedVersions.includes(c.assetVersionId)} onChange={e=>setSelectedVersions(old=>e.target.checked?[...new Set([...old,c.assetVersionId])]:old.filter(v=>v!==c.assetVersionId))}/>
         <span><strong>{c.name}</strong><small>{c.stage} · v{c.versionNumber} · {c.assetVersionId.slice(0,12)}</small></span>
        </label>)}</div>)}
      </div>
      <div className="s9-actions">
       <button disabled={candidateLoading||candidateOffset===0} onClick={()=>setCandidateOffset(v=>Math.max(0,v-100))}>上一页候选</button>
       <button disabled={candidateLoading||!snapshot.candidateTruncated} onClick={()=>setCandidateOffset(v=>v+100)}>下一页候选</button>
      </div>
      <button className="s9-commit" disabled={busy||!panel||selectedVersions.length===0} onClick={()=>{
        if(!panel)return;void mutate('attach',{id:panel.id,versionIds:selectedVersions,usage});
      }}>原子关联所选 {selectedVersions.length} 个固定版本</button>
    </section>
   </div>}
 </div>;
}
