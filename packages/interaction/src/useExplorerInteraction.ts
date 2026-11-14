import {useCallback,useEffect,useRef,useState} from 'react';
import {
  canDrop,createSelection,distance,dragItemIds,dragOperation,isEditableTarget,
  marqueeHits,rectFromPoints,selectAll,selectItem,selectMarquee
} from './model';
import type {DragIntent,ItemId,ModifierKeys,Point,Rect,Selection,Operation} from './model';

type Pending={
  pointerId:number;
  kind:'marquee'|'item';
  root:HTMLElement;
  origin:Point;
  itemId:string|null;
  itemIds:string[];
  sourceId:string;
  additive:boolean;
  original:Set<ItemId>;
  moved:boolean;
};
export type Gesture=
  | {kind:'idle'}
  | {kind:'marquee';rect:Rect}
  | {kind:'drag';x:number;y:number;targetId:string|null;allowed:boolean;operation:Operation;count:number};
export interface ExplorerOptions{
  visibleIds:readonly ItemId[];
  enabled?:boolean;
  onDrop?:(intent:DragIntent)=>void;
  onCopy?:(ids:string[])=>void;
  onCut?:(ids:string[])=>void;
  onPaste?:(ids:string[],operation:Operation)=>void;
  onDelete?:(ids:string[])=>void;
  onRename?:(id:string)=>void;
}
export function useExplorerInteraction(options:ExplorerOptions){
  const [selection,setSelection]=useState<Selection>(createSelection);
  const [gesture,setGesture]=useState<Gesture>({kind:'idle'});
  const selectionRef=useRef(selection);
  selectionRef.current=selection;
  const optionsRef=useRef(options);
  optionsRef.current=options;
  const visibleRef=useRef(options.visibleIds);
  visibleRef.current=options.visibleIds;
  const pending=useRef<Pending|null>(null);
  const clipboard=useRef<{ids:string[];operation:Operation}|null>(null);

  const setSelectedIds=useCallback((ids:string[])=>{
    setSelection({selected:new Set(ids),anchor:ids[0]||null,focused:ids.at(-1)||null});
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
    if(optionsRef.current.enabled===false || event.button!==0 || !event.isPrimary)return;
    const root=event.currentTarget;
    const target=event.target;
    if(!(target instanceof Element)||!root.contains(target)||isEditableTarget(target))return;
    // Buttons, checkboxes, navigation and editable controls handle their own clicks.
    if(target.closest('button, a, input, label, [role="checkbox"], [data-explorer-no-gesture]'))return;
    const item=target.closest<HTMLElement>('[data-explorer-id]');
    if(item&&!root.contains(item))return;
    const itemId=item?.dataset.explorerId||null;
    const sourceId=item?.closest<HTMLElement>('[data-source-container-id]')?.dataset.sourceContainerId
      ||root.dataset.sourceContainerId||'';
    pending.current={
      pointerId:event.pointerId,kind:itemId?'item':'marquee',root,
      origin:{x:event.clientX,y:event.clientY},itemId,
      itemIds:itemId?dragItemIds(itemId,selectionRef.current.selected):[],
      sourceId,additive:event.ctrlKey||event.metaKey,
      original:new Set(selectionRef.current.selected),moved:false
    };
  },[]);

  useEffect(()=>{
    function pointerMove(event:PointerEvent){
      const p=pending.current;
      if(!p||event.pointerId!==p.pointerId||optionsRef.current.enabled===false)return;
      const point={x:event.clientX,y:event.clientY};
      if(!p.moved&&distance(p.origin,point)<5)return;
      p.moved=true;
      if(event.cancelable)event.preventDefault();
      if(p.kind==='marquee'){
        const rect=rectFromPoints(p.origin,point);
        // Only query this workbench's active canvas, not every Explorer instance in the app.
        const bounds=p.root.getBoundingClientRect();
        const itemRects=Array.from(p.root.querySelectorAll<HTMLElement>('[data-explorer-id]'))
          .filter(el=>el.getClientRects().length>0)
          .map(el=>({id:el.dataset.explorerId!,rect:el.getBoundingClientRect()}));
        const hits=marqueeHits(rect,itemRects,bounds);
        setSelection(previous=>selectMarquee(previous,hits,p.original,p.additive));
        setGesture({kind:'marquee',rect});
        return;
      }
      const targetId=document.elementFromPoint(point.x,point.y)
        ?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
      setGesture({
        kind:'drag',x:point.x,y:point.y,targetId,
        allowed:canDrop(p.itemIds,p.sourceId,targetId),
        operation:dragOperation(event),count:p.itemIds.length
      });
    }

    function pointerUp(event:PointerEvent){
      const p=pending.current;
      if(!p||event.pointerId!==p.pointerId)return;
      pending.current=null;
      if(!p.moved){
        if(p.kind==='item'&&p.itemId){
          setSelection(previous=>selectItem(previous,p.itemId!,visibleRef.current,event));
        }else if(!p.additive){
          clear();
        }
      }else if(p.kind==='item'){
        const point={x:event.clientX,y:event.clientY};
        const targetId=document.elementFromPoint(point.x,point.y)
          ?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
        if(canDrop(p.itemIds,p.sourceId,targetId)){
          optionsRef.current.onDrop?.({
            itemIds:p.itemIds,sourceId:p.sourceId,targetId:targetId!,
            operation:dragOperation(event)
          });
        }
      }
      setGesture({kind:'idle'});
    }

    function cancel(event?:PointerEvent){
      if(event&&pending.current&&event.pointerId!==pending.current.pointerId)return;
      pending.current=null;
      setGesture({kind:'idle'});
    }
    window.addEventListener('pointermove',pointerMove,{passive:false});
    window.addEventListener('pointerup',pointerUp);
    window.addEventListener('pointercancel',cancel);
    const onBlur=()=>cancel();
    window.addEventListener('blur',onBlur);
    return ()=>{
      window.removeEventListener('pointermove',pointerMove);
      window.removeEventListener('pointerup',pointerUp);
      window.removeEventListener('pointercancel',cancel);
      window.removeEventListener('blur',onBlur);
    };
  },[clear]);

  useEffect(()=>{
    if(options.enabled===false){
      pending.current=null;
      setGesture({kind:'idle'});
      return;
    }
    function onKeyDown(event:KeyboardEvent){
      if(isEditableTarget(event.target)||event.defaultPrevented)return;
      const mod=event.ctrlKey||event.metaKey;
      if(event.key==='Escape'){
        if(pending.current){
          pending.current=null;
          setGesture({kind:'idle'});
          event.preventDefault();
        }else if(selectionRef.current.selected.size){
          clear();event.preventDefault();
        }
        return;
      }
      if(mod&&event.key.toLowerCase()==='a'){
        event.preventDefault();
        setSelection(prev=>selectAll(visibleRef.current,prev));
        return;
      }
      const ids=[...selectionRef.current.selected];
      if(mod&&event.key.toLowerCase()==='c'&&ids.length){
        event.preventDefault();clipboard.current={ids,operation:'copy'};
        optionsRef.current.onCopy?.(ids);return;
      }
      if(mod&&event.key.toLowerCase()==='x'&&ids.length){
        event.preventDefault();clipboard.current={ids,operation:'move'};
        optionsRef.current.onCut?.(ids);return;
      }
      if(mod&&event.key.toLowerCase()==='v'&&clipboard.current){
        event.preventDefault();
        optionsRef.current.onPaste?.(clipboard.current.ids,clipboard.current.operation);
        return;
      }
      if(event.key==='Delete'&&ids.length){
        event.preventDefault();optionsRef.current.onDelete?.(ids);return;
      }
      if(event.key==='F2'&&ids.length===1){
        event.preventDefault();optionsRef.current.onRename?.(ids[0]);
      }
    }
    document.addEventListener('keydown',onKeyDown);
    return ()=>document.removeEventListener('keydown',onKeyDown);
  },[options.enabled,clear]);

  const visibleKey=options.visibleIds.join('\u0000');
  useEffect(()=>{
    const visible=new Set(visibleRef.current);
    setSelection(prev=>{
      if([...prev.selected].every(id=>visible.has(id))
        &&(!prev.anchor||visible.has(prev.anchor)))return prev;
      const selected=new Set([...prev.selected].filter(id=>visible.has(id)));
      return {...prev,selected,anchor:prev.anchor&&visible.has(prev.anchor)?prev.anchor:null};
    });
  },[visibleKey]);

  return {
    selectedIds:[...selection.selected],
    selectedSet:selection.selected,gesture,onPointerDown,
    setSelectedIds,clear,select,toggle
  };
}
