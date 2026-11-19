import React,{useEffect,useState} from 'react';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import {analysisApi,analysisError} from './statistical-analysis';
import type {AnalysisRequest,AnalysisResponse} from './statistical-analysis';
import './production-analysis.css';

/** Only this native S8 response can establish a replicate's inclusion.
 * Selected draft inclusion and frozen inclusion remain distinguishable. */
export interface AnalysisEvidence{
  analysisId:string;includedReplicateIds:string[];includedN:number;frozenAt:string|null;
}
export function ProductionAnalysisDrawer({assayId,assayName,onClose,onEvidence}:{
  assayId:string;assayName:string;onClose:()=>void;onEvidence:(x:AnalysisEvidence|null)=>void;
}){
  const [state,setState]=useState<AnalysisResponse|null>(null);
  const [chosen,setChosen]=useState<string|null>(null);
  const [name,setName]=useState('');
  const [notes,setNotes]=useState('');
  const [selected,setSelected]=useState<string[]>([]);
  const [inputs,setInputs]=useState<string[]>([]);
  const [results,setResults]=useState<string[]>([]);
  const [busy,setBusy]=useState(false);
  const [error,setError]=useState('');
  const [dirty,setDirty]=useState(false);
  const [tab,setTab]=useState<'samples'|'versions'>('samples');
  function apply(data:AnalysisResponse,id:string|null){
    setState(data);
    const active=data.analyses.find(a=>a.id===id)??null;
    setChosen(active?.id??null);
    setName(active?.name??'');setNotes(active?.notes??'');
    setSelected(data.samples.filter(x=>x.included).map(x=>x.replicateId));
    setInputs(data.inputVersionIds);setResults(data.resultVersionIds);
    setDirty(false);
    onEvidence(active?{
      analysisId:active.id,includedReplicateIds:data.samples.filter(x=>x.included).map(x=>x.replicateId),
      includedN:active.includedN,frozenAt:active.frozenAt
    }:null);
  }
  useEffect(()=>{
    let active=true;
    onEvidence(null);
    void analysisApi({action:'list',assayId}).then(data=>{if(active)apply(data,null)})
      .catch(e=>{if(active)setError(analysisError(e))});
    return ()=>{active=false;onEvidence(null)};
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[assayId]);
  async function run(req:AnalysisRequest,preferred:string|null=chosen){
    if(busy)return;
    setBusy(true);setError('');
    try{
      const data=await analysisApi(req);
      apply(data,preferred??data.selectedId);
    }catch(e){setError(analysisError(e))}
    finally{setBusy(false);}
  }
  const active=state?.analyses.find(a=>a.id===chosen);
  const frozen=Boolean(active?.frozenAt);
  const groups=Object.values((state?.samples??[]).reduce<Record<string,{
    label:string;rows:AnalysisResponse['samples']
  }>>((acc,x)=>{
    (acc[x.groupId]??={label:x.groupName,rows:[]}).rows.push(x);return acc;
  },{}));
  const changeSelected=(v:string)=>{setSelected(old=>old.includes(v)?old.filter(x=>x!==v):[...old,v]);setDirty(true);};
  const changeVersions=(id:string,result:boolean)=>{
    const setter=result?setResults:setInputs;
    setter(old=>old.includes(id)?old.filter(x=>x!==id):[...old,id]);
    setDirty(true);
  };
  const changes:AnalysisRequest={
    action:chosen?'save':'create',assayId,
    ...(chosen?{analysisId:chosen}:{}),name:name.trim(),notes,
    replicateIds:selected,inputVersionIds:inputs,resultVersionIds:results
  };
  return <aside role="dialog" aria-label="真实统计分析" className="dm-real-detail dm-analysis-drawer">
    <button type="button" disabled={busy} onClick={onClose}>关闭 ×</button>
    <h2>{assayName} · 统计分析</h2>
    <p>实验生物学重复 n={state?.samples.length??'…'}；当前选定纳入 n={selected.length}。
      纳入关系与 Figure 代表样本分开维护，只有已冻结分析作为固定证据。</p>
    {error&&<p role="alert" className="dm-paper-error">{error}</p>}
    <div className="dm-analysis-history">
      <UI.Button small variant="secondary" disabled={busy} onClick={()=>{
        if(!state)return;
        apply({...state,samples:state.samples.map(x=>({...x,included:false})),
          inputVersionIds:[],resultVersionIds:[]},null);
      }}>＋ 新建分析</UI.Button>
      {state?.analyses.map(a=><button key={a.id} type="button" disabled={busy}
        className={chosen===a.id?'active':''}
        onClick={()=>void run({action:'get',assayId,analysisId:a.id},a.id)}>
        <strong>{a.name}</strong><small>纳入 {a.includedN} · {a.frozenAt?'已冻结':'草稿'}</small>
      </button>)}
    </div>
    <div className="dm-analysis-fields">
      <label>分析名称<input aria-label="统计分析名称" value={name} disabled={frozen||busy}
        onChange={e=>{setName(e.target.value);setDirty(true);}}/></label>
      <label>备注<textarea aria-label="统计分析备注" value={notes} disabled={frozen||busy}
        onChange={e=>{setNotes(e.target.value);setDirty(true);}}/></label>
      <div className="dm-analysis-tabs">
        <UI.Button small variant={tab==='samples'?'primary':'ghost'} onClick={()=>setTab('samples')}>纳入样本</UI.Button>
        <UI.Button small variant={tab==='versions'?'primary':'ghost'} onClick={()=>setTab('versions')}>输入与结果版本</UI.Button>
      </div>
      {tab==='samples'?groups.map(g=><fieldset key={g.rows[0]?.groupId} disabled={frozen||busy}>
        <legend>{g.label} · 生物学 n={g.rows.length} · 本次纳入 n={g.rows.filter(s=>selected.includes(s.replicateId)).length}</legend>
        {g.rows.map(rep=><label key={rep.replicateId}>
          <input type="checkbox" checked={selected.includes(rep.replicateId)}
            onChange={()=>changeSelected(rep.replicateId)}/>{rep.label}
        </label>)}
      </fieldset>):<div className="dm-analysis-versions">
        <h3>统计输入版本</h3>
        {state?.availableVersions.map(v=><label key={'input'+v.id}>
          <input disabled={busy||frozen} type="checkbox" checked={inputs.includes(v.id)}
            onChange={()=>changeVersions(v.id,false)}/>{v.assetName} · v{v.versionNumber} {v.deleted?'（历史记录）':''}
        </label>)}
        <h3>统计结果版本</h3>
        {state?.availableVersions.map(v=><label key={'output'+v.id}>
          <input disabled={busy||frozen} type="checkbox" checked={results.includes(v.id)}
            onChange={()=>changeVersions(v.id,true)}/>{v.assetName} · v{v.versionNumber} {v.deleted?'（历史记录）':''}
        </label>)}
      </div>}
    </div>
    <div className="dm-analysis-footer">
      <UI.Button variant="primary" disabled={busy||frozen||!name.trim()||!state}
        onClick={()=>void run(changes)}>保存分析</UI.Button>
      <UI.Button disabled={!chosen||busy||frozen||dirty}
        onClick={()=>{if(window.confirm('冻结后不能直接修改。确认将当前纳入样本和固定版本写入不可变分析快照？'))
          void run({action:'freeze',assayId,analysisId:chosen!},chosen);}}>冻结</UI.Button>
      <UI.Button disabled={!chosen||busy||!frozen} onClick={()=>void run({
        action:'revise',assayId,analysisId:chosen!
      },null)}>创建修订</UI.Button>
      {dirty&&<span role="status">尚未保存；此处不构成统计纳入证据。</span>}
    </div>
  </aside>;
}
