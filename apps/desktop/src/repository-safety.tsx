import React,{useEffect,useRef,useState} from 'react';
import {reportTask} from './task-tray.tsx';
import {safeLabel,safetyApi,safetyListen,safeSafetyError} from './repository-safety.ts';
import type {SafetyApi,SafetyProgress,SafetyReport,SnapshotSummary,ProgressListen,SafetyRequest} from './repository-safety.ts';
import './repository-safety.css';

const actionLabels:Record<string,string>={audit:'只读完整性检查',backup:'创建并验证快照',restore:'隔离恢复',verify:'验证备份哈希'};
const phases:Record<string,string>={
 sqlite_snapshot:'SQLite WAL 一致性快照',copy_blobs:'复制并核验 CAS 原始字节',
 verify_blobs:'检查快照哈希及版本',published:'封存备份',
 restore_sqlite:'复制隔离恢复数据库',restore_blobs:'恢复并核验 CAS',restore_published:'隔离恢复已完成'
};
export function RepositorySafetyPanel({onClose,onBusyChange,api=safetyApi,listenProgress=safetyListen}:
 {onClose:()=>void;onBusyChange?:(busy:boolean)=>void;api?:SafetyApi;listenProgress?:ProgressListen}){
 const [busy,setBusy]=useState(false),[error,setError]=useState(''),[message,setMessage]=useState('');
 const activeTask=useRef<string|null>(null),activeKind=useRef<'backup'|'restore'|'audit'|'verify'>('audit');
 useEffect(()=>{onBusyChange?.(busy);return ()=>onBusyChange?.(false);},[busy,onBusyChange]);
 const [snapshots,setSnapshots]=useState<SnapshotSummary[]>([]);
 const [report,setReport]=useState<SafetyReport|null>(null),[progress,setProgress]=useState<SafetyProgress|null>(null);
 const [backupConfirm,setBackupConfirm]=useState(false);
 const [restoreId,setRestoreId]=useState<string|null>(null),[restoreLabel,setRestoreLabel]=useState('');
 const [restoreConfirm,setRestoreConfirm]=useState(false);
 useEffect(()=>{
  let alive=true,stop:(()=>void)|undefined;
  void listenProgress(data=>{
    if(!alive)return;
    setProgress(data);
    if(activeTask.current&&data.total>0&&data.completed<=data.total){
      reportTask({id:activeTask.current,kind:activeKind.current,title:'仓库安全 · '+(phases[data.phase]??'检查进度'),phase:'running',completed:data.completed,total:data.total,detail:'来自 S7 原生进度事件；未完成核验前不代表成功'});
    }
  }).then(unlisten=>{
    if(!alive)unlisten();else stop=unlisten;
  }).catch(()=>{});
  return ()=>{alive=false;stop?.();};
 },[listenProgress]);
 useEffect(()=>{
  let alive=true;
  void api({action:'list'}).then(data=>{if(alive)setSnapshots(data.snapshots);})
   .catch(reason=>{if(alive)setError(safeSafetyError(reason).message);});
  return ()=>{alive=false;};
 },[api]);
 async function run(request:SafetyRequest){
  if(busy)return;
  setBusy(true);setError('');setMessage('');setProgress(null);
  const track=request.action!=='list';
  const taskId='safety:'+request.action;
  if(track){
    activeTask.current=taskId;
    activeKind.current=request.action==='list'?'audit':request.action;
    reportTask({id:taskId,kind:request.action==='list'?'audit':request.action,title:'仓库安全 · '+actionLabels[request.action],phase:'running',completed:0,total:0,detail:'正在等待真实 S7 IPC 结果'});
  }
  try{
    const result=await api(request);
    if(result.report)setReport(result.report);
    const status=result.status;
    setMessage(status==='issues_found'?'检查发现问题，请查看报告；未自动修复任何数据。':
      status==='clean'?'受控测试仓库检查通过；这不等于正式安全验收。':
      status==='restored'?'独立测试仓库 '+result.restoreLabel+' 恢复成功；原运行仓库未被替换。':
      status==='verified'&&request.action==='backup'?'已封存快照 '+result.snapshotId+'；请另复制至独立磁盘。':
      status==='verified'?'快照 '+result.snapshotId+' 的数据库和 Blob 已核验。':'快照列表已刷新。');
    if(request.action==='backup'||request.action==='restore'||request.action==='list'){
      const latest=await api({action:'list'});setSnapshots(latest.snapshots);
    }
    if(track)reportTask({id:taskId,kind:request.action==='list'?'audit':request.action,title:'仓库安全 · '+actionLabels[request.action],phase:status==='issues_found'?'failed':'completed',completed:status==='issues_found'?0:1,total:1,detail:status==='issues_found'?'发现完整性问题；查看仓库报告':'已收到原生 S7 确认结果；不代表异地备份'});
    if(request.action==='backup')setBackupConfirm(false);
    if(request.action==='restore'){setRestoreId(null);setRestoreLabel('');setRestoreConfirm(false);}
  }catch(reason){
    setError(safeSafetyError(reason).message);
    if(track)reportTask({id:taskId,kind:request.action==='list'?'audit':request.action,title:'仓库安全 · '+actionLabels[request.action],phase:'failed',completed:0,total:1,detail:'原生结果未确认；请在本模块重新查询状态'});
  }
  finally{if(activeTask.current===taskId)activeTask.current=null;setBusy(false);}
 }
 return <main className="dm-safety" aria-label="S7 完整性与备份恢复工作台" aria-busy={busy}>
  <header className="dm-safety-header">
    <div><small>DATA M / S7 · 仅测试仓库</small><h1>完整性检查 · 备份 · 恢复</h1>
      <p>只允许可抛弃测试数据；恢复演练签收前，禁止珍贵或唯一科研文件入库。</p></div>
    <button type="button" disabled={busy} onClick={onClose}>返回项目管理</button>
  </header>
  <div className="dm-safety-warning" role="note">
    <strong>不是异地备份：</strong>快照保存在当前设备应用数据目录，须另行复制至独立磁盘并再次核验。
    本页不覆盖正在使用的仓库，也不修复损坏 Blob。
  </div>
  <section className="dm-safety-section" aria-label="仓库完整性检查">
    <h2>1 · 核验测试仓库</h2><p>检查 SQLite、外键、关系、正式 CAS 哈希和孤儿暂存数据，不修改源库。</p>
    <button type="button" disabled={busy} onClick={()=>void run({action:'audit'})}>运行只读完整性检查</button>
  </section>
  <section className="dm-safety-section" aria-label="创建验证快照">
    <h2>2 · 创建一致性快照</h2>
    <p>SQLite VACUUM INTO 捕获 WAL 已提交事务；逐项复制并验证 Blob SHA-256 后才封存。</p>
    <label className="dm-safety-check"><input type="checkbox" checked={backupConfirm} disabled={busy}
       onChange={event=>setBackupConfirm(event.target.checked)}/>
      我确认正在操作可抛弃测试仓库；设备内快照并非异地备份。</label>
    <button type="button" disabled={busy||!backupConfirm} onClick={()=>void run({action:'backup'})}>确认创建并验证备份</button>
  </section>
  <section className="dm-safety-section" aria-label="快照列表与隔离恢复">
    <h2>3 · 校验和隔离恢复</h2>
    <button type="button" disabled={busy} onClick={()=>void run({action:'list'})}>刷新快照列表</button>
    <p>仅向全新的独立测试目录恢复，不覆盖或切换当前仓库。必须输入新标签并确认。</p>
    {snapshots.length===0?<p>尚无已封存的快照。</p>:
      <ul className="dm-safety-snapshots">{snapshots.map(item=><li key={item.snapshotId}>
        <div><code>{item.snapshotId}</code><small>Schema v{item.schemaVersion} · {item.blobCount} 个 Blob · {item.versionCount} 个版本 · {item.createdAt}</small></div>
        <button type="button" disabled={busy} onClick={()=>void run({action:'verify',snapshotId:item.snapshotId})}>验证哈希</button>
        <button type="button" disabled={busy} onClick={()=>{setRestoreId(item.snapshotId);setRestoreLabel('');setRestoreConfirm(false);setError('');}}>恢复到新目录</button>
      </li>)}</ul>}
    {restoreId&&<div className="dm-safety-restore" role="group" aria-label="隔离恢复确认">
      <h3>恢复快照 <code>{restoreId}</code></h3>
      <p>目标：当前设备应用数据 / s7-restores / 新目录标签。目标必须不存在；原仓库不受影响。</p>
      <label>新恢复目录标签
        <input aria-label="恢复目录标签" value={restoreLabel} disabled={busy} maxLength={50}
         placeholder="例如 s7-drill-01" onChange={event=>{setRestoreLabel(event.target.value);setRestoreConfirm(false);}}/>
      </label>
      {restoreLabel&&!safeLabel(restoreLabel)&&<p role="alert">标签须为 1–50 位字母、数字、减号或下划线，不能是设备保留名。</p>}
      <label className="dm-safety-check"><input type="checkbox" disabled={busy} checked={restoreConfirm}
        onChange={event=>setRestoreConfirm(event.target.checked)}/>
        我确认只向新的测试目录恢复，绝不覆盖当前仓库。</label>
      <div className="dm-safety-actions">
        <button type="button" disabled={busy} onClick={()=>{setRestoreId(null);setRestoreConfirm(false);}}>取消</button>
        <button type="button" disabled={!restoreId||!safeLabel(restoreLabel)||!restoreConfirm||busy}
          onClick={()=>{if(restoreId)void run({action:'restore',snapshotId:restoreId,restoreLabel,confirm:true});}}>确认隔离恢复</button>
      </div>
    </div>}
  </section>
  {progress&&<p className="dm-safety-progress" role="status">
    {phases[progress.phase]??'正在核验'}：{progress.completed}/{progress.total}
    {progress.total>0&&<progress max={progress.total} value={progress.completed}/>}
  </p>}
  {busy&&<p role="status">正在执行真实 SQLite 和文件系统操作，请勿关闭测试仓库。</p>}
  {error&&<p className="dm-safety-error" role="alert">{error}</p>}
  {message&&<p className="dm-safety-result" role="status">{message}</p>}
  {report&&<section className="dm-safety-section" aria-label="完整性核验报告">
    <h2>核验报告</h2><p>Blob：已核对 {report.checkedBlobs}，哈希通过 {report.verifiedBlobs}；
      问题 {report.issues.length}{report.truncated?'（更多问题已截断）':''}。</p>
    {report.issues.length>0&&<ul className="dm-safety-issues">{report.issues.map((issue,i)=>
      <li key={issue.code+'-'+i}><code>{issue.code}</code> · {issue.subject}</li>)}</ul>}
    <h3>仓库关系计数</h3>
    <dl className="dm-safety-counts">{Object.entries(report.tableCounts).map(([table,count])=>
      <div key={table}><dt>{table}</dt><dd>{count}</dd></div>)}</dl>
  </section>}
  <p className="dm-safety-footer">机器验证不能替代 Windows 11 原生验收。S0–S7 全部签收且完成真实恢复演练后才可考虑珍贵科研数据。</p>
 </main>;
}
