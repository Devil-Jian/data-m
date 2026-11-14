import {describe,expect,it} from 'vitest';
import {
  canDrop,createSelection,dragItemIds,dragOperation,intersects,marqueeHits,
  rectFromPoints,selectAll,selectItem,selectMarquee
} from './model';
const ids=['a','b','c','d','e'];
describe('Explorer selection',()=>{
  it('plain click replaces the selection',()=>{
    const s=selectItem(createSelection(),'b',ids);
    expect([...s.selected]).toEqual(['b']);
    expect(s.anchor).toBe('b');
  });
  it('Ctrl toggles individual entries',()=>{
    const first=selectItem(createSelection(),'a',ids);
    const second=selectItem(first,'c',ids,{ctrlKey:true});
    expect([...second.selected]).toEqual(['a','c']);
    expect([...selectItem(second,'c',ids,{ctrlKey:true}).selected]).toEqual(['a']);
  });
  it('Shift selects inclusive reverse range',()=>{
    const from=selectItem(createSelection(),'d',ids);
    expect([...selectItem(from,'b',ids,{shiftKey:true}).selected]).toEqual(['b','c','d']);
  });
  it('Ctrl+Shift accumulates the range without erasing previous selection',()=>{
    const a=selectItem(createSelection(),'a',ids);
    const b=selectItem(a,'d',ids,{ctrlKey:true});
    const c=selectItem(b,'b',ids,{ctrlKey:true,shiftKey:true});
    expect(new Set(c.selected)).toEqual(new Set(['a','b','c','d']));
  });
  it('Ctrl+A only targets visible IDs',()=>{
    expect([...selectAll(['c','d'],createSelection()).selected]).toEqual(['c','d']);
  });
  it('additive marquee uses snapshot, not previous transient hits',()=>{
    const base=selectItem(createSelection(),'a',ids);
    const first=selectMarquee(base,['c'],base.selected,true);
    const second=selectMarquee(first,['d'],base.selected,true);
    expect([...second.selected]).toEqual(['a','d']);
  });
});
describe('Explorer pointer gesture rules',()=>{
  it('handles reverse marquee coordinates',()=>{
    expect(rectFromPoints({x:60,y:70},{x:10,y:20}))
      .toEqual({left:10,right:60,top:20,bottom:70});
  });
  it('limits marquee to the active viewport and ignores outside items',()=>{
    const viewport={left:0,top:0,right:200,bottom:200};
    const items=[
      {id:'inside',rect:{left:20,top:20,right:40,bottom:40}},
      {id:'out-of-scope',rect:{left:300,top:20,right:340,bottom:40}},
      {id:'clipped',rect:{left:190,top:20,right:230,bottom:40}}
    ];
    const hits=marqueeHits({left:10,top:10,right:400,bottom:100},items,viewport);
    expect(hits).toEqual(['inside','clipped']);
  });
  it('returns empty when marquee is fully outside the current canvas',()=>{
    expect(marqueeHits(
      {left:300,right:400,top:20,bottom:40},
      [{id:'a',rect:{left:10,right:30,top:20,bottom:40}}],
      {left:0,right:100,top:0,bottom:100}
    )).toEqual([]);
  });
  it('dragging a selected item carries the whole selection',()=>{
    const selected=new Set(['a','c']);
    expect(dragItemIds('a',selected)).toEqual(['a','c']);
    expect(dragItemIds('b',selected)).toEqual(['b']);
  });
  it('handles drop validity and key modifiers',()=>{
    expect(dragOperation({ctrlKey:true})).toBe('copy');
    expect(dragOperation({shiftKey:true})).toBe('move');
    expect(canDrop(['a'],'g1','g1')).toBe(false);
    expect(canDrop(['a'],'g1','g2')).toBe(true);
    expect(canDrop([],'g1','g2')).toBe(false);
    expect(canDrop(['a'],'g1',null)).toBe(false);
  });
  it('intersection includes border contact but not separated boxes',()=>{
    const box={left:10,right:60,top:20,bottom:70};
    expect(intersects(box,{left:60,right:90,top:30,bottom:45})).toBe(true);
    expect(intersects(box,{left:61,right:90,top:30,bottom:45})).toBe(false);
  });
});
