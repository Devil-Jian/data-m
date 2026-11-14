import React, {useEffect,useState} from 'react';
import {createRoot} from 'react-dom/client';
import {Workbench} from '@data-m/core';
import type {UIKit} from '@data-m/core';
import {getCurrentWebview} from '@tauri-apps/api/webview';
import {isTauri} from '@tauri-apps/api/core';
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
type DropState={kind:'over'}|{kind:'received';count:number}|{kind:'error';detail:string}|null;

/** Native Windows shell adapter. The first milestone receives file paths but never writes them. */
function DesktopApp(){
  const [drop,setDrop]=useState<DropState>(null);
  useEffect(()=>{
    if(!isTauri())return;
    let disposed=false;
    let unlisten:(()=>void)|undefined;
    void getCurrentWebview().onDragDropEvent(event=>{
      if(disposed)return;
      switch(event.payload.type){
        case 'enter':
        case 'over':
          setDrop({kind:'over'});
          break;
        case 'drop':
          // No import until the P2 Rust asset service is implemented and reviewed.
          setDrop({kind:'received',count:event.payload.paths.length});
          break;
        case 'leave':
          setDrop(null);
          break;
      }
    }).then(fn=>{if(disposed)fn();else unlisten=fn}).catch(err=>{
      if(!disposed)setDrop({kind:'error',detail:String(err)});
    });
    return ()=>{disposed=true;unlisten?.()};
  },[]);
  return <>
    <Workbench ui={ui} variant="shadcn"/>
    {drop&&<div className={'dm-native-drop '+(drop.kind==='over'?'is-over':'')} role="status">
      {drop.kind==='over'?'释放鼠标以检测文件（本版本尚不导入）':
       drop.kind==='received'?`已检测到 ${drop.count} 个文件/目录；安全导入将在 P2 启用，原文件未修改`:
       `Windows 拖放事件不可用：${drop.detail}`}
      <button type="button" onClick={()=>setDrop(null)} aria-label="关闭拖放提示">×</button>
    </div>}
  </>;
}
createRoot(document.getElementById('root')!).render(<React.StrictMode><DesktopApp/></React.StrictMode>);
