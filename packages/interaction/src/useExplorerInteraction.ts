import {useCallback,useEffect,useRef,useState} from 'react';
import {
  canDrop,createSelection,distance,dragOperation,intersects,isEditableTarget,
  rectFromPoints,selectAll,selectItem,selectMarquee
} from './model';
import type {DragIntent,ItemId,ModifierKeys,Point,Rect,Selection,Operation} from './model';

type Pending = {
  kind:'marquee'|'item'; origin:Point; sourceId:string|null; itemId:string|null;
  additive:boolean; original:Set<string>; moved:boolean;
};
export type Gesture =
  | {kind:'idle'}
  | {kind:'marquee';rect:Rect}
  | {kind:'drag';x:number;y:number;targetId:string|null;allowed:boolean;operation:Operation;count:number};
export interface ExplorerOptions {
  visibleIds:readonly ItemId[];
  enabled?:boolean;
  onDrop?:(intent:DragIntent)=>void;
  onCopy?:(ids:string[])=>void;
  onCut?:(ids:string[])=>void;
  onPaste?:(ids:string[],operation:Operation)=>void;
  onDelete?:(ids:string[])=>void;
  onRename?:(id:string)=>void;
}
export function useExplorerInteraction(options:ExplorerOptions) {
  const [selection,setSelection]=useState<Selection>(createSelection);
  const [gesture,setGesture]=useState<Gesture>({kind:'idle'});
  const selectionRef=useRef(selection);selectionRef.current=selection;
  const optionsRef=useRef(options);optionsRef.current=options;
  const pending=useRef<Pending|null>(null);
  const clipboard=useRef<{ids:string[];operation:Operation}|null>(null);
  const preventNextClick=useRef(false);
  const visibleRef=useRef(options.visibleIds);visibleRef.current=options.visibleIds;

  const setSelectedIds=useCallback((ids:string[])=>{
    setSelection(previous=>({selected:new Set(ids),anchor:ids[0]||null,focused:ids.at(-1)||null}));
  },[]);
  const clear=useCallback(()=>setSelection(createSelection()),[]);
  const select=useCallback((id:string,mods:ModifierKeys={})=>{
    setSelection(previous=>selectItem(previous,id,visibleRef.current,mods));
  },[]);
  const toggle=useCallback((id:string,checked:boolean)=>{
    setSelection(previous=>{
      const selected=new Set(previous.selected);
      if(checked)selected.add(id);else selected.delete(id);
      return {selected,anchor:id,focused:id};
    });
  },[]);

  const onPointerDown=useCallback((event:React.PointerEvent<HTMLElement>)=>{
    if(optionsRef.current.enabled===false||event.button!==0)return;
    const target=event.target;
    if(!(target instanceof Element)||isEditableTarget(target))return;
    // Interactive controls and links manage their own selection and actions.
    if(target.closest('button, a, input, label, [role="checkbox"], [data-explorer-no-gesture]'))return;
    const item=target.closest<HTMLElement>('[data-explorer-id]');
    const dropSource=item?.closest<HTMLElement>('[data-source-container-id]');
    const sourceId=dropSource?.dataset.sourceContainerId||'';
    pending.current={
      kind:item?'item':'marquee',itemId:item?.dataset.explorerId||null,sourceId,
      origin:{x:event.clientX,y:event.clientY},
      additive:event.ctrlKey||event.metaKey,original:new Set(selectionRef.current.selected),moved:false
    };
    // Prevent accidental text selection once the pointer starts dragging.
  },[]);

  useEffect(()=>{
    function move(event:PointerEvent) {
      const p=pending.current;if(!p)return;
      const point={x:event.clientX,y:event.clientY};
      if(!p.moved&&distance(p.origin,point)<5)return;
      p.moved=true;
      if(p.kind==='marquee') {
        const rect=rectFromPoints(p.origin,point);
        const hits=Array.from(document.querySelectorAll<HTMLElement>('[data-explorer-id]'))
          .filter(el=>el.getClientRects().length&&intersects(rect,el.getBoundingClientRect()))
          .map(el=>el.dataset.explorerId!).filter(Boolean);
        setSelection(previous=>selectMarquee(previous,hits,p.original,p.additive));
        setGesture({kind:'marquee',rect});
        return;
      }
      const current=selectionRef.current;
      const ids=p.itemId&&current.selected.has(p.itemId)?Array.from(current.selected):p.itemId?[p.itemId]:[];
      const element=document.elementFromPoint(point.x,point.y);
      const targetId=element?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
      const operation=dragOperation(event,'move');
      setGesture({kind:'drag',x:point.x,y:point.y,targetId,allowed:canDrop(ids,p.sourceId||'',targetId),operation,count:ids.length});
    }
    function up(event:PointerEvent) {
      const p=pending.current;if(!p)return;
      pending.current=null;
      const g=gestureRef.current;
      if(p.moved){
        preventNextClick.current=true;
        if(p.kind==='drag'&&false){/* reserved for native OLE integration */}
        if(p.kind==='item') {
          const current=selectionRef.current;
          const ids=p.itemId&&current.selected.has(p.itemId)?Array.from(current.selected):p.itemId?[p.itemId]:[];
          const point={x:event.clientX,y:event.clientY};
          const targetId=document.elementFromPoint(point.x,point.y)
            ?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
          if(canDrop(ids,p.sourceId||'',targetId)) optionsRef.current.onDrop?.({
            itemIds:ids,sourceId:p.sourceId||'',targetId:targetId!,operation:dragOperation(event)
          });
        }
      } else if(p.kind==='item'&&p.itemId){
        setSelection(previous=>selectItem(previous,p.itemId!,visibleRef.current,event));
      } else if(!p.additive){
        clear();
      }
      setGesture({kind:'idle'});
    }
    function cancel(){pending.current=null;setGesture({kind:'idle'})}
    window.addEventListener('pointermove',move);
    window.addEventListener('pointerup',up);
    window.addEventListener('pointercancel',cancel);
    window.addEventListener('blur',cancel);
    return ()=>{
      window.removeEventListener('pointermove',move);
      window.removeEventListener('pointerup',up);
      window.removeEventListener('pointercancel',cancel);
      window.removeEventListener('blur',cancel);
    };
  },[clear]);
  const gestureRef=useRef(gesture);gestureRef.current=gesture;
  useEffect(()=>{
    if(options.enabled===false)return;
    function key(event:KeyboardEvent) {
      if(isEditableTarget(event.target)||event.defaultPrevented)return;
      const mod=event.ctrlKey||event.metaKey;
      if(event.key==='Escape'){
        if(pending.current){pending.current=null;setGesture({kind:'idle'})}
        else clear();
        return;
      }
      if(mod&&event.key.toLowerCase()==='a'){
        event.preventDefault();
        setSelection(prev=>selectAll(visibleRef.current,prev));return;
      }
      const ids=Array.from(selectionRef.current.selected);
      if(mod&&event.key.toLowerCase()==='c'&&ids.length){
        event.preventDefault();clipboard.current={ids,operation:'copy'};optionsRef.current.onCopy?.(ids);return;
      }
      if(mod&&event.key.toLowerCase()==='x'&&ids.length){
        event.preventDefault();clipboard.current={ids,operation:'move'};optionsRef.current.onCut?.(ids);return;
      }
      if(mod&&event.key.toLowerCase()==='v'&&clipboard.current){
        event.preventDefault();optionsRef.current.onPaste?.(clipboard.current.ids,clipboard.current.operation);return;
      }
      if(event.key==='Delete'&&ids.length){event.preventDefault();optionsRef.current.onDelete?.(ids);return}
      if(event.key==='F2'&&ids.length===1){event.preventDefault();optionsRef.current.onRename?.(ids[0])}
    }
    document.addEventListener('keydown',key);
    return ()=>document.removeEventListener('keydown',key);
  },[options.enabled,clear]);
  useEffect(()=>{
    // Filter/project changes should not keep hidden selections.
    const visible=new Set(options.visibleIds);
    setSelection(prev=>{
      if([...prev.selected].every(id=>visible.has(id)))return prev;
      const selected=new Set([...prev.selected].filter(id=>visible.has(id)));
      return {...prev,selected,anchor:prev.anchor&&visible.has(prev.anchor)?prev.anchor:null};
    });
  },[options.visibleIds.join('\u0000')]);

  return {
    selectedIds:Array.from(selection.selected),
    selectedSet:selection.selected,
    gesture,
    onPointerDown,
    setSelectedIds,clear,select,toggle,
    consumeClick:()=>{
      if(preventNextClick.current){preventNextClick.current=false;return true}
      return false;
    }
  };
}
