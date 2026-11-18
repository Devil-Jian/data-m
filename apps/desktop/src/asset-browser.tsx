import React,{useEffect,useRef,useState} from 'react';
import {invoke} from '@tauri-apps/api/core';
import {ProvenanceView} from '@data-m/core';
import {assetApi} from './asset-browser.ts';
import type {AssetApi,AssetQuery,AssetRow} from './asset-browser.ts';
import {beginAssetDrag,dropAssets,endAssetDrag,getClipboard,hasAssetDrag,pasteAssets,putClipboard,safeTransferError,sourcesFromRows,transferApi} from './asset-transfer.ts';
import type {AssetScope,TransferApi} from './asset-transfer.ts';
import {AssetLifecycle} from './asset-lifecycle.tsx';
import './asset-browser.css';

export function AssetBrowser({query,api=assetApi,transfers=transferApi}:{query:AssetQuery;api?:AssetApi;transfers?:TransferApi}){
  const [search,setSearch]=useState(''),[sort,setSort]=useState<AssetQuery['sort']>('name');
  const [view,setView]=useState<'list'|'grid'|'table'>('list');
  const [pages,setPages]=useState<string[]>([]),[page,setPage]=useState<AssetRow[]>([]);
  const [next,setNext]=useState<string|null>(null),[total,setTotal]=useState(0);
  const [chosen,setChosen]=useState<Set<string>>(new Set()),[anchor,setAnchor]=useState<string|null>(null);
  const [detail,setDetail]=useState<AssetRow|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const [traceProjectId,setTraceProjectId]=useState<string|null>(null);
  const [revision,setRevision]=useState(0),[feedback,setFeedback]=useState(''),[clipboardCount,setClipboardCount]=useState(0);
  const [menu,setMenu]=useState<{x:number;y:number;id:string}|null>(null);
  const epoch=useRef(0),rowRefs=useRef(new Map<string,HTMLButtonElement>()),listRef=useRef<HTMLDivElement>(null);
  const target:AssetScope|null=query.stage?{assayId:query.assayId,stage:query.stage,groupId:query.groupId??null,replicateId:query.replicateId??null}:null;
  useEffect(()=>{
    const onChanged=()=>{setRevision(v=>v+1);setMenu(null);};
    const onClipboard=()=>setClipboardCount(getClipboard()?.items.length??0);
    onClipboard();
    window.addEventListener('data-m-s5-changed',onChanged);
    window.addEventListener('data-m-s5-clipboard',onClipboard);
    return ()=>{window.removeEventListener('data-m-s5-changed',onChanged);window.removeEventListener('data-m-s5-clipboard',onClipboard);};
  },[]);
  const pageCursor=pages.at(-1);
  // A new page invalidates the previous page's write selection.
  useEffect(()=>{setChosen(new Set());setAnchor(null);},[pageCursor]);
  useEffect(()=>{setPages([]);setPage([]);setChosen(new Set());setAnchor(null);setDetail(null);setMenu(null);},[query.assayId,query.stage,query.groupId,query.replicateId,search,sort]);
  useEffect(()=>{
    const ticket=++epoch.current;setBusy(true);setError('');
    const timeout=window.setTimeout(()=>{
      api.list({...query,search,sort,limit:50,...(pageCursor?{cursor:pageCursor}:{})}).then(result=>{
        if(ticket!==epoch.current)return;
        setPage(result.items);setNext(result.nextCursor);setTotal(result.total);
        setChosen(previous=>new Set([...previous].filter(id=>result.items.some(item=>item.assetLinkId===id))));
      }).catch(e=>{if(ticket===epoch.current){setPage([]);setTotal(0);setNext(null);setError(e instanceof Error?e.message:'无法查询资产');}})
        .finally(()=>{if(ticket===epoch.current)setBusy(false)});
    },search?200:0);
    return ()=>{window.clearTimeout(timeout);++epoch.current;};
  },[api,query.assayId,query.stage,query.groupId,query.replicateId,search,sort,pageCursor,revision]);
  useEffect(()=>{
    let live=true;
    setTraceProjectId(null);
    if(!detail)return;
    type Node={id:string;kind:string;parentId:string|null};
    void invoke<{nodes:Node[]}>('project_tree',{request:{action:'list'}})
      .then(data=>{
        if(!live)return;
        const nodes=data.nodes;
        const assay=nodes.find(n=>n.kind==='assay'&&n.id===query.assayId);
        const theme=nodes.find(n=>n.kind==='theme'&&n.id===assay?.parentId);
        const project=nodes.find(n=>n.kind==='project'&&n.id===theme?.parentId);
        setTraceProjectId(project?.id??null);
      }).catch(()=>{if(live)setTraceProjectId(null)});
    return()=>{live=false};
  },[query.assayId,detail?.assetId]);
  function select(id:string,event:React.MouseEvent|React.KeyboardEvent){
    const index=page.findIndex(item=>item.assetLinkId===id);
    if(index<0)return;
    if(event.shiftKey&&anchor){
      const start=page.findIndex(item=>item.assetLinkId===anchor);
      if(start>=0){const ids=page.slice(Math.min(start,index),Math.max(start,index)+1).map(x=>x.assetLinkId);
        setChosen(old=>new Set([...old,...ids]));return;}
    }
    if(event.ctrlKey||event.metaKey){setChosen(old=>{const next=new Set(old);if(next.has(id))next.delete(id);else next.add(id);return next;});}
    else setChosen(new Set([id]));
    setAnchor(id);
  }
  function selectedSources(forcedId?:string){
    const ids=forcedId&&!chosen.has(forcedId)?new Set([forcedId]):chosen;
    return sourcesFromRows(query,page.filter(item=>ids.has(item.assetLinkId)));
  }
  function capture(action:'copy'|'move',forcedId?:string){
    try{
      const sources=selectedSources(forcedId);
      putClipboard(action,sources);
      setFeedback(sources.length+' 条关联已'+(action==='copy'?'复制':'剪切')+'到应用剪贴板，请选择目标后粘贴。');
      setError('');
    }catch(reason){setError(safeTransferError(reason).message);}
    setMenu(null);
  }
  async function paste(){
    if(!target)return;
    setError('');setFeedback('正在验证目标并提交批量操作…');setMenu(null);
    try{
      const result=await pasteAssets(target,transfers);
      setFeedback((result.action==='copy'?'复制':'移动')+'成功：'+result.links.length+' 条关联。');
    }catch(reason){setFeedback('');setError(safeTransferError(reason).message);}
  }
  async function drop(event:React.DragEvent<HTMLElement>){
    if(!hasAssetDrag())return;
    event.preventDefault();event.stopPropagation();
    if(!target)return;
    try{
      const result=await dropAssets(event,target,transfers);
      setError('');setFeedback((result.action==='copy'?'复制':'移动')+'成功：'+result.links.length+' 条关联。');
    }catch(reason){setFeedback('');setError(safeTransferError(reason).message);}
  }
  useEffect(()=>{
    const onKey=(event:KeyboardEvent)=>{
      if(!(event.ctrlKey||event.metaKey)||event.altKey)return;
      const active=event.target as HTMLElement|null;
      if(active?.closest('input,textarea,[contenteditable="true"],dialog'))return;
      const key=event.key.toLowerCase();
      if((key==='c'||key==='x')&&active&&listRef.current?.contains(active)&&chosen.size){
        event.preventDefault();capture(key==='c'?'copy':'move');
      }else if(key==='v'&&getClipboard()&&target){event.preventDefault();void paste();}
    };
    window.addEventListener('keydown',onKey);
    return ()=>window.removeEventListener('keydown',onKey);
  });
  async function open(item:AssetRow){
    const ticket=epoch.current;
    try {const result=await api.detail(item.assetLinkId);if(ticket===epoch.current)setDetail(result.item);}
    catch(e){setError(e instanceof Error?e.message:'详情读取失败');}
  }
  return <section className="dm-asset-browser" aria-label="真实资产浏览器" data-browser-mode={api===assetApi?'repository':'test-mock'}
    onDragOver={event=>{if(hasAssetDrag()){event.preventDefault();event.dataTransfer.dropEffect=event.ctrlKey?'copy':event.shiftKey?'move':'none';}}}
    onDrop={event=>{void drop(event);}}>
    <header><h3>已入库文件</h3><span role="status">{busy?'正在查询…':`匹配 ${total} 条 · 本页 ${page.length} 条 · 已选 ${chosen.size} 条`}</span></header>
    <div className="dm-asset-tools">
      <input aria-label="搜索已入库文件" placeholder="搜索文件名称" value={search} onChange={event=>setSearch(event.target.value)}/>
      <select aria-label="文件排序" value={sort} onChange={event=>setSort(event.target.value as AssetQuery['sort'])}>
        <option value="name">名称</option><option value="newest">最新版本时间</option><option value="size">文件大小</option>
      </select>
      <div className="dm-asset-view-switch" role="group" aria-label="文件显示方式">
        {(['list','grid','table'] as const).map(mode=><button type="button" key={mode}
          aria-pressed={view===mode} onClick={()=>setView(mode)}>{mode==='list'?'列表':mode==='grid'?'网格':'表格'}</button>)}
      </div>
      <button type="button" disabled={busy||page.length===0} onClick={()=>setChosen(new Set(page.map(item=>item.assetLinkId)))}>选择本页</button>
      <button type="button" onClick={()=>setChosen(new Set())}>取消选择</button>
      <button type="button" disabled={!chosen.size||busy} onClick={()=>capture('copy')}>复制 (Ctrl+C)</button>
      <button type="button" disabled={!chosen.size||busy} onClick={()=>capture('move')}>剪切 (Ctrl+X)</button>
      <button type="button" disabled={!clipboardCount||!target||busy} onClick={()=>void paste()}>粘贴 (Ctrl+V)</button>
    </div>
    <AssetLifecycle query={query} page={page} chosen={chosen} listRef={listRef} onChanged={()=>setRevision(x=>x+1)}/>
    <p className="dm-asset-hint">目标：{target?target.stage+' / '+(target.replicateId??'实验级'):'未选择合法阶段'} · 剪贴板 {clipboardCount} 条 · Ctrl+拖拽复制，Shift+拖拽移动。</p>
    {feedback&&<p className="dm-asset-feedback" aria-live="polite">{feedback}</p>}
    {error&&<p role="alert">{error}</p>}
    <div ref={listRef} className="dm-asset-list" data-view={view} role="listbox" aria-label="资产文件列表" aria-multiselectable="true"
      onKeyDown={event=>{
        if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='a'){event.preventDefault();setChosen(new Set(page.map(x=>x.assetLinkId)));return;}
        if(!['ArrowDown','ArrowUp'].includes(event.key))return;
        const current=page.findIndex(x=>x.assetLinkId===document.activeElement?.getAttribute('data-link-id'));
        const target=page[Math.max(0,Math.min(page.length-1,current+(event.key==='ArrowDown'?1:-1)))];
        if(target){event.preventDefault();rowRefs.current.get(target.assetLinkId)?.focus();select(target.assetLinkId,event);}
      }}>
      {page.map(item=><div key={item.assetLinkId} role="option" aria-selected={chosen.has(item.assetLinkId)}
        className={chosen.has(item.assetLinkId)?'is-selected':''}
        onContextMenu={event=>{event.preventDefault();if(!chosen.has(item.assetLinkId)){setChosen(new Set([item.assetLinkId]));setAnchor(item.assetLinkId);}setMenu({x:event.clientX,y:event.clientY,id:item.assetLinkId});}}>
        <button type="button" data-link-id={item.assetLinkId} draggable
          onDragStart={event=>{try{beginAssetDrag(selectedSources(item.assetLinkId));event.dataTransfer.setData('application/x-data-m-asset-links','1');event.dataTransfer.effectAllowed='copyMove';}catch(reason){event.preventDefault();setError(safeTransferError(reason).message);}}}
          onDragEnd={endAssetDrag} ref={node=>{if(node)rowRefs.current.set(item.assetLinkId,node);else rowRefs.current.delete(item.assetLinkId);}}
          aria-label={`选择 ${item.name}`} onClick={event=>select(item.assetLinkId,event)}
          onDoubleClick={()=>void open(item)}>{item.name}</button>
        <span>{item.stage}</span><span>{item.sizeBytes.toLocaleString()} B</span>
        <span>v{item.versionNumber}</span><button type="button" onClick={()=>void open(item)}>详情</button>
      </div>)}
      {!busy&&!error&&!page.length&&<p>该范围尚无已入库文件。</p>}
    </div>
    {menu&&<div className="dm-asset-context" role="menu" aria-label="资产右键菜单" style={{left:menu.x,top:menu.y}}>
      <button type="button" role="menuitem" onClick={()=>capture('copy')}>复制</button>
      <button type="button" role="menuitem" onClick={()=>capture('move')}>剪切</button>
      <button type="button" role="menuitem" disabled={!clipboardCount} onClick={()=>void paste()}>粘贴到当前阶段</button>
      <button type="button" role="menuitem" onClick={()=>{window.dispatchEvent(new CustomEvent('data-m-s6-rename',{detail:{id:menu.id}}));setMenu(null);}}>重命名</button>
      <button type="button" role="menuitem" onClick={()=>{window.dispatchEvent(new CustomEvent('data-m-s6-delete',{detail:{id:menu.id}}));setMenu(null);}}>移至回收区</button>
      <button type="button" role="menuitem" onClick={()=>setMenu(null)}>关闭菜单</button>
    </div>}
    <footer>
      <button type="button" disabled={!pages.length||busy} onClick={()=>setPages(v=>v.slice(0,-1))}>上一页</button>
      <span>第 {pages.length+1} 页</span>
      <button type="button" disabled={!next||busy} onClick={()=>{if(next)setPages(v=>[...v,next]);}}>下一页</button>
      <small>选择基于 AssetLinkId；仅对本页明确选中的关联执行操作；切换页或筛选即清空选区。</small>
    </footer>
    {detail&&<aside className="dm-asset-detail" aria-label="文件详情"><button type="button" onClick={()=>setDetail(null)}>关闭详情</button>
      <h3>{detail.name}</h3><dl><dt>关联 ID</dt><dd>{detail.assetLinkId}</dd><dt>资产 ID</dt><dd>{detail.assetId}</dd>
      <dt>固定版本 ID</dt><dd>{detail.assetVersionId}</dd><dt>版本</dt><dd>{detail.versionNumber}</dd>
      <dt>阶段</dt><dd>{detail.stage}</dd><dt>大小</dt><dd>{detail.sizeBytes} B</dd>
      <dt>SHA-256</dt><dd>{detail.sha256}</dd><dt>Panel 引用</dt><dd>{detail.referenced?'有':'无'}</dd></dl>
      {traceProjectId?<ProvenanceView key={traceProjectId+'-'+detail.assetId} projectId={traceProjectId} action="asset" id={detail.assetId}/>:
       <p>当前无法从项目树核实所属科研项目，不能显示未经验证的跨项目反向引用。</p>}
    </aside>}
  </section>;
}
