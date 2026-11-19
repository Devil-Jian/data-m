/**
 * S12R production data adapter. This module contains no demonstration fixtures.
 * It reads existing S1/S2/S4 native protocols and preserves source scope and IDs.
 * It must not infer sample-level statistics or representative status from filenames.
 */
import {projectTreeApi} from './project-tree';
import type {ProjectTreeApi,ProjectTreeNode} from './project-tree';
import {experimentApi} from './experiment-tree';
import type {ExperimentApi,ExperimentGroup} from './experiment-tree';
import {assetApi} from './asset-browser';
import type {AssetApi,AssetRow} from './asset-browser';

export type ProductionStage = 'raw'|'processed'|'statistics'|'display';
export const PRODUCTION_STAGES:readonly ProductionStage[] =
  ['raw','processed','statistics','display'] as const;

export interface ProductionProject {
  id:string;
  name:string;
  themes:ProductionTheme[];
}
export interface ProductionTheme {
  id:string;
  name:string;
  assays:ProductionAssay[];
}
export interface ProductionAssay {id:string;name:string}

/** Sample-level cells never contain assay-wide files unless evidence is provided. */
export interface ReplicateMatrixRow {
  id:string;
  name:string;
  groupId:string;
  raw:AssetRow[];
  processed:AssetRow[];
  statistics:{scope:'assay';status:'unlinked';count:number};
  display:{scope:'assay';status:'unlinked';count:number};
}
export interface ProductionAssaySnapshot {
  assayId:string;
  groups:ExperimentGroup[];
  rows:ReplicateMatrixRow[];
  assayFiles:{statistics:AssetRow[];display:AssetRow[]};
  replicateCount:number;
  /** A page boundary is explicit; never silently truncate the asset listing. */
  incomplete:boolean;
}

export function productionProjectHierarchy(nodes:readonly ProjectTreeNode[]):ProductionProject[]{
  const projects=nodes.filter(node=>node.kind==='project');
  return projects.map(project=>({
    id:project.id,name:project.name,
    themes:nodes.filter(node=>node.kind==='theme'&&node.parentId===project.id).map(theme=>({
      id:theme.id,name:theme.name,
      assays:nodes.filter(node=>node.kind==='assay'&&node.parentId===theme.id).map(assay=>({
        id:assay.id,name:assay.name
      }))
    }))
  }));
}
export interface ProductionDataAdapter {
  loadProjects():Promise<ProductionProject[]>;
  loadAssay(assayId:string):Promise<ProductionAssaySnapshot>;
}

/**
 * S4 allows <=100 rows/page. Fetches all pages for the initial bounded
 * matrix; an explicit incomplete flag prevents any false completeness claim.
 * Future large-scale S12R-04 views should use server pagination/virtual lists.
 */
export function createProductionAdapter(deps:{
  projects:ProjectTreeApi;
  experiments:ExperimentApi;
  assets:AssetApi;
  maxPages?:number;
}):ProductionDataAdapter {
  const maxPages=deps.maxPages??10;
  if(!Number.isSafeInteger(maxPages)||maxPages<1)throw new Error('maxPages must be positive');
  async function listStage(assayId:string,stage:ProductionStage):Promise<{items:AssetRow[];incomplete:boolean;total:number}>{
    const items:AssetRow[]=[];
    const visited=new Set<string>();
    let cursor:string|undefined;
    let lastTotal=0;
    for(let i=0;i<maxPages;i++){
      const page=await deps.assets.list({assayId,stage,limit:100,...(cursor?{cursor}:{})});
      if(page.items.some(asset=>asset.stage!==stage))throw new Error('Asset stage mismatch');
      lastTotal=page.total;
      items.push(...page.items);
      if(!page.nextCursor)return {items,incomplete:items.length<page.total,total:page.total};
      if(visited.has(page.nextCursor))throw new Error('Repeated asset cursor');
      visited.add(page.nextCursor);
      cursor=page.nextCursor;
    }
    const total=items.length===0?0:Math.max(items.length,lastTotal);
    return {items,incomplete:true,total};
  }
  return {
    async loadProjects(){
      const result=await deps.projects({action:'list'});
      return productionProjectHierarchy(result.nodes);
    },
    async loadAssay(assayId){
      const tree=await deps.experiments({action:'list',assayId});
      const stages=await Promise.all(PRODUCTION_STAGES.map(stage=>listStage(assayId,stage)));
      const [raw,processed,statistics,display]=stages;
      // O(files + replicates) indexing: never scan every asset for every sample.
      const byReplicate=(files:AssetRow[])=>{
        const index=new Map<string,AssetRow[]>();
        for(const file of files){
          if(!file.replicateId)continue;
          const list=index.get(file.replicateId)??[];
          list.push(file);index.set(file.replicateId,list);
        }
        return index;
      };
      const rawIndex=byReplicate(raw.items),processedIndex=byReplicate(processed.items);
      // Note: never reassign assay-level files to each biological replicate.
      const rows=tree.groups.flatMap(group=>group.replicates.map(rep=>({
        id:rep.id,name:rep.label,groupId:group.id,
        raw:rawIndex.get(rep.id)??[],
        processed:processedIndex.get(rep.id)??[],
        statistics:{scope:'assay' as const,status:'unlinked' as const,count:statistics.total},
        display:{scope:'assay' as const,status:'unlinked' as const,count:display.total}
      })));
      return {
        assayId,groups:tree.groups,rows,
        assayFiles:{statistics:statistics.items,display:display.items},
        replicateCount:tree.replicateCount,
        incomplete:stages.some(page=>page.incomplete)
      };
    }
  };
}
export const productionAdapter=createProductionAdapter({
  projects:projectTreeApi,experiments:experimentApi,assets:assetApi
});
