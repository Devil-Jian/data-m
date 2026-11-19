import {describe,expect,it,vi} from 'vitest';
import {createProductionAdapter,productionProjectHierarchy} from './production-adapter';
import type {AssetRow,AssetApi} from './asset-browser';

const hash='a'.repeat(64);
function asset(id:string,stage:AssetRow['stage'],replicateId:string|null):AssetRow{
  return {assetId:'asset-'+id,assetLinkId:'link-'+id,assetVersionId:'version-'+id,
    versionNumber:1,name:id,stage,groupId:replicateId?'group-a':null,
    replicateId,sha256:hash,sizeBytes:3,referenced:false};
}

describe('S12R production adapter',()=>{
  it('builds projects from native IDs without creating demo records',()=>{
    expect(productionProjectHierarchy([
      {id:'project-a',kind:'project',parentId:null,name:'真实项目'},
      {id:'theme-a',kind:'theme',parentId:'project-a',name:'主题'},
      {id:'assay-a',kind:'assay',parentId:'theme-a',name:'ROS'}
    ])).toEqual([{id:'project-a',name:'真实项目',
      themes:[{id:'theme-a',name:'主题',assays:[{id:'assay-a',name:'ROS'}]}]}]);
    expect(productionProjectHierarchy([])).toEqual([]);
  });

  it('keeps replicate Raw/Processed separate from assay Statistics/Display',async()=>{
    const files=[
      asset('raw1','raw','rep1'),asset('raw2','raw','rep1'),
      asset('proc1','processed','rep1'),asset('stat1','statistics',null),
      asset('display1','display',null)
    ];
    const assets:AssetApi={
      list:vi.fn(async ({stage})=>({items:files.filter(x=>x.stage===stage),total:files.filter(x=>x.stage===stage).length,nextCursor:null})),
      detail:vi.fn()
    };
    const adapter=createProductionAdapter({
      projects:vi.fn(async()=>({nodes:[]})),
      experiments:vi.fn(async()=>({assayId:'assay-a',replicateCount:2,stages:[],
        groups:[{id:'group-a',name:'Control',replicates:[{id:'rep1',label:'R1'},{id:'rep2',label:'R2'}]}]})),
      assets
    });
    const result=await adapter.loadAssay('assay-a');
    expect(result.replicateCount).toBe(2);
    expect(result.rows[0].raw.map(x=>x.assetLinkId)).toEqual(['link-raw1','link-raw2']);
    expect(result.rows[0].processed.map(x=>x.assetLinkId)).toEqual(['link-proc1']);
    expect(result.rows[1].raw).toEqual([]);
    expect(result.rows[0].statistics).toEqual({scope:'assay',status:'unlinked',count:1});
    expect(result.rows[1].display).toEqual({scope:'assay',status:'unlinked',count:1});
    expect(result.assayFiles.statistics[0].replicateId).toBeNull();
    expect(result.incomplete).toBe(false);
  });

  it('flags incomplete pages rather than treating a limit as full results',async()=>{
    const adapter=createProductionAdapter({
      projects:vi.fn(async()=>({nodes:[]})),
      experiments:vi.fn(async()=>({assayId:'assay-a',replicateCount:0,groups:[],stages:[]})),
      assets:{list:vi.fn(async({stage})=>({
        items:[asset('file-'+stage,stage!,null)],total:100,nextCursor:'1'
      })),detail:vi.fn()},
      maxPages:1
    });
    expect((await adapter.loadAssay('assay-a')).incomplete).toBe(true);
  });
});


describe('S12R replicate scope safety',()=>{
  it.each([
    ['missing replicate',null,'group-a'],
    ['foreign replicate','rep-other','group-a'],
    ['wrong group','rep1','group-other']
  ])('rejects %s instead of silently hiding a raw asset',async(_label,replicateId,groupId)=>{
    const unsafe={...asset('raw1','raw','rep1'),replicateId,groupId};
    const adapter=createProductionAdapter({
      projects:vi.fn(async()=>({nodes:[]})),
      experiments:vi.fn(async()=>({assayId:'assay-a',replicateCount:1,stages:[],
        groups:[{id:'group-a',name:'Control',replicates:[{id:'rep1',label:'R1'}]}]})),
      assets:{list:vi.fn(async({stage})=>({
        items:stage==='raw'?[unsafe]:[],total:stage==='raw'?1:0,nextCursor:null
      })),detail:vi.fn()}
    });
    await expect(adapter.loadAssay('assay-a')).rejects.toThrow('Asset replicate scope mismatch');
  });
});
