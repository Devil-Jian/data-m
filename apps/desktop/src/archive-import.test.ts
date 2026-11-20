import {describe,it,expect} from 'vitest';
import {validateArchiveResponse,reportArchive} from './archive-import';
import type {ArchiveEntry,ArchiveTask} from './archive-import';

const sha='a'.repeat(64);
const entry:ArchiveEntry={
  id:'entry-1',relativePath:'Theme/ROS/Control/ROS01_process_crop.tif',
  sourcePath:'C:\\Test\\ROS01_process_crop.tif',sizeBytes:37,sha256:sha,
  projectName:'A',themeName:'Theme',assayName:'ROS',groupName:'Control',
  replicateName:'ROS01',stage:'processed',decision:'import',
  alias:'ROS01_process_crop.tif',processTag:'crop',issue:'',status:'queued',jobId:null
};
const task:ArchiveTask={
  id:'task-1',rootName:'A',layout:'project_theme_assay_group',targetProjectId:null,
  targetThemeName:'',fallbackGroup:'',status:'prepared',total:1,imported:0,
  skipped:0,review:0,failed:0,ready:1,entries:[entry],nextOffset:null
};
describe('S13 strict native manifest contract',()=>{
  it('retains stable source identities, tags and SHA without inventing sample inclusion',()=>{
    const parsed=validateArchiveResponse({current:task,tasks:[{...task,entries:[]}],templates:[]});
    expect(parsed.current?.entries[0].processTag).toBe('crop');
    const report=reportArchive(task,[entry]);
    expect(report.entries[0]).toMatchObject({
      sha256:sha,stage:'processed',processTag:'crop',replicate:'ROS01'
    });
    expect(JSON.stringify(report)).not.toContain('sourcePath');
  });
  it('rejects fabricated committed counts, invalid digests and duplicate IDs',()=>{
    expect(()=>validateArchiveResponse({current:{...task,imported:2},tasks:[],templates:[]})).toThrow();
    expect(()=>validateArchiveResponse({current:{...task,entries:[{...entry,sha256:'abcd'}]},tasks:[],templates:[]})).toThrow();
    expect(()=>validateArchiveResponse({current:{...task,entries:[entry,entry]},tasks:[],templates:[]})).toThrow();
  });
  it('does not accept a rep-scoped display assertion in the import stage protocol',()=>{
    const parsed=validateArchiveResponse({current:{...task,entries:[
      {...entry,stage:'display',processTag:'',decision:'import'}
    ]},tasks:[],templates:[]});
    // Replicate name is only a naming hint here; the S3 target will be assay-scoped.
    expect(parsed.current?.entries[0].replicateName).toBe('ROS01');
    expect(parsed.current?.entries[0].stage).toBe('display');
  });
});
