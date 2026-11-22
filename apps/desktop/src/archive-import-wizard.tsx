import React,{useCallback,useEffect,useRef,useState} from 'react';
import {shadcnUI as UI} from '@data-m/ui/workbench-ui';
import type {ImportDropRegistrar,ImportStage} from './import-jobs';
import {archiveApi,archiveError,pickArchiveFolder,reportArchive} from './archive-import';
import type {ArchiveApi,ArchiveDecision,ArchiveEntry,ArchiveLayout,ArchiveRequest,ArchiveTask,ArchiveTemplate} from './archive-import';
import {runArchiveTask} from './archive-import-runner';
import type {ArchiveProgress} from './archive-import-runner';
import './archive-import-wizard.css';

const layouts:{value:ArchiveLayout;label:string}[]=[
  {value:'project_theme_assay_group',label:'项目 → 主题 → 实验 → 分组'},
  {value:'project_exp_data_experiment_assay_group',label:'项目 → exp_data → 实验1/2 → ROS/WB/CV → 分组 → 数据阶段'},
  {value:'theme_assay_group',label:'主题 → 实验 → 分组（合并现有项目）'},
  {value:'assay_group',label:'实验 → 分组（指定主题）'}
];
const stages:{value:string;label:string}[]=[
  {value:'__choose__',label:'请选择文件阶段'},
  {value:'raw',label:'Raw · 原始数据'},
  {value:'processed',label:'Processed · 处理文件'},
  {value:'statistics',label:'Statistics · 实验级统计'},
  {value:'display',label:'Display · 实验级展示'},
  {value:'paper',label:'Paper · 项目论文原件'},
  {value:'figure',label:'Figure · 项目组图原件'}
];
interface Editable{
  id:string;projectName:string;themeName:string;assayName:string;groupName:string;
  replicateName:string;stage:ImportStage|'__choose__';alias:string;decision:ArchiveDecision;
}
function asEdit(e:ArchiveEntry):Editable{return{
  id:e.id,projectName:e.projectName,themeName:e.themeName,assayName:e.assayName,
  groupName:e.groupName,replicateName:e.replicateName,
  stage:e.stage==='review'?'__choose__':e.stage,alias:e.alias,
  decision:e.decision==='review'?'import':e.decision
}}
function exportReport(task:ArchiveTask,entries:ArchiveEntry[]){
  const blob=new Blob([JSON.stringify(reportArchive(task,entries),null,2)],
    {type:'application/json;charset=utf-8'});
  const url=URL.createObjectURL(blob);
  const a=document.createElement('a');
  a.href=url;a.download='data-m-s13-'+task.id+'-report.json';
  document.body.append(a);a.click();a.remove();URL.revokeObjectURL(url);
}
export interface ArchiveWizardProps{
  registerImportDrop?:ImportDropRegistrar;
  projects:{id:string;name:string}[];
  onClose:()=>void;onImported:(task:ArchiveTask)=>void;
  api?:ArchiveApi;
}
export function ArchiveImportWizard({registerImportDrop,projects,onClose,onImported,api=archiveApi}:ArchiveWizardProps){
  const [task,setTask]=useState<ArchiveTask|null>(null);
  const [templates,setTemplates]=useState<ArchiveTemplate[]>([]);
  const [history,setHistory]=useState<ArchiveTask[]>([]);
  const [layout,setLayout]=useState<ArchiveLayout>('project_exp_data_experiment_assay_group');
  const [projectId,setProjectId]=useState('');
  const [themeName,setThemeName]=useState('');
  const [fallbackGroup,setFallbackGroup]=useState('');
  const [templateName,setTemplateName]=useState('');
  const [busy,setBusy]=useState(false);
  const [running,setRunning]=useState(false);
  const [error,setError]=useState('');
  const [edit,setEdit]=useState<Editable|null>(null);
  const [entries,setEntries]=useState<ArchiveEntry[]>([]);
  const [reviewOnly,setReviewOnly]=useState(true);
  const [checked,setChecked]=useState<string[]>([]);
  const [bulkStage,setBulkStage]=useState<ImportStage|'__choose__'>('__choose__');
  const [bulkGroup,setBulkGroup]=useState('');
  const [bulkReplicate,setBulkReplicate]=useState('');
  const [bulkDecision,setBulkDecision]=useState<ArchiveDecision>('import');
  const [progress,setProgress]=useState<ArchiveProgress|null>(null);
  const controller=useRef<AbortController|null>(null);
  const mounted=useRef(true);
  function apply(response:{current:ArchiveTask|null;tasks:ArchiveTask[];templates:ArchiveTemplate[]},replace=false){
    setHistory(response.tasks);setTemplates(response.templates);
    if(response.current){
      setTask(response.current);
      setEntries(old=>replace?response.current!.entries:
        [...old.filter(e=>!response.current!.entries.some(next=>next.id===e.id)),
          ...response.current!.entries]);
    }
  }
  useEffect(()=>{
    mounted.current=true;
    void api({action:'list'}).then(response=>{
      if(mounted.current){setHistory(response.tasks);setTemplates(response.templates);}
    }).catch(err=>{if(mounted.current)setError(archiveError(err))});
    return ()=>{mounted.current=false;controller.current?.abort()};
  },[api]);
  const scanPath=useCallback(async(path:string)=>{
    if(busy||running)return;
    setBusy(true);setError('');setProgress(null);setChecked([]);setEdit(null);
    try{
      const response=await api({action:'scan',rootPath:path,layout,targetProjectId:projectId||undefined,
        targetThemeName:themeName,fallbackGroup});
      if(mounted.current)apply(response,true);
    }catch(e){if(mounted.current)setError(archiveError(e))}
    finally{if(mounted.current)setBusy(false);}
  },[api,busy,running,layout,projectId,themeName,fallbackGroup]);
  useEffect(()=>{
    if(!registerImportDrop)return;
    registerImportDrop(paths=>{
      if(paths.length!==1){setError('请选择一个项目根文件夹，不支持一次拖入多个目录。');return;}
      void scanPath(paths[0]);
    });
    return ()=>registerImportDrop(null);
  },[registerImportDrop,scanPath]);
  async function chooseFolder(){
    setError('');
    try{
      const path=await pickArchiveFolder();
      if(path)await scanPath(path);
    }catch(e){setError(archiveError(e))}
  }
  async function reload(id:string,replace=true){
    const response=await api({action:'get',taskId:id});
    if(mounted.current){apply(response,replace);setChecked([]);setEdit(null);}
  }
  async function loadMore(){
    if(!task?.nextOffset)return;
    setBusy(true);setError('');
    try{apply(await api({action:'get',taskId:task.id,offset:task.nextOffset}),false)}
    catch(e){setError(archiveError(e))}
    finally{setBusy(false)}
  }
  async function send(req:ArchiveRequest,reset=false){
    setBusy(true);setError('');
    try{
      const response=await api(req);
      if(mounted.current)apply(response,reset);
      return response.current;
    }catch(e){if(mounted.current)setError(archiveError(e));return null}
    finally{if(mounted.current)setBusy(false)}
  }
  async function saveEntry(){
    if(!task||!edit)return;
    if(edit.decision!=='skip'&&edit.stage==='__choose__'){
      setError('请选择明确阶段；未识别的文件不能直接导入。');return;
    }
    const {id:_ignored,...values}=edit;
    const result=await send({action:'edit',taskId:task.id,entryId:edit.id,
      ...values,stage:edit.stage==='__choose__'?undefined:edit.stage},false);
    if(result)setEdit(null);
  }
  async function applyBulk(){
    if(!task||!checked.length)return;
    if(bulkDecision!=='skip'&&bulkStage==='__choose__'&&!bulkGroup&&!bulkReplicate){
      setError('请指定批量文件阶段、分组、样本或明确跳过。');return;
    }
    setBusy(true);setError('');
    try{
      for(const id of checked){
        await api({action:'edit',taskId:task.id,entryId:id,decision:bulkDecision,
          ...(bulkStage!=='__choose__'?{stage:bulkStage}:{}),
          ...(bulkGroup.trim()?{groupName:bulkGroup.trim()}:{}),
          ...(bulkReplicate.trim()?{replicateName:bulkReplicate.trim()}:{}),
        });
      }
      await reload(task.id);
    }catch(e){setError(archiveError(e))}
    finally{setBusy(false)}
  }
  async function preflight(){
    if(task)await send({action:'prepare',taskId:task.id},true);
  }
  async function start(){
    if(!task||running)return;
    setRunning(true);setError('');setProgress(null);
    const abort=new AbortController();controller.current=abort;
    try{
      const result=await runArchiveTask(task,setProgress,abort.signal);
      if(mounted.current){await reload(result.id);onImported(result);}
    }catch(e){
      if(mounted.current){setError(archiveError(e));await reload(task.id).catch(()=>{});}
    }finally{controller.current=null;if(mounted.current)setRunning(false);}
  }
  async function pause(){
    controller.current?.abort();
    if(task)await send({action:'pause',taskId:task.id},true);
    setRunning(false);
  }
  async function saveTemplate(){
    if(templateName.trim())await send({action:'templateSave',
      templateName:templateName.trim(),layout,targetThemeName:themeName,fallbackGroup});
  }
  async function exportAll(){
    if(!task)return;
    setBusy(true);setError('');
    try{
      const all:ArchiveEntry[]=[];let offset=0;
      do{
        const response=(await api({action:'get',taskId:task.id,offset})).current;
        if(!response)throw Error('报告来源任务已不存在');
        all.push(...response.entries);
        if(response.nextOffset===null)break;
        offset=response.nextOffset;
      }while(offset<=task.total);
      if(all.length!==task.total)throw Error('报告条目不完整，拒绝生成');
      exportReport(task,all);
    }catch(e){setError(archiveError(e))}
    finally{setBusy(false)}
  }
  const visible=entries.filter(e=>!reviewOnly||e.decision==='review'||e.status==='failed');
  const ready=task?.status==='prepared'&&task.review===0;
  return <div className="dm-s13-cover" role="presentation">
    <section className="dm-s13-wizard" role="dialog" aria-modal="true" aria-label="智能归档文件夹导入">
      <header className="dm-s13-header">
        <div><h2>导入已有科研归档文件夹</h2>
          <p>自动建立项目、主题、实验、分组和生物学重复；不移动或修改 Windows 原始文件。</p>
        </div>
        <UI.Button small variant="ghost" onClick={()=>{if(running)void pause();onClose()}}>关闭 ×</UI.Button>
      </header>
      <div className="dm-s13-steps">
        {['① 选择目录','② 识别与校对','③ 安全导入'].map((label,index)=>
          <span key={label} className={(index===0&&!task)||(index===1&&task?.status==='scanned')||
            (index===2&&['prepared','running','paused','failed','completed'].includes(task?.status??''))
            ?'current':''}>{label}</span>)}
      </div>
      {error&&<p role="alert" className="dm-s13-error">{error}</p>}
      <div className="dm-s13-main">
        <section className="dm-s13-config" aria-label="目录识别配置">
          <h3>选择科研根目录</h3>
          <div className="dm-s13-drop">
            <strong>拖入完整科研项目文件夹</strong>
            <p>也可以使用 Windows 文件夹选择器；扫描不会开始实际导入。</p>
            <UI.Button disabled={busy||running} variant="primary" onClick={()=>void chooseFolder()}>
              选择本地文件夹
            </UI.Button>
          </div>
          <label>目录层级规则
            <UI.Select ariaLabel="导入目录层级规则" value={layout}
              options={layouts} onChange={v=>setLayout(v as ArchiveLayout)}/>
          </label>
          <label>项目目标
            <UI.Select ariaLabel="归档项目目标" value={projectId||'__new__'}
              options={[{value:'__new__',label:'按根目录创建新 Project'},
                ...projects.map(p=>({value:p.id,label:'合并：'+p.name}))]}
              onChange={v=>setProjectId(v==='__new__'?'':v)}/>
          </label>
          {layout==='assay_group'&&<label>所属主题
            <UI.Input value={themeName} onChange={setThemeName} ariaLabel="归档所属主题"
              placeholder="必须填写主题名称"/>
          </label>}
          <label>缺少分组目录时的明确默认分组（可留空）
            <UI.Input value={fallbackGroup} onChange={setFallbackGroup}
              ariaLabel="缺省分组名称" placeholder="留空：标记待确认"/>
          </label>
          <div className="dm-s13-templates">
            <label>载入识别模板
              <UI.Select ariaLabel="载入识别模板" value="__none__"
                options={[{value:'__none__',label:'选择已有模板'},
                  ...templates.map(t=>({value:t.name,label:t.name}))]}
                onChange={value=>{
                  const t=templates.find(x=>x.name===value);
                  if(t){setLayout(t.layout);setThemeName(t.targetThemeName);setFallbackGroup(t.fallbackGroup);}
                }}/>
            </label>
            <UI.Input ariaLabel="新模板名称" value={templateName}
              onChange={setTemplateName} placeholder="模板名称"/>
            <UI.Button small disabled={!templateName.trim()||busy||running}
              onClick={()=>void saveTemplate()}>保存模板</UI.Button>
          </div>
          <h3>最近的可恢复任务</h3>
          <div className="dm-s13-history">
            {history.slice(0,8).map(t=><button key={t.id} type="button"
              className={task?.id===t.id?'active':''} disabled={busy||running}
              onClick={()=>void reload(t.id)}>
              <strong>{t.rootName}</strong><small>{t.status} · {t.imported}/{t.total} 个验证文件</small>
            </button>)}
            {!history.length&&<p>暂无历史归档任务。</p>}
          </div>
          <p className="dm-s13-warning">源文件仅只读；任务记录保存在本机 S13 队列。请另行保持原始文件备份。</p>
        </section>
        <section className="dm-s13-preview" aria-label="科研目录结构识别预览">
          {!task?<div className="dm-s13-empty">
            <h3>等待扫描项目文件夹</h3>
            <p>自动提取目录结构、ROS01 等样本前缀，以及 raw / process / show / statistics 阶段。</p>
            <p>S14 根据父目录识别 Raw/Processed/Statistics/Display；paper/figure 原件作为项目级 CAS 资产导入，不自动关联 Panel 或 ShowData。</p>
          </div>:<>
            <div className="dm-s13-summary">
              <h3>{task.rootName}</h3>
              <small>任务 ID：{task.id}</small>
              <p role="status">{task.total} 个文件 · {task.imported} 已验证 · {task.skipped} 跳过 · {task.review} 待确认 · {task.failed} 失败</p>
              <p>目录映射：{layouts.find(x=>x.value===task.layout)?.label} · 当前 {task.status}</p>
              <div className="dm-s13-summary-actions">
                <UI.Button small disabled={busy||running} onClick={()=>void preflight()}>检查冲突与源文件</UI.Button>
                <UI.Button small variant="ghost" disabled={busy||running}
                  onClick={()=>void reload(task.id)}>重新读取任务</UI.Button>
                <UI.Button small variant="ghost" disabled={busy} onClick={()=>void exportAll()}>导出完整报告 JSON</UI.Button>
              </div>
            </div>
            <div className="dm-s13-file-toolbar">
              <label><input type="checkbox" checked={reviewOnly}
                onChange={e=>setReviewOnly(e.target.checked)}/> 仅看待确认 / 失败</label>
              <span>预览 {entries.length} / {task.total} 个文件</span>
              <UI.Button small disabled={busy||!task.nextOffset} onClick={()=>void loadMore()}>加载后续 500 项</UI.Button>
            </div>
            {checked.length>0&&<div className="dm-s13-bulk">
              <strong>批量校对 {checked.length} 项</strong>
              <UI.Select value={bulkDecision} ariaLabel="批量操作决定"
                onChange={v=>setBulkDecision(v as ArchiveDecision)}
                options={[{value:'import',label:'导入'},{value:'skip',label:'跳过'}]}/>
              <UI.Select value={bulkStage} ariaLabel="批量文件阶段"
                onChange={v=>setBulkStage(v as typeof bulkStage)} options={stages}/>
              <UI.Input value={bulkGroup} ariaLabel="批量分组"
                onChange={setBulkGroup} placeholder="可选：分组"/>
              <UI.Input value={bulkReplicate} ariaLabel="批量样本"
                onChange={setBulkReplicate} placeholder="可选：样本"/>
              <UI.Button small disabled={busy||running} onClick={()=>void applyBulk()}>应用到选中项</UI.Button>
            </div>}
            <div className="dm-s13-entries" role="list" aria-label="源文件映射清单">
              {visible.map(e=><article role="listitem" key={e.id}
                className={'dm-s13-file'+(e.decision==='review'?' needs-review':'')}>
                <label><input type="checkbox" aria-label={'选择 '+e.relativePath}
                  checked={checked.includes(e.id)}
                  onChange={event=>setChecked(old=>event.target.checked?[...old,e.id]:
                    old.filter(id=>id!==e.id))}/></label>
                <div className="dm-s13-file-main">
                  <strong>{e.relativePath}</strong>
                  <small>{e.themeName||'项目级'} / {e.assayName||e.stage} / {e.groupName||'实验级'} /
                    {e.replicateName||'无特定样本'} → {e.stage}{e.processTag?' · 处理标签 '+e.processTag:''}</small>
                  {e.issue&&<span className="dm-s13-issue">{e.issue}</span>}
                  <small>{e.sizeBytes} bytes · SHA-256 {e.sha256.slice(0,12)}… ·
                    {e.decision==='skip'?'跳过':e.status==='imported'?'已验证':e.decision==='review'?'待确认':'待导入'}</small>
                </div>
                <UI.Button small variant="ghost" disabled={busy||running||e.status==='imported'}
                  onClick={()=>setEdit(asEdit(e))}>校对</UI.Button>
              </article>)}
              {!visible.length&&<p className="dm-s13-empty">当前加载页没有待确认或失败文件。</p>}
            </div>
            {task.status==='review'&&<p className="dm-s13-warning" role="alert">
              尚有 {task.review} 个待确认目标或同名冲突，请校对映射、改名或明确跳过，然后重新检查。
            </p>}
          </>}
        </section>
      </div>
      {task&&<footer className="dm-s13-footer">
        <div><strong>安全提交</strong>
          <span>每批至多 500 项 · CAS 不可变 · 同 SHA 去重 · 中断可恢复</span></div>
        {progress&&<span role="status" className="dm-s13-progress">
          {progress.label} · {progress.completed}/{progress.total}</span>}
        {running?<UI.Button variant="secondary" onClick={()=>void pause()}>暂停调度（保留原生任务）</UI.Button>:
          <UI.Button variant="primary" disabled={busy||task.review>0||
            !['prepared','paused','failed'].includes(task.status)}
            onClick={()=>void start()}>{ready?'确认批量导入':'继续未完成导入'}</UI.Button>}
        {task.status==='completed'&&<UI.Button variant="primary"
          onClick={()=>onImported(task)}>打开真实工作区</UI.Button>}
      </footer>}
      {edit&&<div className="dm-s13-edit-cover">
        <section className="dm-s13-edit" role="dialog" aria-label="校对导入映射">
          <h3>校对文件阶段与目标</h3>
          <p>不会修改 Windows 原件，仅调整 Data-M 记录。</p>
          <label>处理方式
            <UI.Select value={edit.decision} ariaLabel="当前文件处理方式"
              onChange={v=>setEdit({...edit,decision:v as ArchiveDecision})}
              options={[{value:'import',label:'确认导入'},{value:'skip',label:'跳过文件'},
                {value:'review',label:'继续待确认'}]}/>
          </label>
          <label>主题<UI.Input ariaLabel="当前文件主题" value={edit.themeName}
            onChange={themeName=>setEdit({...edit,themeName})}/></label>
          <label>实验<UI.Input ariaLabel="当前文件实验" value={edit.assayName}
            onChange={assayName=>setEdit({...edit,assayName})}/></label>
          <label>分组<UI.Input ariaLabel="当前文件分组" value={edit.groupName}
            onChange={groupName=>setEdit({...edit,groupName})}/></label>
          <label>生物学样本<UI.Input ariaLabel="当前文件样本" value={edit.replicateName}
            onChange={replicateName=>setEdit({...edit,replicateName})}/></label>
          <label>阶段
            <UI.Select ariaLabel="当前文件阶段" value={edit.stage}
              options={stages} onChange={v=>setEdit({...edit,stage:v as Editable['stage']})}/>
          </label>
          <label>入库显示文件名（不改源文件）
            <UI.Input value={edit.alias} ariaLabel="导入文件新名称"
              onChange={alias=>setEdit({...edit,alias})}/>
          </label>
          <div className="dm-s13-edit-actions">
            <UI.Button disabled={busy} onClick={()=>setEdit(null)}>取消</UI.Button>
            <UI.Button variant="primary" disabled={busy||!edit.alias.trim()}
              onClick={()=>void saveEntry()}>保存映射</UI.Button>
          </div>
        </section>
      </div>}
    </section>
  </div>;
}
