import React,{useEffect,useRef,useState} from 'react';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import {formatDropSize} from './drop-preview';
import {importApi,importMode,importErrorMessage,safeImportError,validateImportResponse} from './import-jobs.ts';
import type {ImportApi,ImportDropRegistrar,ImportJob,ImportRequest,ImportTarget} from './import-jobs.ts';
import './import-jobs.css';

const {Button,Badge}=shadcnUI;
const statuses={prepared:'待确认',running:'导入中',completed:'已完成',failed:'失败',cancelled:'已取消',interrupted:'已中断',integrity_failed:'完整性检查失败'};
const fileStatuses={queued:'排队中',copying:'复制中',staged:'已暂存',blob_published:'已发布，待登记',committed:'已登记',failed:'失败',cancelled:'已取消',interrupted:'已中断'};
const stages={raw:'Raw 原始数据',processed:'Processed 处理数据',statistics:'Statistics 统计结果',display:'Display 展示成果'};
function active(job:ImportJob){return job.status==='prepared'||job.status==='running';}
interface Props {
  assayId:string;target:ImportTarget|null;targetLabel:string;onClose:()=>void;
  onLockChange:(locked:boolean)=>void;registerImportDrop?:ImportDropRegistrar;api?:ImportApi;
  describeTarget?:(target:ImportTarget)=>string;
}

export function ImportJobsPanel({assayId,target,targetLabel,onClose,onLockChange,registerImportDrop,describeTarget,api=importApi}:Props){
  const [jobs,setJobs]=useState<ImportJob[]>([]),[requestCount,setRequestCount]=useState(0);
  const [loaded,setLoaded]=useState(false),[error,setError]=useState('');
  const alive=useRef(true),jobsRef=useRef(jobs),epoch=useRef(0);
  const actions=useRef(new Set<string>()),polling=useRef(false);
  jobsRef.current=jobs;
  const locked=requestCount>0||jobs.some(active);
  const fixedJob=jobs.find(active);
  const describe=(value:ImportTarget)=>describeTarget?.(value)??`${stages[value.stage]} · ${value.replicateId?`样本 ${value.replicateId}`:`实验 ${value.assayId}`}`;
  useEffect(()=>{onLockChange(locked);return ()=>onLockChange(false);},[locked,onLockChange]);

  function apply(incoming:ImportJob[],replace=false){
    const relevant=incoming.filter(job=>job.target.assayId===assayId);
    setJobs(current=>replace?relevant:[...current.filter(job=>!relevant.some(item=>item.id===job.id)),...relevant]);
  }
  async function command(request:ImportRequest,key:string,replace=false){
    if(actions.current.has(key))return;
    actions.current.add(key);const ticket=++epoch.current;
    setRequestCount(count=>count+1);setError('');
    try{
      const result=validateImportResponse(await api(request));
      if(alive.current&&ticket===epoch.current){apply(result.jobs,replace);setLoaded(true);}
    }catch(reason){if(alive.current&&ticket===epoch.current)setError(safeImportError(reason).message);}
    finally{actions.current.delete(key);if(alive.current)setRequestCount(count=>count-1);}
  }
  useEffect(()=>{
    alive.current=true;
    void command({action:'list'},'list',true);
    return ()=>{alive.current=false;};
  },[api,assayId]);
  useEffect(()=>{
    const timer=window.setInterval(()=>{
      const running=jobsRef.current.filter(job=>job.status==='running');
      if(polling.current||actions.current.size||!running.length)return;
      polling.current=true;
      const ticket=epoch.current;
      void Promise.all(running.map(async job=>validateImportResponse(await api({action:'get',jobId:job.id})))).then(results=>{
        if(alive.current&&ticket===epoch.current){for(const result of results)apply(result.jobs);setError('');}
      }).catch(reason=>{if(alive.current&&ticket===epoch.current)setError(safeImportError(reason).message);})
        .finally(()=>{polling.current=false;});
    },500);
    return ()=>window.clearInterval(timer);
  },[api,assayId]);
  useEffect(()=>{
    if(!registerImportDrop)return;
    // Paths live only in this drop callback's prepare invocation. They are never
    // retained in React state, rendered, logged, or used to start automatically.
    registerImportDrop(paths=>{
      if(actions.current.size||jobsRef.current.some(active)){setError('请先确认或取消当前任务，再拖入新的测试文件。');return;}
      if(!loaded){setError('任务记录尚未加载，请先重新加载任务再准备导入。');return;}
      if(!target){setError('请先选择样本，或选择实验级 Statistics / Display 阶段。');return;}
      if(!paths.length){setError('未收到可准备的测试文件。');return;}
      void command({action:'prepare',paths,target,requestId:crypto.randomUUID()},'prepare');
    });
    return ()=>registerImportDrop(null);
  },[api,assayId,target?.assayId,target?.stage,target?.groupId,target?.replicateId,loaded,registerImportDrop]);

  return <section className="dm-import-panel" aria-label="安全导入任务" aria-busy={requestCount>0}>
    <header className="dm-import-header"><div><h2>安全导入任务</h2><Badge tone="neutral">{api===importApi?importMode():'注入 API（测试运行态）'}</Badge></div>
      <div className="dm-import-actions"><Button disabled={requestCount>0} onClick={()=>void command({action:'list'},'list',true)}>重新加载任务</Button>
        <Button disabled={locked} onClick={onClose}>关闭导入任务</Button></div></header>
    <p className="dm-import-test-warning">仅用于受控测试库和可丢弃测试文件。请勿拖入正式或珍贵科研数据。</p>
    <div className="dm-import-drop-zone"><strong>{fixedJob?'待处理任务固定目标：':'当前目标：'}{fixedJob?describe(fixedJob.target):target?targetLabel:'尚未选择有效目标'}</strong>
      <p>{fixedJob?'待确认或正在运行的任务已固定目标。请先完成或取消，再切换样本和阶段。':target?'将测试文件拖入窗口以准备任务，然后逐项检查并明确确认导入。':'Raw / Processed 需要先选择样本；Statistics / Display 为实验级。'}</p>
      <small>拖入不会自动开始复制。预览可识别数量不作为导入授权。</small></div>
    {error&&<p role="alert" className="dm-import-error">{error}</p>}
    {!loaded&&requestCount>0&&<p role="status">正在加载持久任务记录…</p>}
    {loaded&&jobs.length===0&&<p className="dm-import-empty">此实验暂无导入任务。</p>}
    <div className="dm-import-job-list">{jobs.map(job=><article key={job.id} className="dm-import-job" aria-label={`导入任务 ${job.id}`}>
      <header><div><strong>任务 {job.id}</strong><span className={`dm-import-state is-${job.status}`}>{statuses[job.status]}</span></div>
        <div className="dm-import-actions">
          {job.status==='prepared'&&<Button variant="primary" disabled={requestCount>0} onClick={()=>void command({action:'start',jobId:job.id},`start:${job.id}`)}>确认导入（仅限可丢弃测试文件）</Button>}
          {active(job)&&<Button variant="danger" disabled={actions.current.has(`cancel:${job.id}`)} onClick={()=>void command({action:'cancel',jobId:job.id},`cancel:${job.id}`)}>取消任务</Button>}
          {['failed','cancelled','interrupted'].includes(job.status)&&<Button disabled={requestCount>0} onClick={()=>void command({action:'retry',jobId:job.id},`retry:${job.id}`)}>重试未完成文件</Button>}
        </div></header>
      <p className="dm-import-job-target">固定目标：{describe(job.target)}</p>
      <p className="dm-import-job-count">已登记 {job.completedCount} / {job.files.length} 个文件 · 已复制 {formatDropSize(job.copiedBytes)} / {formatDropSize(job.totalBytes)}</p>
      <progress aria-label={`任务 ${job.id} 复制进度`} value={Math.min(job.copiedBytes,job.totalBytes)} max={job.totalBytes||1}/>
      {job.status==='prepared'&&<p className="dm-import-job-note">待明确确认。尚未开始复制。</p>}
      {job.status!=='completed'&&job.status!=='prepared'&&<p className="dm-import-job-note">本批次尚未全部完成。已登记文件保留，取消或重试不会重新登记已完成文件。</p>}
      {job.errorCode&&<p className="dm-import-file-error">{importErrorMessage(job.errorCode)}</p>}
      <ul className="dm-import-files" aria-label={`任务 ${job.id} 文件清单`}>{job.files.map(file=><li key={file.id}>
        <div><strong>{file.name}</strong><span>{formatDropSize(file.sizeBytes)}</span><span>{fileStatuses[file.status]}</span></div>
        {file.sha256&&<small>SHA-256：{file.sha256}</small>}
        {file.errorCode&&<p className="dm-import-file-error">{importErrorMessage(file.errorCode)}</p>}
      </li>)}</ul>
    </article>)}</div>
  </section>;
}
