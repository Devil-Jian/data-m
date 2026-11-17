import React, {useCallback, useEffect, useMemo, useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import './paper-workspace.css';

type Asset={assetVersionId:string;usage:string;versionNumber:number;name:string;sha256:string;stage:string;assayId:string;replicateId:string|null};
type Rep={replicateId:string;assetVersionId:string|null};
type Panel={id:string;label:string;assets:Asset[];representatives:Rep[]};
type Figure={id:string;label:string;panels:Panel[]};
type Paper={id:string;title:string;figures:Figure[]};
type Candidate={assetVersionId:string;assetId:string;versionNumber:number;name:string;sha256:string;stage:string;assayId:string;groupId:string|null;replicateId:string|null};
type Snapshot={papers:Paper[];candidates:Candidate[]};
type ProjectNode={id:string;kind:string;name:string};
const empty:Snapshot={papers:[],candidates:[]};
type Kind='paper'|'figure'|'panel';
type Action='create'|'rename'|'delete'|'reorder'|'attach'|'detach'|'representative';
function errorText(error:unknown):string {
  if(error && typeof error==='object' && 'message' in error) return String((error as {message:unknown}).message);
  return String(error);
}
export function PaperWorkspace() {
 const [projects,setProjects]=useState<ProjectNode[]>([]);
 const [projectId,setProjectId]=useState('');
 const [snapshot,setSnapshot]=useState<Snapshot>(empty);
 const [selectedPaper,setSelectedPaper]=useState('');
 const [selectedPanel,setSelectedPanel]=useState('');
 const [name,setName]=useState('');
 const [search,setSearch]=useState('');
 const [stage,setStage]=useState('');
 const [usage,setUsage]=useState('display');
 const [selectedVersions,setSelectedVersions]=useState<string[]>([]);
 const [busy,setBusy]=useState(false);
 const [error,setError]=useState('');
 const [notice,setNotice]=useState('');
 const [ready,setReady]=useState(false);
 const refresh=useCallback(async(project:string)=>{
   if(!project){setSnapshot(empty);return}
   const [current,candidates]=await Promise.all([
     invoke<Snapshot>('paper_workspace',{request:{action:'list',projectId:project}}),
     invoke<Snapshot>('paper_workspace',{request:{action:'candidates',projectId:project}})
   ]);
   setSnapshot({papers:current.papers,candidates:candidates.candidates});
 },[]);
 useEffect(()=>{
   let cancelled=false;
   invoke<{nodes:ProjectNode[]}>('project_tree',{request:{action:'list'}})
     .then(result=>{if(cancelled)return;const items=result.nodes.filter(n=>n.kind==='project');setProjects(items);setProjectId(id=>id||items[0]?.id||'');setReady(true)})
     .catch(e=>{if(!cancelled){setError('无法连接真实项目数据库：'+errorText(e));setReady(true)}});
   return()=>{cancelled=true};
 },[]);
 useEffect(()=>{setError('');setSelectedPaper('');setSelectedPanel('');setSelectedVersions([]);refresh(projectId).catch(e=>setError(errorText(e)))},[projectId,refresh]);
 const paper=snapshot.papers.find(p=>p.id===selectedPaper)||snapshot.papers[0];
 const panel=paper?.figures.flatMap(f=>f.panels).find(p=>p.id===selectedPanel);
 const candidates=useMemo(()=>snapshot.candidates.filter(c=>
    (!search||[c.name,c.assayId,c.assetVersionId,c.replicateId||''].some(v=>v.toLowerCase().includes(search.toLowerCase())))&&
    (!stage||c.stage===stage)
 ),[snapshot.candidates,search,stage]);
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
 const grouped=candidates.reduce<Record<string,Candidate[]>>((out,c)=>{const k=c.assayId+' / '+(c.groupId||'未分组')+' / '+(c.replicateId||'未关联样本');(out[k]??=[]).push(c);return out},{});
 return <div className="s9-shell" aria-label="S9 真实论文管理工作区">
   <div className="s9-top"><div><strong>论文 / Figure / Panel</strong><small>固定 AssetVersion · 真实 SQLite 持久化</small></div>
     <label>科研项目 <select aria-label="选择真实科研项目" value={projectId} onChange={e=>setProjectId(e.target.value)}>{projects.map(p=><option key={p.id} value={p.id}>{p.name}</option>)}</select></label>
   </div>
   {error&&<div className="s9-error" role="alert">{error}</div>}
   {notice&&<div className="s9-notice" role="status">{notice}</div>}
   {!ready&&<p>读取本地项目…</p>}
   {ready&&!projects.length&&<p>没有可用的真实项目。请先在项目管理工作区创建科研项目；演示论文不会写入数据库。</p>}
   {!!projects.length&&<div className="s9-columns">
    <section className="s9-structure"><h3>论文结构</h3>
     <div className="s9-new"><input aria-label="新增或修改名称" value={name} onChange={e=>setName(e.target.value)} placeholder="论文 / Figure / Panel 名称"/>
       <button disabled={busy} onClick={()=>create('paper')}>新建论文</button></div>
     {snapshot.papers.map(p=><div key={p.id} className="s9-tree-paper">
       <button className={paper?.id===p.id?'s9-active':''} onClick={()=>{setSelectedPaper(p.id);setSelectedPanel('')}}>{p.title}</button>
       {paper?.id===p.id&&<>
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
            <button className={panel?.id===x.id?'s9-active':''} onClick={()=>setSelectedPanel(x.id)}>{x.label} · {x.assets.length} 份</button>
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
     <p className="s9-muted">{selectedFigure?.label||'从左侧选择具体 Panel'} · 关联只锁定文件版本 ID，重命名/移动不会替换版本。</p>
     {!panel?<p>选择 Panel 查看其固定文件版本与来源。</p>:<>
       {panel.assets.length===0&&<p>尚无版本关联，可从右侧选择文件。</p>}
       {panel.assets.map(a=><article className="s9-asset" key={a.assetVersionId+'-'+a.usage}>
         <strong>{a.name}</strong><small>{a.assayId} · {a.stage} · {a.usage} · v{a.versionNumber}</small>
         <code title={a.assetVersionId}>Version ID: {a.assetVersionId}</code>
         <code title={a.sha256}>SHA-256: {a.sha256}</code>
         <div className="s9-actions"><span>已固定引用</span><button disabled={busy} onClick={()=>mutate('detach',{id:panel.id,versionId:a.assetVersionId,usage:a.usage})}>仅移除引用</button></div>
       </article>)}
       <h4>代表性生物学重复</h4>
       {panel.representatives.map(r=><p key={r.replicateId}>{r.replicateId} → {r.assetVersionId}</p>)}
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
      <div className="s9-candidates">
       {Object.entries(grouped).map(([group,items])=><div key={group}><h4>{group}</h4>
        {items.map(c=><label className="s9-candidate" key={c.assetVersionId+'-'+group}>
         <input type="checkbox" checked={selectedVersions.includes(c.assetVersionId)} onChange={e=>setSelectedVersions(old=>e.target.checked?[...new Set([...old,c.assetVersionId])]:old.filter(v=>v!==c.assetVersionId))}/>
         <span><strong>{c.name}</strong><small>{c.stage} · v{c.versionNumber} · {c.assetVersionId.slice(0,12)}</small></span>
        </label>)}</div>)}
      </div>
      <button className="s9-commit" disabled={busy||!panel||selectedVersions.length===0} onClick={async()=>{
        if(!panel)return;for(const versionId of selectedVersions){await mutate('attach',{id:panel.id,versionId,usage})}
      }}>关联所选 {selectedVersions.length} 个固定版本</button>
    </section>
   </div>}
 </div>;
}
