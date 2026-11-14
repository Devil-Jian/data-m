/** Framework-independent Explorer selection and drag rules. */
export type ItemId = string;
export type Operation = 'move' | 'copy';
export type ModifierKeys = { ctrlKey?: boolean; shiftKey?: boolean; metaKey?: boolean };
export type Selection = { selected: Set<ItemId>; anchor: ItemId | null; focused: ItemId | null };
export type Rect = { left:number; right:number; top:number; bottom:number };
export type Point = { x:number;y:number };
export type DragIntent = { itemIds:ItemId[]; sourceId:string; targetId:string; operation:Operation };
export const createSelection = (): Selection => ({selected:new Set(),anchor:null,focused:null});

export function selectItem(previous:Selection, id:ItemId, visibleOrder:readonly ItemId[], mods:ModifierKeys={}):Selection {
  const ctrl=!!(mods.ctrlKey||mods.metaKey);
  if(mods.shiftKey && previous.anchor && visibleOrder.includes(previous.anchor)) {
    const start=visibleOrder.indexOf(previous.anchor),end=visibleOrder.indexOf(id);
    if(end>=0) {
      const selected=ctrl?new Set(previous.selected):new Set<ItemId>();
      for(let i=Math.min(start,end);i<=Math.max(start,end);i++)selected.add(visibleOrder[i]);
      return {selected,anchor:previous.anchor,focused:id};
    }
  }
  if(ctrl) {
    const selected=new Set(previous.selected);
    if(selected.has(id)) selected.delete(id); else selected.add(id);
    return {selected,anchor:id,focused:id};
  }
  return {selected:new Set([id]),anchor:id,focused:id};
}
export function selectAll(visibleOrder:readonly ItemId[],previous:Selection):Selection {
  return {selected:new Set(visibleOrder),anchor:previous.anchor&&visibleOrder.includes(previous.anchor)?previous.anchor:visibleOrder[0]||null,focused:visibleOrder.at(-1)||null};
}
export function selectMarquee(previous:Selection,hitIds:readonly ItemId[],base:ReadonlySet<ItemId>,additive:boolean):Selection {
  return {selected:new Set(additive?[...base,...hitIds]:hitIds),anchor:previous.anchor,focused:previous.focused};
}
export function intersects(a:Rect,b:Rect):boolean {
  return a.left<=b.right && a.right>=b.left && a.top<=b.bottom && a.bottom>=b.top;
}
export function rectFromPoints(a:Point,b:Point):Rect {
  return {left:Math.min(a.x,b.x),top:Math.min(a.y,b.y),right:Math.max(a.x,b.x),bottom:Math.max(a.y,b.y)};
}
export function distance(a:Point,b:Point):number{return Math.hypot(a.x-b.x,a.y-b.y)}
export function dragOperation(mods:ModifierKeys,defaultOperation:Operation='move'):Operation {
  if(mods.ctrlKey||mods.metaKey)return 'copy';
  if(mods.shiftKey)return 'move';
  return defaultOperation;
}
export function canDrop(ids:readonly ItemId[],sourceId:string,targetId:string|null):boolean {
  return !!targetId && targetId!==sourceId && ids.length>0;
}
export function isEditableTarget(target:EventTarget|null):boolean {
  if(!(target instanceof Element))return false;
  return !!target.closest('input, textarea, select, [contenteditable="true"], [role="textbox"], [role="combobox"]');
}

/** Snapshot the dragged item set at pointer-down: dragging a selected item keeps the whole selection. */
export function dragItemIds(id:ItemId, current:ReadonlySet<ItemId>):ItemId[]{
  return current.has(id) ? [...current] : [id];
}
/** Hit test only items in the current Explorer scope. Item positions are viewport-relative. */
export function marqueeHits(
  area:Rect,
  items:ReadonlyArray<{id:ItemId;rect:Rect}>,
  viewport:Rect
):ItemId[]{
  const clipped:Rect={
    left:Math.max(area.left,viewport.left),
    right:Math.min(area.right,viewport.right),
    top:Math.max(area.top,viewport.top),
    bottom:Math.min(area.bottom,viewport.bottom)
  };
  if(clipped.left>clipped.right||clipped.top>clipped.bottom)return [];
  return items.filter(item=>intersects(clipped,item.rect)&&intersects(item.rect,viewport)).map(item=>item.id);
}
