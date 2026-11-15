import {describe,expect,it} from 'vitest';
import {INITIAL_PROJECTS} from './data';
import {
  createHistory,historyStep,locationExists,sameLocation,visitLocation
} from './navigation-history';
import type {NavigationLocation} from './navigation-history';

const first:NavigationLocation={
  module:'experiment',projectId:'prj-01',themeId:'theme-ox',
  assayId:'assay-ros',groupId:'ros-g1',paperId:'p1',panelId:null
};
const other={...first,groupId:'ros-g2'};
const third={...first,module:'paper' as const};

describe('Explorer session navigation',()=>{
  it('does not store duplicate visits to the same directory',()=>{
    const start=createHistory(first);
    expect(visitLocation(start,{...first})).toBe(start);
    expect(start.entries).toHaveLength(1);
  });
  it('back, forward and back bounds never create additional visits',()=>{
    const visited=visitLocation(visitLocation(createHistory(first),other),third);
    expect(historyStep(visited,-1).entries[1]).toEqual(other);
    expect(historyStep(visited,-1).index).toBe(1);
    expect(historyStep(visited,1)).toBe(visited);
    expect(historyStep(createHistory(first),-1)).toEqual(createHistory(first));
    expect(historyStep(visited,1)).toBe(visited);
    expect(historyStep(historyStep(visited,-1),1).index).toBe(2);
  });
  it('new navigation following Back truncates the forward branch',()=>{
    const full=visitLocation(visitLocation(createHistory(first),other),third);
    const back=historyStep(full,-1);
    const newRoute={...first,groupId:'ros-g3'};
    const branched=visitLocation(back,newRoute);
    expect(branched.entries).toEqual([first,other,newRoute]);
    expect(historyStep(branched,1)).toBe(branched);
  });
  it('limits the history to the last 50 views',()=>{
    let history=createHistory(first);
    for(let i=0;i<80;i++)history=visitLocation(history,{...first,groupId:'group-'+i});
    expect(history.entries).toHaveLength(50);
    expect(history.index).toBe(49);
    expect(history.entries[0].groupId).toBe('group-30');
  });
  it('stores project/paper/Panel identity separately from experiment containers',()=>{
    const a={...first,module:'paper' as const,panelId:null};
    const b={...a,panelId:'panel-123'};
    expect(sameLocation(a,b)).toBe(false);
    expect(sameLocation(first,a)).toBe(false);
  });
  it('rejects stale project and group identities before restoring',()=>{
    expect(locationExists(first,INITIAL_PROJECTS)).toBe(true);
    expect(locationExists({...first,projectId:'deleted-project'},INITIAL_PROJECTS)).toBe(false);
    expect(locationExists({...first,groupId:'deleted-group'},INITIAL_PROJECTS)).toBe(false);
    expect(locationExists({...first,assayId:'deleted-assay'},INITIAL_PROJECTS)).toBe(false);
  });
  it('does not restore an invalid paper or panel',()=>{
    expect(locationExists({...first,module:'paper',paperId:'missing'},INITIAL_PROJECTS)).toBe(false);
    expect(locationExists({...first,module:'paper',panelId:'nonexistent-panel'},INITIAL_PROJECTS)).toBe(false);
  });
});
