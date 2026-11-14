/** Read-only native-drop inspection state. No raw OS paths are stored here. */
export type DropEntry={
  name:string;
  kind:'file'|'directory'|'link'|'unsupported'|'unavailable'|'invalid';
  sizeBytes:number|null;
  eligible:boolean;
  warning:string|null;
};
export type DropPreview={
  entries:DropEntry[];
  totalReceived:number;
  omittedCount:number;
  eligibleCount:number;
};
export type DropState=
  | {kind:'idle'}
  | {kind:'over'}
  | {kind:'inspecting';requestId:number;count:number}
  | {kind:'ready';preview:DropPreview}
  | {kind:'error';message:string};
export type DropAction=
  | {type:'hover'}
  | {type:'leave'}
  | {type:'start';requestId:number;count:number}
  | {type:'success';requestId:number;preview:DropPreview}
  | {type:'fail';requestId:number}
  | {type:'listenerError'}
  | {type:'dismiss'};

export const initialDropState:DropState={kind:'idle'};

export function dropReducer(previous:DropState,action:DropAction):DropState{
  switch(action.type){
    case 'hover':
      // An incidental drag-over event must not hide a completed preview.
      return previous.kind==='idle'||previous.kind==='over'?{kind:'over'}:previous;
    case 'leave':
      return previous.kind==='over'?initialDropState:previous;
    case 'start':
      return {kind:'inspecting',requestId:action.requestId,count:action.count};
    case 'success':
      return previous.kind==='inspecting'&&previous.requestId===action.requestId
        ?{kind:'ready',preview:action.preview}:previous;
    case 'fail':
      return previous.kind==='inspecting'&&previous.requestId===action.requestId
        ?{kind:'error',message:'无法读取拖入项目的信息；原始文件未修改'}:previous;
    case 'listenerError':
      return {kind:'error',message:'Windows 原生拖入监听不可用；请确认使用 Tauri 桌面客户端'};
    case 'dismiss':
      return initialDropState;
  }
}
export function formatDropSize(bytes:number|null):string{
  if(bytes===null||!Number.isFinite(bytes)||bytes<0)return '—';
  if(bytes<1024)return `${bytes} B`;
  if(bytes<1024*1024)return `${(bytes/1024).toFixed(1)} KB`;
  if(bytes<1024*1024*1024)return `${(bytes/1048576).toFixed(1)} MB`;
  return `${(bytes/1073741824).toFixed(2)} GB`;
}
