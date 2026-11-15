/** Pure navigation history for the local-first workbench.
 *
 * History stores stable container IDs, never OS paths or biological sample contents.
 * It is a session-only UI stack and does not mutate files or browser history.
 */
import type {ModuleName,Project} from './data';

export type NavigationLocation={
  module:ModuleName;
  projectId:string;
  themeId:string;
  assayId:string;
  groupId:string;
  paperId:string;
  panelId:string|null;
};
export type NavigationHistory={
  entries:NavigationLocation[];
  index:number;
};

export function sameLocation(a:NavigationLocation,b:NavigationLocation):boolean{
  return a.module===b.module&&a.projectId===b.projectId&&
    a.themeId===b.themeId&&a.assayId===b.assayId&&a.groupId===b.groupId&&
    a.paperId===b.paperId&&a.panelId===b.panelId;
}

export function createHistory(first:NavigationLocation):NavigationHistory{
  return {entries:[first],index:0};
}

/** A new navigation after Back removes all previously reachable Forward entries. */
export function visitLocation(
  history:NavigationHistory,
  next:NavigationLocation,
  capacity=50
):NavigationHistory{
  if(sameLocation(history.entries[history.index],next))return history;
  const entries=[...history.entries.slice(0,history.index+1),next];
  const limit=Math.max(1,Math.trunc(capacity));
  const bounded=entries.length>limit?entries.slice(entries.length-limit):entries;
  return {entries:bounded,index:bounded.length-1};
}

export function historyStep(
  history:NavigationHistory,
  offset:-1|1
):NavigationHistory{
  const nextIndex=history.index+offset;
  if(nextIndex<0||nextIndex>=history.entries.length)return history;
  return {...history,index:nextIndex};
}

/** Refuse stale/deleted container routes instead of silently falling back. */
export function locationExists(
  location:NavigationLocation,
  projects:readonly Project[]
):boolean{
  const project=projects.find(p=>p.id===location.projectId);
  if(!project)return false;
  const theme=project.themes.find(t=>t.id===location.themeId);
  if(!theme)return false;
  const assay=theme.assays.find(a=>a.id===location.assayId);
  if(!assay||!assay.groups.some(g=>g.id===location.groupId))return false;
  if(location.module==='paper'){
    const paper=project.papers.find(p=>p.id===location.paperId);
    if(!paper){
      return location.paperId===''&&location.panelId===null&&project.papers.length===0;
    }
    if(location.panelId&&!paper.figures.some(f=>f.panels.some(p=>p.id===location.panelId)))return false;
  }
  return true;
}

/** Walk past stale/deleted navigation targets without restoring fallback folders. */
export function findHistoryTarget(
  history:NavigationHistory,
  offset:-1|1,
  available:(location:NavigationLocation)=>boolean
):NavigationHistory{
  let target=historyStep(history,offset);
  while(target!==history){
    if(available(target.entries[target.index]))return target;
    const subsequent=historyStep(target,offset);
    if(subsequent===target)break;
    target=subsequent;
  }
  return history;
}
