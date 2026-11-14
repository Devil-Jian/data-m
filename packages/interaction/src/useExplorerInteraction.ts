import {useCallback,useEffect,useRef,useState} from 'react';
import {
  canDrop,contentMarqueeHits,contentPoint,createSelection,distance,dragItemIds,
  dragOperation,edgeScrollVelocity,isEditableTarget,rectFromPoints,
  selectAll,selectItem,selectMarquee
} from './model';
import type {
  DragIntent,ItemId,ModifierKeys,Operation,Point,PositionedItem,Rect,Selection
} from './model';

type Pending={
  pointerId:number;
  kind:'marquee'|'item';
  root:HTMLElement;
  host:HTMLElement;
  origin:Point;
  originContent:Point;
  latest:Point;
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

/** Return content-space rectangles for all logical items, including unmounted virtual rows. */
export type VirtualItemLayout=(host:HTMLElement)=>readonly PositionedItem[];
export interface ExplorerOptions{
  visibleIds:readonly ItemId[];
  enabled?:boolean;
  getVirtualItems?:VirtualItemLayout;
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
  const selectionRef=useRef(selection);selectionRef.current=selection;
  const optionsRef=useRef(options);optionsRef.current=options;
  const visibleRef=useRef(options.visibleIds);visibleRef.current=options.visibleIds;
  const pending=useRef<Pending|null>(null);
  const clipboard=useRef<{ids:string[];operation:Operation}|null>(null);
  const frame=useRef<number|null>(null);

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
    if(optionsRef.current.enabled===false||event.button!==0||!event.isPrimary)return;
    const root=event.currentTarget,target=event.target;
    if(!(target instanceof Element)||!root.contains(target)||isEditableTarget(target))return;
    if(target.closest('button, a, input, label, [role="checkbox"], [data-explorer-no-gesture]'))return;
    const item=target.closest<HTMLElement>('[data-explorer-id]');
    if(item&&!root.contains(item))return;
    const itemId=item?.dataset.explorerId||null;
    // A nested scrolling view may have its own content coordinate system.
    // Otherwise use the stable Explorer root as the scroll container.
    const child=target.closest<HTMLElement>('[data-explorer-scroll-host]');
    // Prefer the outer vertical scroller when an inner table only overflows horizontally.
    const host=child&&root.contains(child)&&child.scrollHeight>child.clientHeight+1
      ?child:root;
    const origin={x:event.clientX,y:event.clientY};
    const bounds=host.getBoundingClientRect();
    pending.current={
      pointerId:event.pointerId,kind:itemId?'item':'marquee',root,host,
      origin,originContent:contentPoint(origin,bounds,host.scrollLeft,host.scrollTop),
      latest:origin,itemId,
      itemIds:itemId?dragItemIds(itemId,selectionRef.current.selected):[],
      sourceId:item?.closest<HTMLElement>('[data-source-container-id]')?.dataset.sourceContainerId
        ||root.dataset.sourceContainerId||'',
      additive:event.ctrlKey||event.metaKey,original:new Set(selectionRef.current.selected),
      moved:false
    };
  },[]);

  useEffect(()=>{
    function stopFrame(){
      if(frame.current!==null){
        cancelAnimationFrame(frame.current);
        frame.current=null;
      }
    }
    function paintMarquee(p:Pending){
      const bounds=p.host.getBoundingClientRect();
      const point=contentPoint(p.latest,bounds,p.host.scrollLeft,p.host.scrollTop);
      const rect=rectFromPoints(p.originContent,point);
      const allowed=new Set(visibleRef.current);
      const virtual=optionsRef.current.getVirtualItems;
      const items:readonly PositionedItem[]=virtual&&p.host.dataset.explorerVirtual==='true'?virtual(p.host):
        Array.from(p.root.querySelectorAll<HTMLElement>('[data-explorer-id]'))
          .filter(el=>el.getClientRects().length>0)
          .map(el=>{
            const r=el.getBoundingClientRect();
            return {
              id:el.dataset.explorerId||'',
              rect:{
                left:r.left-bounds.left+p.host.scrollLeft,
                right:r.right-bounds.left+p.host.scrollLeft,
                top:r.top-bounds.top+p.host.scrollTop,
                bottom:r.bottom-bounds.top+p.host.scrollTop
              }
            };
          });
      const hits=contentMarqueeHits(rect,items,allowed);
      setSelection(previous=>selectMarquee(previous,hits,p.original,p.additive));
      // Draw in viewport space. This rectangle is *visual only*;
      // actual selection is calculated in scroll-content space.
      setGesture({kind:'marquee',rect:rectFromPoints(p.origin,p.latest)});
    }
    function tick(){
      frame.current=null;
      const p=pending.current;
      if(!p||!p.moved||p.kind!=='marquee'||optionsRef.current.enabled===false)return;
      const host=p.host;
      const box=host.getBoundingClientRect();
      const speed=edgeScrollVelocity(p.latest,box);
      const oldX=host.scrollLeft,oldY=host.scrollTop;
      if(speed.x||speed.y){
        host.scrollLeft+=speed.x;
        host.scrollTop+=speed.y;
      }
      if(host.scrollLeft!==oldX||host.scrollTop!==oldY)paintMarquee(p);
      if((speed.x||speed.y)&&(host.scrollLeft!==oldX||host.scrollTop!==oldY)){
        frame.current=requestAnimationFrame(tick);
      }
    }
    function schedule(){
      stopFrame();
      const p=pending.current;
      if(p?.moved&&p.kind==='marquee')frame.current=requestAnimationFrame(tick);
    }
    function pointerMove(event:PointerEvent){
      const p=pending.current;
      if(!p||event.pointerId!==p.pointerId||optionsRef.current.enabled===false)return;
      const point={x:event.clientX,y:event.clientY};
      if(!p.moved&&distance(p.origin,point)<5)return;
      p.moved=true;
      p.latest=point;
      if(event.cancelable)event.preventDefault();
      if(p.kind==='marquee'){
        paintMarquee(p);
        schedule();
      }else{
        const targetId=document.elementFromPoint(point.x,point.y)
          ?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
        setGesture({
          kind:'drag',x:point.x,y:point.y,targetId,
          allowed:canDrop(p.itemIds,p.sourceId,targetId),
          operation:dragOperation(event),count:p.itemIds.length
        });
      }
    }
    function pointerUp(event:PointerEvent){
      const p=pending.current;
      if(!p||event.pointerId!==p.pointerId)return;
      pending.current=null;
      stopFrame();
      if(!p.moved){
        if(p.kind==='item'&&p.itemId){
          setSelection(previous=>selectItem(previous,p.itemId!,visibleRef.current,event));
        }else if(!p.additive)clear();
      }else if(p.kind==='item'){
        const targetId=document.elementFromPoint(event.clientX,event.clientY)
          ?.closest<HTMLElement>('[data-drop-container-id]')?.dataset.dropContainerId||null;
        if(canDrop(p.itemIds,p.sourceId,targetId)){
          optionsRef.current.onDrop?.({
            itemIds:p.itemIds,sourceId:p.sourceId,targetId:targetId!,
            operation:dragOperation(event)
          });
        }
      }else{
        p.latest={x:event.clientX,y:event.clientY};
        paintMarquee(p);
      }
      setGesture({kind:'idle'});
    }
    function cancel(event?:PointerEvent){
      if(event&&pending.current&&event.pointerId!==pending.current.pointerId)return;
      pending.current=null;
      stopFrame();
      setGesture({kind:'idle'});
    }
    function onScroll(event:Event){
      const p=pending.current;
      if(!p||!p.moved||p.kind!=='marquee')return;
      if(event.target===p.host||p.host.contains(event.target as Node))paintMarquee(p);
    }
    const onBlur=()=>cancel();
    window.addEventListener('pointermove',pointerMove,{passive:false});
    window.addEventListener('pointerup',pointerUp);
    window.addEventListener('pointercancel',cancel);
    window.addEventListener('blur',onBlur);
    document.addEventListener('scroll',onScroll,true);
    return ()=>{
      stopFrame();
      window.removeEventListener('pointermove',pointerMove);
      window.removeEventListener('pointerup',pointerUp);
      window.removeEventListener('pointercancel',cancel);
      window.removeEventListener('blur',onBlur);
      document.removeEventListener('scroll',onScroll,true);
    };
  },[clear]);

  useEffect(()=>{
    if(options.enabled===false){
      pending.current=null;
      if(frame.current!==null)cancelAnimationFrame(frame.current);
      frame.current=null;
      setGesture({kind:'idle'});
      return;
    }
    function onKeyDown(event:KeyboardEvent){
      if(isEditableTarget(event.target)||event.defaultPrevented)return;
      const mod=event.ctrlKey||event.metaKey;
      if(event.key==='Escape'){
        if(pending.current){
          pending.current=null;
          if(frame.current!==null)cancelAnimationFrame(frame.current);
          frame.current=null;
          setGesture({kind:'idle'});event.preventDefault();
        }else if(selectionRef.current.selected.size){
          clear();event.preventDefault();
        }
        return;
      }
      if(mod&&event.key.toLowerCase()==='a'){
        event.preventDefault();setSelection(prev=>selectAll(visibleRef.current,prev));return;
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
        event.preventDefault();optionsRef.current.onPaste?.(
          clipboard.current.ids,clipboard.current.operation
        );return;
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
      if([...prev.selected].every(id=>visible.has(id))&&
        (!prev.anchor||visible.has(prev.anchor)))return prev;
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
