import React, {useEffect,useReducer} from 'react';
import {createRoot} from 'react-dom/client';
import {Workbench} from '@data-m/core';
import type {UIKit} from '@data-m/core';
import {getCurrentWebview} from '@tauri-apps/api/webview';
import {invoke,isTauri} from '@tauri-apps/api/core';
import {dropReducer,formatDropSize,initialDropState} from './drop-preview';
import type {DropPreview} from './drop-preview';
import {Button} from '../../../demos/shadcn/src/components/ui/button';
import {SelectField} from '../../../demos/shadcn/src/components/ui/select';
import {CheckboxField} from '../../../demos/shadcn/src/components/ui/checkbox';
import {InputField} from '../../../demos/shadcn/src/components/ui/input';
import {BadgeField} from '../../../demos/shadcn/src/components/ui/badge';
import '@data-m/core/style.css';
import '../../../demos/shadcn/src/style.css';
import './desktop.css';

const ui:UIKit={
  Button:({children,onClick,variant='secondary',small=false,disabled,title})=>
    <Button onClick={onClick} variant={variant} small={small} disabled={disabled} title={title}>{children}</Button>,
  Select:SelectField,
  Input:InputField,
  Check:CheckboxField,
  Badge:BadgeField
};
/** Native Windows drop adapter. Inspection is read-only; source paths never reach UI state. */
function DesktopApp(){
  const [drop,dispatch]=useReducer(dropReducer,initialDropState);
  useEffect(()=>{
    if(!isTauri())return;
    let disposed=false;
    let lastRequest=0;
    let unlisten:(()=>void)|undefined;
    void getCurrentWebview().onDragDropEvent(event=>{
      if(disposed)return;
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
  return <>
    <Workbench ui={ui} variant="shadcn"/>
    {drop.kind!=='idle'&&<section
      className={'dm-native-drop '+(drop.kind==='over'?'is-over':'')}
      role={drop.kind==='ready'?'dialog':'status'}
      aria-label="Windows 文件拖入预览">
      <div className="dm-drop-title">
        <strong>Windows 文件拖入检查</strong>
        <button type="button" onClick={dismiss} aria-label="关闭文件拖入预览">×</button>
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
        <p className="dm-drop-warning">仅只读检查文件元数据。尚未导入、复制或修改任何文件。
          真正导入需要 P2 安全存储服务，当前不可用。</p>
      </>}
    </section>}
  </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><DesktopApp/></React.StrictMode>);
