import React,{useEffect,useRef,useState} from 'react';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import {lifecycleApi,lifecycleError,recycleApi} from './asset-lifecycle';
import type {RecycledRow} from './asset-lifecycle';
import type {ProductionStage} from './production-adapter';

const stages:ProductionStage[]=['raw','processed','statistics','display'];
/** S6 recovery preserves the original AssetLink and never deletes CAS bytes. */
export function ProductionRecycleDrawer({assayId,groupId,onClose,onRecovered}:{
  assayId:string;groupId:string;onClose:()=>void;onRecovered:()=>void;
}){
  const [stage,setStage]=useState<ProductionStage>('raw');
  const [rows,setRows]=useState<RecycledRow[]>([]);
  const [cursor,setCursor]=useState<string|null>(null);
  const [total,setTotal]=useState(0);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const version=useRef(0);
  async function load(next:string|null=null){
    if(!assayId||busy)return;
    const token=version.current;
    setBusy(true);setError('');
    try{
      const page=await recycleApi({assayId,stage,limit:100,
        ...(stage==='raw'||stage==='processed'?{groupId}:{}),
        ...(next?{cursor:next}:{})});
      if(token!==version.current)return;
      setRows(prev=>next?[...prev,...page.items]:page.items);
      setTotal(page.total);setCursor(page.nextCursor);
    }catch(e){if(token===version.current)setError(lifecycleError(e).message);}
    finally{if(token===version.current)setBusy(false);}
  }
  useEffect(()=>{
    version.current++;setRows([]);setCursor(null);setTotal(0);setError('');
    const token=version.current;
    void recycleApi({assayId,stage,limit:100,
      ...(stage==='raw'||stage==='processed'?{groupId}:{})})
      .then(page=>{if(token===version.current){setRows(page.items);setTotal(page.total);setCursor(page.nextCursor)}})
      .catch(e=>{if(token===version.current)setError(lifecycleError(e).message)});
    return ()=>{version.current++;};
  },[assayId,groupId,stage]);
  async function restore(row:RecycledRow){
    if(busy||!row.canRestore)return;
    if(!window.confirm('恢复原位置的文件关联 '+row.name+'？不改写 CAS Blob。'))return;
    setBusy(true);setError('');
    const token=version.current;
    try{
      await lifecycleApi({action:'restore',requestId:crypto.randomUUID(),items:[{
        assetLinkId:row.assetLinkId,assetId:row.assetId,assetVersionId:row.assetVersionId,
        assayId:row.assayId,stage:row.stage,groupId:row.groupId,replicateId:row.replicateId
      }]});
      if(token===version.current){
        onRecovered();
        const page=await recycleApi({assayId,stage,limit:100,
          ...(stage==='raw'||stage==='processed'?{groupId}:{})});
        if(token===version.current){setRows(page.items);setTotal(page.total);setCursor(page.nextCursor);}
      }
    }catch(e){if(token===version.current)setError(lifecycleError(e).message)}
    finally{if(token===version.current)setBusy(false);}
  }
  return <aside role="dialog" aria-label="真实资产回收区" className="dm-real-detail dm-real-recycle">
    <button type="button" disabled={busy} onClick={onClose}>关闭 ×</button>
    <h2>资产关联回收区</h2>
    <p>回收与恢复仅变更逻辑关联，不直接删除不可变 CAS 原始字节；论文最后引用受保护。</p>
    <UI.Select ariaLabel="回收区阶段" value={stage} onChange={setStage as (value:string)=>void}
      options={stages.map(id=>({value:id,label:id}))}/>
    <p role="status">查询结果：{rows.length} / {total} 条历史关联</p>
    {error&&<p role="alert">{error}</p>}
    {rows.map(row=><article key={row.assetLinkId} className="dm-paper-reference">
      <strong>{row.name}</strong>
      <small>{row.stage} · 原 Link {row.assetLinkId} · {row.deletedAt}</small>
      <code>{row.assetVersionId}</code>
      <UI.Button small variant="secondary" disabled={busy||!row.canRestore}
        onClick={()=>void restore(row)}>恢复到原位置</UI.Button>
      {!row.canRestore&&<small>{row.restoreBlockReason||'原目标已失效或存在冲突'}</small>}
    </article>)}
    {cursor&&<UI.Button small disabled={busy} onClick={()=>void load(cursor)}>加载后续 100 条</UI.Button>}
    {!busy&&!rows.length&&<p>该阶段暂无已回收资产。</p>}
  </aside>;
}
