import React,{useEffect,useRef,useState} from 'react';
import {assetApi} from './asset-browser.ts';
import type {AssetApi,AssetQuery,AssetRow} from './asset-browser.ts';
import './asset-browser.css';

export function AssetBrowser({query,api=assetApi}:{query:AssetQuery;api?:AssetApi}){
  const [search,setSearch]=useState(''),[sort,setSort]=useState<AssetQuery['sort']>('name');
  const [pages,setPages]=useState<string[]>([]),[page,setPage]=useState<AssetRow[]>([]);
  const [next,setNext]=useState<string|null>(null),[total,setTotal]=useState(0);
  const [chosen,setChosen]=useState<Set<string>>(new Set()),[anchor,setAnchor]=useState<string|null>(null);
  const [detail,setDetail]=useState<AssetRow|null>(null),[error,setError]=useState(''),[busy,setBusy]=useState(false);
  const epoch=useRef(0),rowRefs=useRef(new Map<string,HTMLButtonElement>());
  const pageCursor=pages.at(-1);
  useEffect(()=>{setPages([]);setChosen(new Set());setAnchor(null);setDetail(null);},[query.assayId,query.stage,query.groupId,query.replicateId,search,sort]);
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
  },[api,query.assayId,query.stage,query.groupId,query.replicateId,search,sort,pageCursor]);
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
  async function open(item:AssetRow){
    const ticket=epoch.current;
    try {const result=await api.detail(item.assetLinkId);if(ticket===epoch.current)setDetail(result.item);}
    catch(e){setError(e instanceof Error?e.message:'详情读取失败');}
  }
  return <section className="dm-asset-browser" aria-label="真实资产浏览器" data-browser-mode={api===assetApi?'repository':'test-mock'}>
    <header><h3>已入库文件</h3><span role="status">{busy?'正在查询…':`匹配 ${total} 条 · 本页 ${page.length} 条 · 已选 ${chosen.size} 条`}</span></header>
    <div className="dm-asset-tools">
      <input aria-label="搜索已入库文件" placeholder="搜索文件名称" value={search} onChange={event=>setSearch(event.target.value)}/>
      <select aria-label="文件排序" value={sort} onChange={event=>setSort(event.target.value as AssetQuery['sort'])}>
        <option value="name">名称</option><option value="newest">最新版本时间</option><option value="size">文件大小</option>
      </select>
      <button type="button" disabled={busy||page.length===0} onClick={()=>setChosen(new Set(page.map(item=>item.assetLinkId)))}>选择本页</button>
      <button type="button" onClick={()=>setChosen(new Set())}>取消选择</button>
    </div>
    {error&&<p role="alert">{error}</p>}
    <div className="dm-asset-list" role="listbox" aria-label="资产文件列表" aria-multiselectable="true"
      onKeyDown={event=>{
        if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='a'){event.preventDefault();setChosen(new Set(page.map(x=>x.assetLinkId)));return;}
        if(!['ArrowDown','ArrowUp'].includes(event.key))return;
        const current=page.findIndex(x=>x.assetLinkId===document.activeElement?.getAttribute('data-link-id'));
        const target=page[Math.max(0,Math.min(page.length-1,current+(event.key==='ArrowDown'?1:-1)))];
        if(target){event.preventDefault();rowRefs.current.get(target.assetLinkId)?.focus();select(target.assetLinkId,event);}
      }}>
      {page.map(item=><div key={item.assetLinkId} role="option" aria-selected={chosen.has(item.assetLinkId)}
        className={chosen.has(item.assetLinkId)?'is-selected':''}>
        <button type="button" data-link-id={item.assetLinkId} ref={node=>{if(node)rowRefs.current.set(item.assetLinkId,node);else rowRefs.current.delete(item.assetLinkId);}}
          aria-label={`选择 ${item.name}`} onClick={event=>select(item.assetLinkId,event)}
          onDoubleClick={()=>void open(item)}>{item.name}</button>
        <span>{item.stage}</span><span>{item.sizeBytes.toLocaleString()} B</span>
        <span>v{item.versionNumber}</span><button type="button" onClick={()=>void open(item)}>详情</button>
      </div>)}
      {!busy&&!error&&!page.length&&<p>该范围尚无已入库文件。</p>}
    </div>
    <footer>
      <button type="button" disabled={!pages.length||busy} onClick={()=>setPages(v=>v.slice(0,-1))}>上一页</button>
      <span>第 {pages.length+1} 页</span>
      <button type="button" disabled={!next||busy} onClick={()=>{if(next)setPages(v=>[...v,next]);}}>下一页</button>
      <small>选择基于 AssetLinkId；此处仅支持本页多选，不会修改文件。</small>
    </footer>
    {detail&&<aside className="dm-asset-detail" aria-label="文件详情"><button type="button" onClick={()=>setDetail(null)}>关闭详情</button>
      <h3>{detail.name}</h3><dl><dt>关联 ID</dt><dd>{detail.assetLinkId}</dd><dt>资产 ID</dt><dd>{detail.assetId}</dd>
      <dt>固定版本 ID</dt><dd>{detail.assetVersionId}</dd><dt>版本</dt><dd>{detail.versionNumber}</dd>
      <dt>阶段</dt><dd>{detail.stage}</dd><dt>大小</dt><dd>{detail.sizeBytes} B</dd>
      <dt>SHA-256</dt><dd>{detail.sha256}</dd><dt>Panel 引用</dt><dd>{detail.referenced?'有':'无'}</dd></dl></aside>}
  </section>;
}
