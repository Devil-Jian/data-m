import React,{useState,useSyncExternalStore} from 'react';
import './task-tray.css';

export type TaskPhase='prepared'|'running'|'completed'|'failed'|'cancelled';
export interface TaskEntry {
 id:string;kind:'import'|'backup'|'restore'|'audit'|'verify'|'export';
 title:string;phase:TaskPhase;completed:number;total:number;detail:string;updatedAt:number;
}
let snapshot:readonly TaskEntry[]=[];
const listeners=new Set<()=>void>();
const subscribe=(listener:()=>void)=>{listeners.add(listener);return ()=>{listeners.delete(listener);};};
const getSnapshot=()=>snapshot;

/** View-only, bounded task summaries. Never used for persistence or authorization. */
export function reportTask(entry:Omit<TaskEntry,'updatedAt'>){
 if(!entry.id||!Number.isSafeInteger(entry.completed)||!Number.isSafeInteger(entry.total)
    ||entry.completed<0||entry.total<0||entry.completed>entry.total)return;
 const safe:{[K in keyof TaskEntry]:TaskEntry[K]}={
  ...entry,title:entry.title.slice(0,96),detail:entry.detail.slice(0,180),updatedAt:Date.now()
 };
 snapshot=[safe,...snapshot.filter(item=>item.id!==entry.id)].slice(0,16);
 listeners.forEach(listener=>listener());
}
export function dismissTask(id:string){
 const entry=snapshot.find(item=>item.id===id);
 if(!entry||entry.phase==='running'||entry.phase==='prepared')return;
 snapshot=snapshot.filter(item=>item.id!==id);
 listeners.forEach(listener=>listener());
}
const phaseLabels:Record<TaskPhase,string>={prepared:'待确认',running:'执行中',completed:'完成',failed:'失败或需要检查',cancelled:'已取消'};
export function TaskTray(){
 const tasks=useSyncExternalStore(subscribe,getSnapshot,getSnapshot);
 const [expanded,setExpanded]=useState(false);
 if(!tasks.length)return null;
 const active=tasks.filter(task=>task.phase==='prepared'||task.phase==='running').length;
 return <aside className="dm-task-tray" aria-label="统一任务状态">
  <button type="button" className="dm-task-toggle" aria-expanded={expanded} onClick={()=>setExpanded(value=>!value)}>
   任务状态 · {active?active+' 项待确认或执行中':tasks.length+' 条记录'} {expanded?'收起':'展开'}
  </button>
  {expanded&&<div className="dm-task-items" role="region" aria-label="任务进度明细">
    <p className="dm-task-disclaimer">状态来自当前会话已校验的业务响应；浏览器 Mock 不代表原生持久化。重启后请在对应模块查询任务记录。本栏不执行任务取消。</p>
    {tasks.map(task=><div key={task.id} className="dm-task-item" data-phase={task.phase}>
      <div><strong>{task.title}</strong><span>{phaseLabels[task.phase]}</span></div>
      <p>{task.detail}</p>
      {task.total>0&&<progress value={task.completed} max={task.total} aria-label={task.title+' 进度'}/>}
      {task.total>0&&<small>{task.completed} / {task.total}</small>}
      {task.phase!=='running'&&task.phase!=='prepared'&&
        <button type="button" onClick={()=>dismissTask(task.id)}>移除记录</button>}
    </div>)}
  </div>}
 </aside>;
}
