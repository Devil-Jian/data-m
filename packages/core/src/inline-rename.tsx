import React, {useEffect,useRef,useState} from 'react';
import {filenameStemLength,renameValidationError} from './rename-validation';

export interface InlineRenameProps{
  original:string;
  siblings:readonly string[];
  onConfirm:(next:string)=>void;
  onCancel:()=>void;
}
/** Editing a candidate basename. Enter only submits a demo intent; no file is renamed. */
export function InlineRename({original,siblings,onConfirm,onCancel}:InlineRenameProps){
  const [value,setValue]=useState(original);
  const [error,setError]=useState<string|null>(null);
  const inputRef=useRef<HTMLInputElement>(null);
  useEffect(()=>{
    const input=inputRef.current;
    if(!input)return;
    input.focus({preventScroll:true});
    input.setSelectionRange(0,filenameStemLength(original));
  },[original]);
  function confirm(){
    const failure=renameValidationError(value,original,siblings);
    if(failure){setError(failure);return;}
    onConfirm(value);
  }
  return <span className="dm-rename-editor" data-explorer-no-gesture>
    <input ref={inputRef} type="text" value={value} aria-label="重命名文件"
      spellCheck={false} autoComplete="off" aria-invalid={Boolean(error)}
      onChange={event=>{setValue(event.target.value);setError(null);}}
      onKeyDown={event=>{
        event.stopPropagation();
        if(event.key==='Enter'){event.preventDefault();confirm();}
        else if(event.key==='Escape'){event.preventDefault();onCancel();}
      }}
      onBlur={event=>{
        if(!event.currentTarget.parentElement?.contains(event.relatedTarget as Node)){
          onCancel();
        }
      }}
    />
    {error&&<span className="dm-rename-error" role="alert">{error}</span>}
  </span>;
}
