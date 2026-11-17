import React,{useEffect,useRef,useState} from 'react';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import {projectTreeApi,projectTreeMode,safeProjectTreeError,validateProjectTreeResponse} from './project-tree.ts';
import type {ProjectTreeApi,ProjectTreeKind,ProjectTreeNode,ProjectTreeRequest} from './project-tree.ts';
import {ExperimentTreeManager} from './experiment-tree.tsx';
import {RepositorySafetyPanel} from './repository-safety.tsx';
import {StatisticalAnalysisPanel} from './statistical-analysis.tsx';
import type {ImportDropRegistrar} from './import-jobs.ts';
import './project-tree.css';

const {Button,Input,Badge}=shadcnUI;
const labels:Record<ProjectTreeKind,string>={project:'项目',theme:'主题',assay:'实验'};
interface Editor {action:'create'|'rename'|'delete';kind:ProjectTreeKind;target?:ProjectTreeNode;parentId?:string;name:string;request?:ProjectTreeRequest}

export function ProjectTreeManager({api=projectTreeApi,onClose,onBusyChange,registerImportDrop}:{api?:ProjectTreeApi;onClose?:()=>void;onBusyChange?:(busy:boolean)=>void;registerImportDrop?:ImportDropRegistrar}){
  const [nodes,setNodes]=useState<ProjectTreeNode[]>([]);
  const [selectedId,setSelectedId]=useState<string|null>(null);
  const [loaded,setLoaded]=useState(false);
  const [pending,setPending]=useState(false);
  const [error,setError]=useState('');
  const [editor,setEditor]=useState<Editor|null>(null);
  const [experiment,setExperiment]=useState<{id:string;name:string}|null>(null);
  const [safetyOpen,setSafetyOpen]=useState(false);
  const [statistics,setStatistics]=useState<{id:string;name:string}|null>(null);
  const alive=useRef(true);
  const busy=useRef(false);
  const dialog=useRef<HTMLDialogElement>(null);
  const selected=nodes.find(node=>node.id===selectedId);
  const childKind:ProjectTreeKind|undefined=selected?.kind==='project'?'theme':selected?.kind==='theme'?'assay':undefined;

  async function query(){
    const snapshot=validateProjectTreeResponse(await api({action:'list'}));
    if(!alive.current)return;
    setNodes(snapshot.nodes);
    setLoaded(true);
    setSelectedId(id=>snapshot.nodes.some(node=>node.id===id)?id:null);
  }
  async function reload(){
    if(busy.current)return;
    busy.current=true;setPending(true);setError('');
    try{await query();}
    catch(reason){if(alive.current)setError(safeProjectTreeError(reason).message);}
    finally{busy.current=false;if(alive.current)setPending(false);}
  }
  useEffect(()=>{
    alive.current=true;
    void reload();
    return ()=>{alive.current=false;};
  },[api]);
  useEffect(()=>{
    if(editor&&!dialog.current?.open)dialog.current?.showModal();
    if(!editor&&dialog.current?.open)dialog.current.close();
  },[editor]);
  useEffect(()=>{onBusyChange?.(pending);return ()=>onBusyChange?.(false);},[pending,onBusyChange]);

  function open(value:Editor){setError('');setEditor(value);}
  function closeEditor(){if(!busy.current){setEditor(null);setError('');}}
  async function submit(){
    if(!editor||busy.current)return;
    if(editor.action!=='delete'&&!editor.name.trim()){setError('名称不能为空。');return;}
    // Retry an unchanged operation using the same idempotency key. Changing the
    // form or opening a fresh operation always allocates a new request ID.
    const request=editor.request??{
      action:editor.action,kind:editor.kind,requestId:crypto.randomUUID(),
      ...(editor.target?{id:editor.target.id}:{}),
      ...(editor.parentId?{parentId:editor.parentId}:{}),
      ...(editor.action!=='delete'?{name:editor.name.trim()}:{}),
    };
    setEditor({...editor,request});
    busy.current=true;setPending(true);setError('');
    try{
      await api(request);
      // A mutation's reply is never used as display data. Only a fresh list can
      // replace the last confirmed tree, including after an uncertain failure.
      await query();
      if(alive.current)setEditor(null);
    }catch(reason){if(alive.current)setError(safeProjectTreeError(reason).message);}
    finally{busy.current=false;if(alive.current)setPending(false);}
  }

  function branch(parentId:string|null):React.ReactNode{
    return nodes.filter(node=>node.parentId===parentId).map(node=><li key={node.id}>
      <button type="button" className="dm-project-node" aria-label={`${labels[node.kind]}：${node.name}`}
        aria-current={selectedId===node.id?'true':undefined} disabled={pending}
        onClick={()=>setSelectedId(node.id)}>
        <span aria-hidden="true" className={`dm-project-kind ${node.kind}`}>{node.kind==='project'?'P':node.kind==='theme'?'T':'A'}</span>
        <span>{node.name}</span>
      </button>
      {nodes.some(child=>child.parentId===node.id)&&<ul>{branch(node.id)}</ul>}
    </li>);
  }

  if(statistics)return <StatisticalAnalysisPanel key={statistics.id} assayId={statistics.id} assayName={statistics.name} onClose={()=>setStatistics(null)}/>;
  if(safetyOpen)return <RepositorySafetyPanel onClose={()=>setSafetyOpen(false)}/>;
  if(experiment)return <ExperimentTreeManager key={experiment.id} assayId={experiment.id} assayName={experiment.name}
    onClose={()=>setExperiment(null)} onBusyChange={onBusyChange} registerImportDrop={registerImportDrop}/>;

  return <main className="dm-project-manager" aria-label="项目管理工作区" aria-busy={pending}>
    <header className="dm-project-header">
      <div><p className="dm-project-eyebrow">DATA M / S1</p><h1>项目管理</h1><p>项目 → 主题 → 实验</p></div>
      <div className="dm-project-header-actions"><Badge tone="neutral">{api===projectTreeApi?projectTreeMode():'注入 API（测试运行态）'}</Badge>
        {onClose&&<Button disabled={pending} onClick={onClose}>返回工作台</Button>}
      </div>
    </header>
    <div className="dm-project-toolbar">
      <Button variant="primary" disabled={pending||!loaded} onClick={()=>open({action:'create',kind:'project',name:''})}>新建项目</Button>
      <Button disabled={pending||Boolean(editor)} onClick={()=>void reload()}>重新加载</Button>
      <Button disabled={pending||Boolean(editor)} onClick={()=>setSafetyOpen(true)}>数据完整性与备份恢复 (S7)</Button>
      <span role="status">{pending?'正在读取或提交项目数据…':loaded?`${nodes.filter(node=>node.kind==='project').length} 个项目 · ${nodes.filter(node=>node.kind==='theme').length} 个主题 · ${nodes.filter(node=>node.kind==='assay').length} 个实验`:''}</span>
    </div>
    {error&&!editor&&<p className="dm-project-error" role="alert">{error}</p>}
    <div className="dm-project-layout">
      <aside className="dm-project-sidebar" aria-label="项目层级导航"><h2>项目树</h2>
        {loaded&&nodes.length===0?<p className="dm-project-empty">暂无项目。新建项目以开始。</p>:<nav aria-label="项目主题实验"><ul className="dm-project-tree">{branch(null)}</ul></nav>}
      </aside>
      <section className="dm-project-details" aria-label="节点详情">
        {selected?<>
          <Badge tone="blue">{labels[selected.kind]}</Badge><h2>{selected.name}</h2>
          <dl><div><dt>所属位置</dt><dd>{selected.parentId?nodes.find(node=>node.id===selected.parentId)?.name:'项目根目录'}</dd></div>
            <div><dt>直接下级</dt><dd>{nodes.filter(node=>node.parentId===selected.id).length} 项</dd></div></dl>
          <div className="dm-project-detail-actions">
            {selected.kind==='assay'&&<Button variant="primary" disabled={pending} onClick={()=>setExperiment({id:selected.id,name:selected.name})}>分组与样本</Button>}
            {selected.kind==='assay'&&<Button disabled={pending} onClick={()=>setStatistics({id:selected.id,name:selected.name})}>统计分析 (S8)</Button>}
            {childKind&&<Button variant="primary" disabled={pending} onClick={()=>open({action:'create',kind:childKind,parentId:selected.id,name:''})}>新建{labels[childKind]}</Button>}
            <Button disabled={pending} onClick={()=>open({action:'rename',kind:selected.kind,target:selected,name:selected.name})}>重命名</Button>
            <Button variant="danger" disabled={pending} onClick={()=>open({action:'delete',kind:selected.kind,target:selected,name:selected.name})}>删除</Button>
          </div>
          <p className="dm-project-note">删除前会检查下级和关联资产。操作完成后重新读取项目存储。</p>
        </>:<div className="dm-project-placeholder"><h2>{loaded?'选择项目、主题或实验':'项目数据尚未加载'}</h2><p>{loaded?'从左侧选择节点以查看详情和管理下级。':'请等待加载，或在出现错误后重新加载。'}</p></div>}
      </section>
    </div>
    <dialog ref={dialog} className="dm-project-dialog" aria-labelledby="dm-project-editor-title"
      onCancel={event=>{event.preventDefault();closeEditor();}}>
      {editor&&<form onSubmit={event=>{event.preventDefault();void submit();}}>
        <h2 id="dm-project-editor-title">{editor.action==='create'?'新建':editor.action==='rename'?'重命名':'删除'}{labels[editor.kind]}</h2>
        {editor.action==='delete'?<p>确认删除「{editor.name}」？有下级或关联资产时将拒绝删除。</p>:<fieldset disabled={pending}>
          <label htmlFor="dm-project-name">名称</label><Input value={editor.name} ariaLabel="名称" placeholder="输入名称"
            onChange={name=>{setEditor({...editor,name,request:undefined});setError('');}}/>
        </fieldset>}
        {error&&<p className="dm-project-error" role="alert">{error}</p>}
        <div className="dm-project-dialog-actions"><Button disabled={pending} onClick={closeEditor}>取消</Button>
          <Button variant={editor.action==='delete'?'danger':'primary'} disabled={pending} onClick={()=>void submit()}>{editor.action==='delete'?'确认删除':'保存'}</Button>
        </div>
        {pending&&<p role="status">正在提交并重新查询，请稍候…</p>}
      </form>}
    </dialog>
  </main>;
}
