/** Demo-only file assets with identities separate from biological replicates.
 * P2 will replace these with database-backed Asset and immutable AssetVersion IDs.
 */
import type {Assay,ExpGroup,Replica,Stage} from './data';

export type DemoAsset={
  id:string; sourceId:string; replicateId:string|null;
  containerId:string; stage:Stage; filename:string;
};
export const demoAssetId=(stage:Stage,sourceId:string):string=>`asset:${stage}:${sourceId}`;

export function selectableDemoAssets(
  assay:Assay,group:ExpGroup,replicates:readonly Replica[],
  mode:'overview'|'stage',stage:Stage,stageFilter:readonly Stage[]
):DemoAsset[]{
  const visible:readonly Stage[]=mode==='overview'?stageFilter:[stage];
  const result:DemoAsset[]=[];
  for(const rep of replicates){
    if(visible.includes('raw'))result.push({
      id:demoAssetId('raw',rep.id),sourceId:rep.id,replicateId:rep.id,
      containerId:group.id,stage:'raw',filename:rep.name
    });
    if(visible.includes('processed'))result.push({
      id:demoAssetId('processed',rep.id),sourceId:rep.id,replicateId:rep.id,
      containerId:group.id,stage:'processed',filename:rep.procName
    });
  }
  // Overview stats/display cells only describe sample metadata; they are not file assets.
  if(mode==='stage'&&stage==='stats')for(const f of assay.statFiles)result.push({
    id:demoAssetId('stats',f.id),sourceId:f.id,replicateId:null,
    containerId:`assay:${assay.id}`,stage:'stats',filename:f.name
  });
  if(mode==='stage'&&stage==='display')for(const f of assay.displayFiles)result.push({
    id:demoAssetId('display',f.id),sourceId:f.id,replicateId:null,
    containerId:`assay:${assay.id}`,stage:'display',filename:f.name
  });
  return result;
}
