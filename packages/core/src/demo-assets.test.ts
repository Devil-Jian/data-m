import {describe,it,expect} from 'vitest';
import {INITIAL_PROJECTS} from './data';
import {demoAssetId,selectableDemoAssets} from './demo-assets';
const assay=INITIAL_PROJECTS[0].themes[0].assays[0];
const group=assay.groups[0];
describe('demo file asset identity',()=>{
 it('raw/processed assets refer to one replicate but have different IDs',()=>{
   const rep=group.replicates[0];
   const assets=selectableDemoAssets(assay,group,[rep],'overview','raw',['raw','processed']);
   expect(assets.map(a=>a.id)).toEqual([demoAssetId('raw',rep.id),demoAssetId('processed',rep.id)]);
   expect(assets.every(a=>a.id!==rep.id&&a.replicateId===rep.id)).toBe(true);
 });
 it('preserves IDs across overview/stage views',()=>{
   const rep=group.replicates[0];
   const overview=selectableDemoAssets(assay,group,[rep],'overview','raw',['raw']);
   const stage=selectableDemoAssets(assay,group,[rep],'stage','raw',['raw']);
   expect(overview.map(a=>a.id)).toEqual(stage.map(a=>a.id));
 });
 it('assay-wide stats/display files are not biological replicates',()=>{
   for(const stage of ['stats','display'] as const){
     const assets=selectableDemoAssets(assay,group,group.replicates,'stage',stage,[]);
     expect(assets).toHaveLength(stage==='stats'?assay.statFiles.length:assay.displayFiles.length);
     expect(assets.every(a=>a.replicateId===null&&a.containerId===`assay:${assay.id}`)).toBe(true);
   }
 });
 it('doubling visible files never changes biological sample count',()=>{
   const n=group.replicates.length;
   const assets=selectableDemoAssets(assay,group,group.replicates,'overview','raw',['raw','processed','stats','display']);
   expect(assets).toHaveLength(n*2);
   expect(group.replicates).toHaveLength(n);
   expect(new Set(assets.map(a=>a.id)).size).toBe(n*2);
 });
});
