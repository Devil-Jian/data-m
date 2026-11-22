import React,{useEffect,useRef,useState} from 'react';
import type {AssetQuery,AssetRow} from './asset-browser.ts';
import {currentLinks,lifecycleApi,lifecycleError,lifecycleRequest,recycleApi,recycledLinks} from './asset-lifecycle.ts';
import type {ExpectedLink,LifecycleAction,LifecycleApi,LifecycleRequest,RecycleApi,RecycledRow} from './asset-lifecycle.ts';

type Editor={action:LifecycleAction;items:ExpectedLink[];name:string;request?:LifecycleRequest};
interface Props {
 query:AssetQuery;page:AssetRow[];chosen:Set<string>;
 listRef:React.RefObject<HTMLDivElement|null>;
 onChanged:()=>void;
 lifecycle?:LifecycleApi;recycle?:RecycleApi;
}
export function AssetLifecycle({query,page,chosen,listRef,onChanged,
 lifecycle=lifecycleApi,recycle=recycleApi}:Props){
 const [recycleOpen,setRecycleOpen]=useState(false),[recycleRows,setRecycleRows]=useState<RecycledRow[]>([]);
 const [recycleTotal,setRecycleTotal]=useState(0),[recycleNext,setRecycleNext]=useState<string|null>(null);
 const [recyclePages,setRecyclePages]=useState<string[]>([]),[recycleChosen,setRecycleChosen]=useState<Set<string>>(new Set());
 const [recycleSearch,setRecycleSearch]=useState(''),[refresh,setRefresh]=useState(0);
 const [recycleLoading,setRecycleLoading]=useState(false),[editor,setEditor]=useState<Editor|null>(null);
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[notice,setNotice]=useState('');
 const dialog=useRef<HTMLDialogElement>(null),ticket=useRef(0);
 const cursor=recyclePages.at(-1);
 function open(action:LifecycleAction,forcedId?:string){
   try{
     const rows=action==='restore'?recycleRows.filter(row=>recycleChosen.has(row.assetLinkId)):
      page.filter(row=>forcedId?row.assetLinkId===forcedId||(!chosen.has(forcedId)&&false):chosen.has(row.assetLinkId));
     const items=action==='restore'?recycledLinks(rows as RecycledRow[]):currentLinks(query,rows as AssetRow[]);
     if(!items.length||items.length>100||(action==='rename'&&items.length!==1))
       throw {code:'INVALID_REQUEST'};
     setError('');setNotice('');
     setEditor({action,items,name:action==='rename'?(rows[0] as AssetRow).name:''});
   }catch(e){setNotice('');setError(lifecycleError(e).message);}
 }
 // Listen to the actual asset right-click context menu. The renderer never
 // trusts a filename: the link is re-resolved from this currently visible page.
 useEffect(()=>{
   const rename=(event:Event)=>open('rename',(event as CustomEvent<{id:string}>).detail.id);
   const remove=(event:Event)=>open('soft_delete',(event as CustomEvent<{id:string}>).detail.id);
   window.addEventListener('data-m-s6-rename',rename);
   window.addEventListener('data-m-s6-delete',remove);
   return ()=>{window.removeEventListener('data-m-s6-rename',rename);window.removeEventListener('data-m-s6-delete',remove);};
 });
 useEffect(()=>{
   const onKey=(event:KeyboardEvent)=>{
     if(busy||recycleOpen||editor)return;
     if(event.key!=='F2'&&event.key!=='Delete')return;
     const active=event.target as HTMLElement|null;
     if(!active||active.closest('input,textarea,[contenteditable="true"],dialog')||!listRef.current?.contains(active))return;
     if(!chosen.size)return;
     event.preventDefault();
     open(event.key==='F2'?'rename':'soft_delete');
   };
   window.addEventListener('keydown',onKey);
   return ()=>window.removeEventListener('keydown',onKey);
 });
 useEffect(()=>{
   if(editor&&!dialog.current?.open)dialog.current?.showModal();
   if(!editor&&dialog.current?.open)dialog.current.close();
 },[editor]);
 // Scope change invalidates all recycle selections and any unsent editor.
 useEffect(()=>{
   setRecyclePages([]);setRecycleChosen(new Set());setRecycleRows([]);
   setEditor(null);setError('');setNotice('');
 },[query.assayId,query.stage,query.groupId,query.replicateId]);
 useEffect(()=>{setRecyclePages([]);setRecycleChosen(new Set());},[recycleSearch]);
 useEffect(()=>{
   if(!recycleOpen)return;
   const id=++ticket.current;
   setRecycleLoading(true);setError('');
   const timer=window.setTimeout(()=>{
     recycle({...query,search:recycleSearch,limit:50,...(cursor?{cursor}:{})})
       .then(result=>{
         if(id!==ticket.current)return;
         setRecycleRows(result.items);setRecycleTotal(result.total);setRecycleNext(result.nextCursor);
         setRecycleChosen(new Set());
       }).catch(reason=>{
         if(id===ticket.current){setRecycleRows([]);setRecycleTotal(0);setRecycleNext(null);setError(lifecycleError(reason).message);}
       }).finally(()=>{if(id===ticket.current)setRecycleLoading(false);});
   },recycleSearch?150:0);
   return ()=>{window.clearTimeout(timer);++ticket.current;};
 },[recycleOpen,recycle,query.assayId,query.stage,query.groupId,query.replicateId,recycleSearch,cursor,refresh]);
 async function submit(){
   if(!editor||busy)return;
   let request:LifecycleRequest;
   try{
     request=editor.request??lifecycleRequest(editor.action,editor.items,editor.action==='rename'?editor.name:undefined);
     setEditor({...editor,request});
   }catch(e){setError(lifecycleError(e).message);return;}
   setBusy(true);setError('');
   try{
     const result=await lifecycle(request);
     setNotice((result.action==='rename'?'重命名':result.action==='restore'?'恢复':'移至回收区')+
       '成功：'+result.results.length+' 条关联。');
     setEditor(null);setRecycleChosen(new Set());setRefresh(x=>x+1);onChanged();
   }catch(reason){setError(lifecycleError(reason).message);}
   finally{setBusy(false);}
 }
 const selectedCount=page.filter(row=>chosen.has(row.assetLinkId)).length;
 return <div className="dm-lifecycle" aria-label="文件重命名与回收">
   <div className="dm-asset-tools">
     <button type="button" disabled={busy||recycleOpen||selectedCount!==1} onClick={()=>open('rename')}>重命名 (F2)</button>
     <button type="button" disabled={busy||recycleOpen||selectedCount===0} onClick={()=>open('soft_delete')}>移至回收区 (Delete)</button>
     <button type="button" disabled={busy} aria-pressed={recycleOpen} onClick={()=>{
       setRecycleOpen(v=>!v);setRecycleChosen(new Set());setRecyclePages([]);setError('');setNotice('');
     }}>{recycleOpen?'关闭回收区':'回收区'}</button>
   </div>
   {notice&&<p className="dm-asset-feedback" role="status">{notice}</p>}
   {error&&!editor&&<p role="alert">{error}</p>}
   {recycleOpen&&<section className="dm-recycle-panel" aria-label="资产回收区">
     <header><h4>逻辑回收区</h4><span role="status">已回收 {recycleTotal} 条 · 本页 {recycleRows.length} 条</span></header>
     <p className="dm-asset-hint">仅回收关联，不删除磁盘文件、CAS Blob 或论文固定版本。</p>
     <div className="dm-asset-tools">
       <input aria-label="搜索回收文件" value={recycleSearch} placeholder="搜索已删除名称"
         onChange={event=>setRecycleSearch(event.target.value)}/>
       <button type="button" disabled={busy||recycleLoading||!recycleRows.length}
         onClick={()=>setRecycleChosen(new Set(recycleRows.filter(x=>x.canRestore).map(x=>x.assetLinkId)))}>选择本页可恢复</button>
       <button type="button" onClick={()=>setRecycleChosen(new Set())}>取消回收选择</button>
       <button type="button" disabled={busy||recycleLoading||!recycleChosen.size}
         onClick={()=>open('restore')}>恢复选中 ({recycleChosen.size})</button>
     </div>
     <div role="listbox" aria-label="回收文件列表" aria-multiselectable="true" className="dm-recycle-list">
       {recycleRows.map(row=><div key={row.assetLinkId} role="option" aria-selected={recycleChosen.has(row.assetLinkId)}>
         <label><input type="checkbox" aria-label={'选择回收 '+row.name}
           checked={recycleChosen.has(row.assetLinkId)} disabled={!row.canRestore}
           onChange={event=>setRecycleChosen(old=>{const next=new Set(old);if(event.target.checked)next.add(row.assetLinkId);else next.delete(row.assetLinkId);return next;})}/>
           <strong>{row.name}</strong></label>
         <small>{row.stage} / {row.replicateId??'实验级'} · {row.deletedAt} · SHA {row.sha256.slice(0,12)}…</small>
         {row.referenced&&<small>论文版本已引用</small>}
         {!row.canRestore&&<span>{row.restoreBlockReason==='NAME_CONFLICT'?'同名占用，无法恢复':'原目标失效，无法恢复'}</span>}
         <button type="button" disabled={busy||recycleLoading||!row.canRestore}
          onClick={()=>{setRecycleChosen(new Set([row.assetLinkId]));setEditor({action:'restore',items:recycledLinks([row]),name:''});setError('');}}>
          恢复</button>
       </div>)}
       {!recycleRows.length&&!recycleLoading&&<p>该作用域没有回收的文件关联。</p>}
     </div>
     <footer>
       <button type="button" disabled={busy||recycleLoading||!recyclePages.length}
        onClick={()=>setRecyclePages(xs=>xs.slice(0,-1))}>回收上一页</button>
       <span>第 {recyclePages.length+1} 页</span>
       <button type="button" disabled={busy||recycleLoading||!recycleNext}
        onClick={()=>{if(recycleNext)setRecyclePages(xs=>[...xs,recycleNext]);}}>回收下一页</button>
     </footer>
   </section>}
   <dialog ref={dialog} className="dm-lifecycle-dialog" aria-label="文件操作确认"
      onCancel={event=>{event.preventDefault();if(!busy){setEditor(null);setError('');}}}>
     {editor&&<form onSubmit={event=>{event.preventDefault();void submit();}}>
       <h3>{editor.action==='rename'?'重命名文件':editor.action==='soft_delete'?'移至回收区':'从回收区恢复'}</h3>
       <p>本次操作 {editor.items.length} 条文件关联；{editor.action==='soft_delete'?
         '仅标记为已删除，可从回收区恢复，不会删除任何磁盘文件。':
         editor.action==='restore'?'恢复到原实验、样本及阶段，冲突时不覆盖。':
         '仅修改当前关联的显示名称，不会修改原始文件或文件格式。'}</p>
       {editor.action==='rename'&&<label>新的显示名称
         <input autoFocus aria-label="新的文件名称" value={editor.name} disabled={busy}
           onChange={event=>{setEditor({...editor,name:event.target.value,request:undefined});setError('');}}/>
       </label>}
       {error&&<p role="alert">{error}</p>}
       <div className="dm-lifecycle-actions">
         <button type="button" disabled={busy} onClick={()=>{setEditor(null);setError('');}}>取消</button>
         <button type="submit" disabled={busy}>{editor.action==='rename'?'保存名称':
           editor.action==='restore'?'确认恢复':'确认移至回收区'}</button>
       </div>
     </form>}
   </dialog>
 </div>;
}
