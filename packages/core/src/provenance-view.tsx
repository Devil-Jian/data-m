import React,{useEffect,useState} from 'react';
import {invoke} from '@tauri-apps/api/core';

type Source={linkId:string;assayId:string;groupId:string|null;replicateId:string|null;
 stage:string;displayName:string;deleted:boolean};
type Edge = {
  paperId:string|null;figureId:string|null;panelId:string|null;usage:string|null;
  analysisId:string|null;analysisRole:string|null;frozenAt:string|null;
  includedReplicateIds:string[];assetId:string;assetVersionId:string;
  versionNumber:number;blobSha256:string;sizeBytes:number;assetDeleted:boolean;
  sources?:Source[];representativeReplicateIds?:string[];
};
type Response={edges:Edge[];totalCount:number;offset:number;limit:number;truncated:boolean};
type Action='panel'|'asset'|'version';
const pageSize=50;
function describeFailure(e:unknown):string {
 if(e&&typeof e==='object'&&'message' in e)return String((e as {message:unknown}).message);
 return String(e);
}
export function ProvenanceView({projectId,action,id}:{projectId:string;action:Action;id:string}) {
 const [result,setResult]=useState<Response|null>(null);
 const [offset,setOffset]=useState(0);
 const [error,setError]=useState('');
 const [busy,setBusy]=useState(false);
 useEffect(()=>{setOffset(0);setResult(null);setError('');},[projectId,action,id]);
 useEffect(()=>{
  let live=true;
  if(!projectId||!id)return;
  setBusy(true);setError('');setResult(null);
  void invoke<Response>('provenance_trace',{request:{
   projectId,action,id,offset,limit:pageSize,
  }}).then(data=>{if(live)setResult(data);})
    .catch(e=>{if(live)setError(describeFailure(e));})
    .finally(()=>{if(live)setBusy(false);});
  return()=>{live=false;};
 },[projectId,action,id,offset]);
 return <section aria-label="S10 版本溯源" className="s9-asset">
  <h4>{action==='panel'?'Panel 固定版本来源':'精确版本反向引用'}</h4>
  <p className="s9-muted">仅显示数据库中可验证的固定版本关系；相同文件名或同实验不等于统计分析关联。</p>
  {busy&&<p role="status">正在查询真实数据库…</p>}
  {error&&<p role="alert" className="s9-error">{error}</p>}
  {result&&<>
   <p role="status">共 {result.totalCount} 条明确引用 · 第 {result.offset+1} 条起
     {result.truncated?' · 还有后续结果':''}</p>
   {!result.edges.length&&<p>尚无已证实的版本引用。</p>}
   {result.edges.map((edge,i)=><article key={edge.assetVersionId+edge.panelId+edge.analysisId+edge.usage+edge.analysisRole+i}>
    <p><strong>v{edge.versionNumber}</strong> · {edge.assetVersionId}</p>
    <small>Asset：{edge.assetId} · {edge.sizeBytes} bytes{edge.assetDeleted?' · 历史已删除':''}</small>
    <code title={edge.blobSha256}>SHA-256：{edge.blobSha256}</code>
    {edge.panelId&&<p>论文 {edge.paperId} / Figure {edge.figureId} / Panel {edge.panelId} · {edge.usage}</p>}
    <details><summary>来源位置与历史状态（{(edge.sources??[]).length} 条）</summary>
     {(edge.sources??[]).length===0?<p>未找到属于当前项目的 AssetLink 来源。</p>:
       (edge.sources??[]).map(s=><p key={s.linkId}>
         实验 {s.assayId} / 分组 {s.groupId||'未指定'} / Replicate {s.replicateId||'未指定'} · {s.stage}
         {s.deleted?' · 历史链接已移除':' · 当前链接'} · {s.displayName}
       </p>)}
     {!!edge.representativeReplicateIds?.length&&
       <p>该 Panel 明确设定的代表样本：{edge.representativeReplicateIds.join(', ')}</p>}
    </details>
    {edge.analysisId&&<p>分析 {edge.analysisId} · {edge.analysisRole} · {edge.frozenAt?'冻结 '+edge.frozenAt:'未冻结'}
      · 历史纳入 n={edge.includedReplicateIds.length}</p>}
    {edge.analysisId&&<details><summary>查看分析当次纳入的 Replicate ID</summary>
      <code>{edge.includedReplicateIds.join(', ')||'当次没有纳入样本'}</code></details>}
   </article>)}
   <div className="s9-actions">
    <button disabled={busy||offset===0} onClick={()=>setOffset(v=>Math.max(0,v-pageSize))}>上一页</button>
    <button disabled={busy||!result.truncated} onClick={()=>setOffset(v=>v+pageSize)}>下一页</button>
   </div>
  </>}
 </section>;
}
