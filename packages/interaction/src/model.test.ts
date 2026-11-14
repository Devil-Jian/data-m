import {describe,expect,it} from 'vitest';
import {
  canDrop,contentMarqueeHits,contentPoint,createSelection,dragItemIds,dragOperation,
  edgeScrollVelocity,intersects,marqueeHits,
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

describe('scroll-content selection including virtualized rows',()=>{
  it('maps viewport pointer to scroll-content coordinates',()=>{
    expect(contentPoint({x:130,y:105},{left:100,top:50,right:500,bottom:450},20,1000))
      .toEqual({x:50,y:1055});
  });
  it('selects offscreen logical item rectangles even when DOM items are not mounted',()=>{
    const items=Array.from({length:10_000},(_,i)=>({
      id:'asset-'+i,
      rect:{left:0,right:250,top:i*28,bottom:(i+1)*28}
    }));
    const all=new Set(items.map(a=>a.id));
    const hit=contentMarqueeHits(
      {left:4,right:200,top:28*200,bottom:28*250},items,all
    );
    expect(hit[0]).toBe('asset-199');
    expect(hit.at(-1)).toBe('asset-250');
    expect(hit.length).toBe(52);
  });
  it('filters virtual hits by currently valid visible IDs',()=>{
    const items=[
      {id:'raw-a',rect:{left:0,right:30,top:0,bottom:30}},
      {id:'processed-a',rect:{left:0,right:30,top:35,bottom:65}}
    ];
    expect(contentMarqueeHits(
      {left:0,right:40,top:0,bottom:70},items,new Set(['processed-a'])
    )).toEqual(['processed-a']);
  });
  it('returns zero inside a safe viewport and proportional speed at edges',()=>{
    const viewport={left:0,right:300,top:100,bottom:500};
    expect(edgeScrollVelocity({x:150,y:300},viewport)).toEqual({x:0,y:0});
    const near=edgeScrollVelocity({x:290,y:490},viewport);
    expect(near.x).toBeGreaterThan(0);
    expect(near.y).toBeGreaterThan(0);
    expect(near.x).toBeLessThan(22);
    expect(edgeScrollVelocity({x:1,y:101},viewport).x).toBeLessThan(0);
    expect(edgeScrollVelocity({x:1,y:101},viewport).y).toBeLessThan(0);
  });
  it('clamps velocity when pointer leaves the viewport',()=>{
    const v=edgeScrollVelocity({x:900,y:-900},{left:0,right:300,top:0,bottom:300});
    expect(v).toEqual({x:22,y:-22});
  });
});
