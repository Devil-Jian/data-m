import {describe,expect,it} from 'vitest';
import {validatePaperSnapshot,displayEvidenceFromPapers} from './production-paper';

const hash='a'.repeat(64);
const snapshot={
  papers:[{id:'paper-a',title:'论文 A',status:'draft',notes:'',figures:[
    {id:'figure-a',label:'Figure 1',panels:[{
      id:'panel-a',label:'A',assets:[{assetVersionId:'version-a',usage:'display',versionNumber:2,
        name:'ROS_show.tiff',sha256:hash,stage:'display',assayId:'assay-a',replicateId:null}],
      representatives:[{replicateId:'rep-a',assetVersionId:'version-a'}]
    }]}
  ]}],candidates:[],candidateTotalCount:0,candidateTruncated:false
};
describe('S12R native paper snapshots',()=>{
  it('preserves exact pinned AssetVersionId and SHA-256 without file-name inference',()=>{
    const value=validatePaperSnapshot(snapshot);
    expect(value.papers[0].figures[0].panels[0].assets[0].assetVersionId).toBe('version-a');
    expect(value.papers[0].figures[0].panels[0].assets[0].sha256).toBe(hash);
  });
  it('claims a representative sample only when pinned display AssetVersion verifies it',()=>{
    const pinned=validatePaperSnapshot({...snapshot,papers:[{...snapshot.papers[0],
      figures:[{...snapshot.papers[0].figures[0],panels:[{
        ...snapshot.papers[0].figures[0].panels[0],
        representatives:[
          {replicateId:'rep-a',assetVersionId:'version-a'},
          {replicateId:'rep-b',assetVersionId:'version-unrelated'}
        ]
      }]}]}]});
    const evidence=displayEvidenceFromPapers(pinned.papers,'assay-a');
    expect(evidence.get('rep-a')).toEqual(['Figure 1 / A']);
    expect(evidence.has('rep-b')).toBe(false);
    expect(displayEvidenceFromPapers(pinned.papers,'other-assay').size).toBe(0);
  });
  it('rejects invalid digests and reused paper/figure IDs',()=>{
    expect(()=>validatePaperSnapshot({...snapshot,papers:[{...snapshot.papers[0],
      figures:[{...snapshot.papers[0].figures[0],panels:[{...snapshot.papers[0].figures[0].panels[0],
        assets:[{...snapshot.papers[0].figures[0].panels[0].assets[0],sha256:'unknown'}]}]}]
    }]})).toThrow();
    expect(()=>validatePaperSnapshot({...snapshot,papers:[snapshot.papers[0],snapshot.papers[0]]})).toThrow();
  });
});
