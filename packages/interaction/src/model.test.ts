import {describe,expect,it} from 'vitest';
import {
  canDrop,createSelection,dragOperation,intersects,rectFromPoints,
  selectAll,selectItem,selectMarquee
} from './model';

const ids=['a','b','c','d','e'];
describe('Explorer selection engine',()=>{
  it('plain click replaces selection and sets anchor',()=>{
    const s=selectItem(createSelection(),'b',ids);
    expect([...s.selected]).toEqual(['b']);expect(s.anchor).toBe('b');
  });
  it('Ctrl click toggles and updates anchor',()=>{
    const a=selectItem(createSelection(),'a',ids);
    const b=selectItem(a,'c',ids,{ctrlKey:true});
    expect([...b.selected]).toEqual(['a','c']);
    expect([...selectItem(b,'c',ids,{ctrlKey:true}).selected]).toEqual(['a']);
  });
  it('Shift selects inclusive range both directions',()=>{
    const a=selectItem(createSelection(),'d',ids);
    expect([...selectItem(a,'b',ids,{shiftKey:true}).selected]).toEqual(['b','c','d']);
  });
  it('Ctrl+Shift adds range to existing selection',()=>{
    const a=selectItem(createSelection(),'a',ids);
    const b=selectItem(a,'d',ids,{ctrlKey:true});
    const c=selectItem(b,'b',ids,{ctrlKey:true,shiftKey:true});
    expect([...c.selected]).toEqual(['a','d','b','c']);
  });
  it('select all respects visible order',()=>{
    expect([...selectAll(['c','d'],createSelection()).selected]).toEqual(['c','d']);
  });
  it('marquee can replace or add to original selection',()=>{
    const a=selectItem(createSelection(),'a',ids);
    expect([...selectMarquee(a,['c'],a.selected,false).selected]).toEqual(['c']);
    expect([...selectMarquee(a,['c'],a.selected,true).selected]).toEqual(['a','c']);
  });
});
describe('Explorer drag rules',()=>{
  it('handles reverse box selection and boundaries',()=>{
    const box=rectFromPoints({x:60,y:70},{x:10,y:20});
    expect(box).toEqual({left:10,right:60,top:20,bottom:70});
    expect(intersects(box,{left:60,right:90,top:30,bottom:45})).toBe(true);
    expect(intersects(box,{left:61,right:90,top:30,bottom:45})).toBe(false);
  });
  it('Ctrl copies, Shift moves, default moves',()=>{
    expect(dragOperation({})).toBe('move');
    expect(dragOperation({ctrlKey:true})).toBe('copy');
    expect(dragOperation({shiftKey:true})).toBe('move');
    expect(dragOperation({ctrlKey:true,shiftKey:true})).toBe('copy');
  });
  it('rejects invalid or same-container drops',()=>{
    expect(canDrop(['a'],'g1','g1')).toBe(false);
    expect(canDrop(['a'],'g1','g2')).toBe(true);
    expect(canDrop([],'g1','g2')).toBe(false);
    expect(canDrop(['a'],'g1',null)).toBe(false);
  });
});
