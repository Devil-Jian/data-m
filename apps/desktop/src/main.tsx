import React, {useCallback,useEffect,useReducer,useRef,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {ProductionWorkbench} from './production-workbench';
import {Workbench} from '@data-m/core';
import {ProjectTreeManager} from './project-tree.tsx';
import {getCurrentWebview} from '@tauri-apps/api/webview';
import {invoke,isTauri} from '@tauri-apps/api/core';
import {dropReducer,formatDropSize,initialDropState} from './drop-preview';
import type {DropPreview} from './drop-preview';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import '@data-m/core/style.css';
import '@data-m/ui/style.css';
import './desktop.css';
import type {ImportDropHandler,ImportDropRegistrar} from './import-jobs.ts';

/** Historical UI test harness, only routed from the dev server with ?legacy=1.
 * Bundled production builds cannot activate these fixtures or this old shell. */
function LegacyDevelopmentHarness({registerImportDrop}:{registerImportDrop:ImportDropRegistrar}){
  const [managingProjects,setManagingProjects]=useState(false);
  const [projectBusy,setProjectBusy]=useState(false);
  return <>
    <nav className="dm-desktop-modebar" aria-label="工作区模式">
      <shadcnUI.Button disabled={projectBusy} variant={managingProjects?'primary':'secondary'} small onClick={()=>setManagingProjects(true)}>项目管理</shadcnUI.Button>
      <shadcnUI.Button disabled={projectBusy} variant={managingProjects?'secondary':'primary'} small onClick={()=>setManagingProjects(false)}>演示工作区</shadcnUI.Button>
      <span>{managingProjects?'Project / Theme / Assay':'演示数据与文件交互，不代表真实入库'}</span>
      <small className="dm-release-candidate" aria-label="安装及版本信息">0.1.0 S11 测试构建 · 未签名 · 支持仓库 Schema v1–v3 · 非正式 v1.0</small>
    </nav>
    <div className="dm-desktop-content">
      {managingProjects?<ProjectTreeManager onBusyChange={setProjectBusy} registerImportDrop={registerImportDrop}/>:<Workbench ui={shadcnUI} variant="shadcn"/>}
    </div>
  </>;
}

/** Native Windows drop adapter. Inspection is read-only; source paths never reach UI state. */
function DesktopApp(){
  const [drop,dispatch]=useReducer(dropReducer,initialDropState);
  const dismissButtonRef=useRef<HTMLButtonElement>(null);
  const importDropRef=useRef<ImportDropHandler|null>(null);
  const registerImportDrop:ImportDropRegistrar=useCallback(handler=>{
    importDropRef.current=handler;
    if(handler)dispatch({type:'dismiss'});
  },[]);
  // Synthetic drops exist only for an explicitly injected browser test backend.
  // A normal browser has no source-path entry or native import simulation.
  useEffect(()=>{
    if(isTauri()||!window.__S3_IMPORTS_MOCK__)return;
    const onTestDrop=(event:Event)=>{
      const paths=(event as CustomEvent<{paths?:unknown}>).detail?.paths;
      if(Array.isArray(paths)&&paths.every(path=>typeof path==='string'))importDropRef.current?.(paths);
    };
    window.addEventListener('data-m-test-import-drop',onTestDrop);
    return ()=>window.removeEventListener('data-m-test-import-drop',onTestDrop);
  },[]);
  // Make the native drop preview dismissible with Escape, including during inspection.
  useEffect(()=>{
    if(drop.kind==='idle')return;
    const onKeyDown=(event:KeyboardEvent)=>{
      if(event.key!=='Escape')return;
      event.preventDefault();
      event.stopImmediatePropagation();
      dispatch({type:'dismiss'});
    };
    window.addEventListener('keydown',onKeyDown,true);
    return ()=>window.removeEventListener('keydown',onKeyDown,true);
  },[drop.kind]);
  useEffect(()=>{
    if(drop.kind==='ready'||drop.kind==='error'){
      dismissButtonRef.current?.focus({preventScroll:true});
    }
  },[drop.kind]);
  useEffect(()=>{
    if(!isTauri())return;
    let disposed=false;
    let lastRequest=0;
    let unlisten:(()=>void)|undefined;
    void getCurrentWebview().onDragDropEvent(event=>{
      if(disposed)return;
      if(importDropRef.current){
        if(event.payload.type==='drop'){
          // Only the active S3 panel prepares these ephemeral OS paths. Its
          // explicit confirmation is still required before any copying starts.
          importDropRef.current(event.payload.paths);
        }
        return;
      }
      switch(event.payload.type){
        case 'enter':
        case 'over':
          dispatch({type:'hover'});
          break;
        case 'drop':{
          const requestId=++lastRequest;
          dispatch({type:'start',requestId,count:event.payload.paths.length});
          // Only the Rust command sees OS paths. The renderer receives basename metadata.
          void invoke<DropPreview>('inspect_drop_paths',{paths:event.payload.paths})
            .then(preview=>{
              if(!disposed)dispatch({type:'success',requestId,preview});
            })
            .catch(()=>{
              if(!disposed)dispatch({type:'fail',requestId});
            });
          break;
        }
        case 'leave':
          dispatch({type:'leave'});
          break;
      }
    }).then(fn=>{if(disposed)fn();else unlisten=fn}).catch(()=>{
      if(!disposed)dispatch({type:'listenerError'});
    });
    return ()=>{disposed=true;unlisten?.()};
  },[]);

  const dismiss=()=>dispatch({type:'dismiss'});
  const summary=drop.kind==='ready'?drop.preview:null;
  return <div className="dm-desktop-shell">
    <div className="dm-desktop-content">
      {import.meta.env.DEV && new URLSearchParams(window.location.search).get('legacy')==='1'
        ?<LegacyDevelopmentHarness registerImportDrop={registerImportDrop}/>
        :<ProductionWorkbench registerImportDrop={registerImportDrop}/>}
    </div>
    {drop.kind!=='idle'&&<section
      className={'dm-native-drop '+(drop.kind==='over'?'is-over':'')}
      role={drop.kind==='ready'?'dialog':'status'}
      aria-label="Windows 文件拖入预览">
      <div className="dm-drop-title">
        <strong>Windows 文件拖入检查</strong>
        <button ref={dismissButtonRef} type="button" onClick={dismiss} aria-label="关闭文件拖入预览">×</button>
      </div>
      {drop.kind==='over'&&<p>松开鼠标以查看文件信息；不会执行导入。</p>}
      {drop.kind==='inspecting'&&<p>正在只读检查 {drop.count} 个文件或文件夹…</p>}
      {drop.kind==='error'&&<p role="alert">{drop.message}</p>}
      {summary&&<>
        <p>收到 {summary.totalReceived} 项，初步可识别 {summary.eligibleCount} 项。
          {summary.omittedCount>0&&` 超出预览上限 ${summary.omittedCount} 项未检查。`}</p>
        <ul className="dm-drop-entries" aria-label="待检查文件清单">
          {summary.entries.slice(0,16).map((entry,index)=><li key={index}>
            <span title={entry.name} className="dm-drop-filename">{entry.name}</span>
            <span>{entry.kind==='file'?'文件':entry.kind==='directory'?'文件夹':'不可用'}</span>
            <span>{formatDropSize(entry.sizeBytes)}</span>
            {entry.warning&&<small>{entry.warning}</small>}
          </li>)}
        </ul>
        {summary.entries.length>16&&<p>预览仅展示前 16 项，其余 {summary.entries.length-16} 项已完成初步检查。</p>}
        <p className="dm-drop-warning">当前仅进行只读文件元数据检查，尚未提交导入任务。请在已验证的目标范围中使用受控导入流程。</p>
      </>}
    </section>}
  </div>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><DesktopApp/></React.StrictMode>);
