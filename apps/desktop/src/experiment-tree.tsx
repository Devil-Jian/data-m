import React,{useEffect,useRef,useState} from 'react';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import {experimentApi,experimentMode,safeExperimentError,validateExperimentResponse} from './experiment-tree.ts';
import type {ExperimentApi,ExperimentKind,ExperimentRequest,ExperimentResponse,ExperimentStageId,ExperimentStageTarget} from './experiment-tree.ts';
import {ImportJobsPanel} from './import-jobs.tsx';
import {AssetBrowser} from './asset-browser.tsx';
import {dropAssets,hasAssetDrag,safeTransferError,transferApi} from './asset-transfer.ts';
import type {AssetScope} from './asset-transfer.ts';
import type {ImportDropRegistrar,ImportTarget} from './import-jobs.ts';
import './experiment-tree.css';

const {Button,Input,Badge}=shadcnUI;
const labels:Record<ExperimentKind,string>={group:'分组',replicate:'样本'};
const stageNames:Record<ExperimentStageId,string>={raw:'原始数据',processed:'处理数据',statistics:'统计结果',display:'展示成果'};
interface Selection {kind:ExperimentKind;id:string}
interface Editor {action:'create'|'rename'|'delete';kind:ExperimentKind;name:string;id?:string;parentId?:string;request?:ExperimentRequest}
interface Props {
  assayId:string;assayName:string;api?:ExperimentApi;onClose:()=>void;
  onBusyChange?:(busy:boolean)=>void;onTargetChange?:(target:ExperimentStageTarget|null)=>void;
  registerImportDrop?:ImportDropRegistrar;
}

export function ExperimentTreeManager({assayId,assayName,api=experimentApi,onClose,onBusyChange,onTargetChange,registerImportDrop}:Props){
  const [snapshot,setSnapshot]=useState<ExperimentResponse|null>(null);
  const [selection,setSelection]=useState<Selection|null>(null);
  const [stageId,setStageId]=useState<ExperimentStageId>('raw');
  const [pending,setPending]=useState(false);
  const [error,setError]=useState('');
  const [editor,setEditor]=useState<Editor|null>(null);
  const [importsOpen,setImportsOpen]=useState(false),[importLocked,setImportLocked]=useState(false);
  const [transferNotice,setTransferNotice]=useState('');
  const navigationLocked=pending||importLocked;
  const alive=useRef(true),busy=useRef(false),dialog=useRef<HTMLDialogElement>(null);
  const groups=snapshot?.groups??[];
  const selectedGroup=groups.find(group=>selection?.kind==='group'?group.id===selection.id:group.replicates.some(replicate=>replicate.id===selection?.id));
  const selectedReplicate=selection?.kind==='replicate'?selectedGroup?.replicates.find(replicate=>replicate.id===selection.id):undefined;
  const selectedName=selectedReplicate?.label??selectedGroup?.name;
  const stage=snapshot?.stages.find(item=>item.id===stageId);
  const targetAvailable=Boolean(stage&&(stage.scope==='assay'||selectedReplicate));
  const importTarget:ImportTarget|null=targetAvailable?{assayId,stage:stageId,...(stage?.scope==='replicate'?{groupId:selectedGroup!.id,replicateId:selectedReplicate!.id}:{})}:null;
  const importTargetLabel=`${assayName} / ${stage?.label??stageId}${stage?.scope==='replicate'?` / ${selectedGroup?.name} / ${selectedReplicate?.label}`:''}`;

  async function query(){
    const result=validateExperimentResponse(await api({action:'list',assayId}),assayId);
    if(!alive.current)return;
    setSnapshot(result);
    setSelection(current=>current&&(current.kind==='group'?result.groups.some(group=>group.id===current.id):result.groups.some(group=>group.replicates.some(replicate=>replicate.id===current.id)))?current:null);
  }
  async function reload(){
    if(busy.current)return;
    busy.current=true;setPending(true);setError('');
    try{await query();}catch(reason){if(alive.current)setError(safeExperimentError(reason).message);}
    finally{busy.current=false;if(alive.current)setPending(false);}
  }
  useEffect(()=>{alive.current=true;void reload();return ()=>{alive.current=false;};},[api,assayId]);
  useEffect(()=>{onBusyChange?.(navigationLocked);return ()=>onBusyChange?.(false);},[navigationLocked,onBusyChange]);
  useEffect(()=>{
    if(editor&&!dialog.current?.open)dialog.current?.showModal();
    if(!editor&&dialog.current?.open)dialog.current.close();
  },[editor]);
  useEffect(()=>{
    onTargetChange?.(targetAvailable?{assayId,stageId,...(stage?.scope==='replicate'?{replicateId:selectedReplicate!.id}:{})}:null);
  },[assayId,stageId,stage?.scope,selectedReplicate?.id,targetAvailable,onTargetChange]);

  function allowAssetDrop(event:React.DragEvent<HTMLElement>){
    if(hasAssetDrag()){event.preventDefault();event.dataTransfer.dropEffect=event.ctrlKey?'copy':event.shiftKey?'move':'none';}
  }
  async function acceptAssetDrop(event:React.DragEvent<HTMLElement>,dropStage:ExperimentStageId,groupId?:string,replicateId?:string){
    if(!hasAssetDrag())return;
    event.preventDefault();event.stopPropagation();
    const target:AssetScope={assayId,stage:dropStage,groupId:groupId??null,replicateId:replicateId??null};
    try{
      const result=await dropAssets(event,target,transferApi);
      setTransferNotice((result.action==='copy'?'复制':'移动')+'成功：'+result.links.length+' 条关联已写入 '+dropStage+'。');
      setStageId(dropStage);
      if(replicateId)setSelection({kind:'replicate',id:replicateId});
    }catch(reason){setTransferNotice(safeTransferError(reason).message);}
  }
  function open(value:Editor){setError('');setEditor(value);}
  function closeEditor(){if(!busy.current){setEditor(null);setError('');}}
  async function submit(){
    if(!editor||busy.current)return;
    if(editor.action!=='delete'&&!editor.name.trim()){setError('名称不能为空。');return;}
    // Keep the same idempotency key for an unchanged retry, including when the
    // mutation committed but the following read failed.
    const request=editor.request??{
      action:editor.action,assayId,kind:editor.kind,requestId:crypto.randomUUID(),
      ...(editor.id?{id:editor.id}:{}),...(editor.parentId?{parentId:editor.parentId}:{}),
      ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
    };
    setEditor({...editor,request});busy.current=true;setPending(true);setError('');
    try{
      await api(request);
      // Mutation replies cannot update the tree or n; a fresh list is required.
      await query();
      if(alive.current)setEditor(null);
    }catch(reason){if(alive.current)setError(safeExperimentError(reason).message);}
    finally{busy.current=false;if(alive.current)setPending(false);}
  }

  return <main className="dm-experiment-manager" aria-label="分组与样本工作区" aria-busy={pending}>
    <header className="dm-experiment-header">
      <div><p className="dm-experiment-eyebrow">DATA M / S2</p><h1>分组与样本</h1><p>{assayName}</p></div>
      <div className="dm-experiment-actions"><Badge tone="neutral">{api===experimentApi?experimentMode():'注入 API（测试运行态）'}</Badge>
        <Button disabled={navigationLocked} onClick={onClose}>返回项目管理</Button></div>
    </header>
    <div className="dm-experiment-toolbar">
      <Button variant="primary" disabled={navigationLocked||!snapshot} onClick={()=>open({action:'create',kind:'group',name:''})}>新建分组</Button>
      <Button disabled={navigationLocked||Boolean(editor)} onClick={()=>void reload()}>重新加载</Button>
      {!importsOpen&&<Button disabled={navigationLocked||!snapshot} onClick={()=>setImportsOpen(true)}>安全导入（测试库）</Button>}
      <span role="status" aria-label="实验样本统计">{snapshot?`${groups.length} 个分组 · 生物学重复总数 n = ${snapshot.replicateCount}`:'样本统计尚未加载'}</span>
      {pending&&<span role="status">正在读取或提交…</span>}
      {transferNotice&&<p className="dm-asset-feedback" aria-live="polite">{transferNotice}</p>}
    </div>
    {error&&!editor&&<p role="alert" className="dm-experiment-error">{error}</p>}
    <div className="dm-experiment-layout">
      <aside className="dm-experiment-sidebar" aria-label="分组和样本导航"><h2>生物学重复</h2>
        {snapshot&&groups.length===0&&<p className="dm-experiment-muted">暂无分组。新建分组后添加生物学重复。</p>}
        <ul className="dm-experiment-tree">{groups.map(group=><li key={group.id} aria-label={`${group.name}，n = ${group.replicates.length}`}>
          <div className="dm-experiment-group-row"><button type="button" disabled={navigationLocked} aria-label={`分组：${group.name}`}
            aria-current={selection?.kind==='group'&&selection.id===group.id?'true':undefined} onClick={()=>setSelection({kind:'group',id:group.id})}>{group.name}</button><span>n = {group.replicates.length}</span></div>
          <ul>{group.replicates.map(replicate=><li key={replicate.id}><button type="button" disabled={navigationLocked} aria-label={`样本：${replicate.label}`}
            aria-current={selection?.kind==='replicate'&&selection.id===replicate.id?'true':undefined}
            onDragOver={allowAssetDrop}
            onDrop={event=>{void acceptAssetDrop(event,stageId==='raw'||stageId==='processed'?stageId:'raw',group.id,replicate.id);}}
            onClick={()=>setSelection({kind:'replicate',id:replicate.id})}><span aria-hidden="true">○</span> {replicate.label}</button></li>)}</ul>
        </li>)}</ul>
        <p className="dm-experiment-count-note">n 仅统计生物学重复；文件数量和文件关联不计入 n。</p>
      </aside>
      <div className="dm-experiment-content">
        <section className="dm-experiment-selection" aria-label="分组样本详情">
          {selection&&selectedName?<><Badge tone="blue">{labels[selection.kind]}</Badge><h2>{selectedName}</h2>
            {selectedGroup&&<p>所属分组：{selectedGroup.name} · n = {selectedGroup.replicates.length}</p>}
            <div className="dm-experiment-actions">
              {selection.kind==='group'&&<Button variant="primary" disabled={navigationLocked} onClick={()=>open({action:'create',kind:'replicate',parentId:selection.id,name:''})}>新建样本</Button>}
              <Button disabled={navigationLocked} onClick={()=>open({action:'rename',kind:selection.kind,id:selection.id,name:selectedName})}>重命名{labels[selection.kind]}</Button>
              <Button variant="danger" disabled={navigationLocked} onClick={()=>open({action:'delete',kind:selection.kind,id:selection.id,name:selectedName})}>删除{labels[selection.kind]}</Button>
            </div></>:<><h2>{snapshot?'选择分组或样本':'实验数据尚未加载'}</h2><p>选择分组以管理生物学重复；选择样本以查看其原始和处理数据阶段。</p></>}
        </section>
        <section className="dm-experiment-stages" aria-label="四阶段容器">
          <nav className="dm-experiment-stage-nav" aria-label="数据阶段">{snapshot?.stages.map(item=><button type="button" key={item.id}
            disabled={navigationLocked} aria-current={stageId===item.id?'page':undefined}
            onDragOver={allowAssetDrop}
            onDrop={event=>{void acceptAssetDrop(event,item.id,item.scope==='replicate'?selectedGroup?.id:undefined,item.scope==='replicate'?selectedReplicate?.id:undefined);}}
            onClick={()=>setStageId(item.id)}>
            {item.label} <small>{stageNames[item.id]}</small><span>{item.scope==='replicate'?'样本级':'实验级'}</span>
          </button>)}</nav>
          {stage&&<section className="dm-experiment-stage-body" aria-label="阶段内容" data-stage-id={stage.id} data-scope={stage.scope} data-replicate-id={stage.scope==='replicate'?selectedReplicate?.id:undefined}>
            <Badge tone={stage.scope==='replicate'?'blue':'green'}>{stage.scope==='replicate'?'样本级':'实验级'}</Badge>
            <h2>{stage.label} · {stageNames[stage.id]}</h2>
            {targetAvailable?<><p>当前目标：{stage.scope==='replicate'?`${selectedGroup?.name} / ${selectedReplicate?.label}`:assayName}</p>
              <AssetBrowser query={{assayId,stage:stageId,...(stage.scope==='replicate'?{groupId:selectedGroup!.id,replicateId:selectedReplicate!.id}:{})}}/></>:<div className="dm-experiment-stage-empty"><strong>请先选择一个样本</strong><p>Raw 和 Processed 属于单个生物学重复。</p></div>}
          </section>}
          {importsOpen&&<ImportJobsPanel assayId={assayId} target={importTarget} targetLabel={importTargetLabel}
            onClose={()=>setImportsOpen(false)} onLockChange={setImportLocked} registerImportDrop={registerImportDrop}
            describeTarget={value=>{
              const group=groups.find(item=>item.id===value.groupId||item.replicates.some(replicate=>replicate.id===value.replicateId));
              const replicate=group?.replicates.find(item=>item.id===value.replicateId);
              return `${assayName} / ${value.stage.toUpperCase()}${value.replicateId?` / ${group?.name??'已不可用分组'} / ${replicate?.label??'已不可用样本'}`:''}`;
            }}
          />}
        </section>
      </div>
    </div>
    <dialog ref={dialog} className="dm-experiment-dialog" aria-labelledby="dm-experiment-editor-title" onCancel={event=>{event.preventDefault();closeEditor();}}>
      {editor&&<form onSubmit={event=>{event.preventDefault();void submit();}}>
        <h2 id="dm-experiment-editor-title">{editor.action==='create'?'新建':editor.action==='rename'?'重命名':'删除'}{labels[editor.kind]}</h2>
        {editor.action==='delete'?<p>确认删除「{editor.name}」？有下级或关联资产时将拒绝删除。</p>:<fieldset disabled={pending}>
          <p>名称</p><Input ariaLabel="名称" value={editor.name} placeholder="输入名称" onChange={name=>{setEditor({...editor,name,request:undefined});setError('');}}/>
        </fieldset>}
        {error&&<p role="alert" className="dm-experiment-error">{error}</p>}
        <div className="dm-experiment-dialog-actions"><Button disabled={pending} onClick={closeEditor}>取消</Button>
          <Button variant={editor.action==='delete'?'danger':'primary'} disabled={pending} onClick={()=>void submit()}>{editor.action==='delete'?'确认删除':'保存'}</Button></div>
        {pending&&<p role="status">正在提交并重新查询，请稍候…</p>}
      </form>}
    </dialog>
  </main>;
}
