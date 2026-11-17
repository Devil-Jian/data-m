import React,{useEffect,useState} from 'react';
import {analysisApi,analysisError} from './statistical-analysis.ts';
import type {AnalysisApi,AnalysisResponse,AnalysisRequest} from './statistical-analysis.ts';
import './statistical-analysis.css';

export function StatisticalAnalysisPanel({assayId,assayName,onClose,api=analysisApi}:{
 assayId:string;assayName:string;onClose:()=>void;api?:AnalysisApi
}){
 const [state,setState]=useState<AnalysisResponse|null>(null);
 const [chosen,setChosen]=useState<string|null>(null);
 const [name,setName]=useState(''),[notes,setNotes]=useState('');
 const [selected,setSelected]=useState<string[]>([]);
 const [inputs,setInputs]=useState<string[]>([]),[results,setResults]=useState<string[]>([]);
 const [busy,setBusy]=useState(false),[error,setError]=useState('');
 const [dirty,setDirty]=useState(false);
 function apply(data:AnalysisResponse,id:string|null){
  setState(data);setChosen(id);
  const current=data.analyses.find(a=>a.id===id);
  setName(current?.name??'');setNotes(current?.notes??'');
  setSelected(data.samples.filter(s=>s.included).map(s=>s.replicateId));
  setInputs(data.inputVersionIds);setResults(data.resultVersionIds);setDirty(false);
 }
 async function run(request:AnalysisRequest,id:string|null=null){
  setBusy(true);setError('');
  try{
   const result=await api(request);
   apply(result,id??result.selectedId);
  }catch(reason){setError(analysisError(reason));}
  finally{setBusy(false);}
 }
 useEffect(()=>{
  let live=true;
  void api({action:'list',assayId}).then(data=>{if(live)apply(data,null);})
   .catch(reason=>{if(live)setError(analysisError(reason));});
  return()=>{live=false;};
 },[api,assayId]);
 const active=state?.analyses.find(a=>a.id===chosen);
 const frozen=Boolean(active?.frozenAt);
 const groups=Object.values((state?.samples??[]).reduce<Record<string,{name:string;items:NonNullable<AnalysisResponse['samples']>}>>
  ((acc,item)=>{(acc[item.groupId]??={name:item.groupName,items:[]}).items.push(item);return acc;},{}));
 const toggle=(values:string[],id:string,set:(v:string[])=>void)=>{
  set(values.includes(id)?values.filter(x=>x!==id):[...values,id]);setDirty(true);
 };
 const changes:AnalysisRequest={
  action:chosen?'save':'create',assayId,...(chosen?{analysisId:chosen}:{}),
  name:name.trim(),notes,replicateIds:selected,inputVersionIds:inputs,resultVersionIds:results,
 };
 return <main className="dm-stat" aria-label="S8 统计分析工作台" aria-busy={busy}>
  <header className="dm-stat-header">
   <div><small>DATA M / S8 · 受控仓库</small><h1>{assayName} · 统计分析</h1>
    <p>独立纳入生物学重复，输入与结果绑定固定 AssetVersion。不会执行统计计算。</p></div>
   <button onClick={onClose} disabled={busy}>返回项目管理</button>
  </header>
  {error&&<p role="alert" className="dm-stat-error">{error}</p>}
  <div className="dm-stat-columns">
   <aside aria-label="分析列表"><h2>分析</h2>
    <button disabled={busy} onClick={()=>{if(!state)return;apply({...state,samples:state.samples.map(s=>({...s,included:false})),inputVersionIds:[],resultVersionIds:[]},null);}}>+ 新建分析</button>
    {state?.analyses.map(a=><button key={a.id} className={chosen===a.id?'active':''} disabled={busy}
      onClick={()=>void run({action:'get',assayId,analysisId:a.id},a.id)}>
      <strong>{a.name}</strong><span>{a.includedN} 纳入 · {a.frozenAt?'已冻结':'草稿'}</span>
      <small>{a.updatedAt||'历史分析'}</small></button>)}
   </aside>
   <section aria-label="组与样本选择"><h2>组与生物学重复</h2>
    <label>分析名称<input aria-label="分析名称" value={name} disabled={frozen||busy}
      onChange={e=>{setName(e.target.value);setDirty(true);}}/></label>
    <label>备注<textarea aria-label="分析备注" value={notes} disabled={frozen||busy}
      onChange={e=>{setNotes(e.target.value);setDirty(true);}}/></label>
    {groups.map(g=><fieldset key={g.items[0].groupId} disabled={frozen||busy}>
      <legend>{g.name} · 实验 n={g.items.length} · 本次纳入 n={g.items.filter(x=>selected.includes(x.replicateId)).length} · 未纳入 {g.items.filter(x=>!selected.includes(x.replicateId)).length}</legend>
      {g.items.map(s=><label key={s.replicateId} className="dm-stat-check">
       <input type="checkbox" checked={selected.includes(s.replicateId)}
        onChange={()=>toggle(selected,s.replicateId,setSelected)}/>{s.label}
      </label>)}
     </fieldset>)}
   </section>
   <section aria-label="输入结果版本"><h2>文件版本</h2>
    <p>勾选的版本固定引用，不随文件移动或重命名变化。</p>
    <h3>统计输入</h3>
    {state?.availableVersions.map(v=><label key={'i'+v.id} className="dm-stat-check">
      <input type="checkbox" disabled={frozen||busy} checked={inputs.includes(v.id)}
       onChange={()=>toggle(inputs,v.id,setInputs)}/>
      {v.assetName} · v{v.versionNumber} · {v.id}{v.deleted?'（历史已移除）':''}
    </label>)}
    <h3>统计结果</h3>
    {state?.availableVersions.map(v=><label key={'r'+v.id} className="dm-stat-check">
      <input type="checkbox" disabled={frozen||busy} checked={results.includes(v.id)}
       onChange={()=>toggle(results,v.id,setResults)}/>
      {v.assetName} · v{v.versionNumber} · {v.id}{v.deleted?'（历史已移除）':''}
    </label>)}
   </section>
  </div>
  <footer className="dm-stat-footer">
   <span>{chosen?'分析 ID：'+chosen:'新建分析'} · {frozen?'Frozen':'Draft'} · 纳入 {selected.length} 个样本 {dirty?'· 尚未保存':''}</span>
   <button disabled={busy||frozen||!name.trim()||!state} onClick={()=>void run(changes)}>保存分析</button>
   <button disabled={busy||!chosen||frozen||dirty} onClick={()=>void run({action:'freeze',assayId,analysisId:chosen!},chosen)}>冻结分析</button>
   <button disabled={busy||!chosen||!frozen} onClick={()=>void run({action:'revise',assayId,analysisId:chosen!})}>创建修订</button>
   <button disabled={busy} onClick={()=>void run({action:'list',assayId})}>刷新历史</button>
  </footer>
 </main>;
}
