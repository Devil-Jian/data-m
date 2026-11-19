import {describe,expect,it,vi} from 'vitest';
import {captureProductionTransfer,validatedProductionTarget,commitProductionTransfer} from './production-transfer';
import type {AssetRow} from './asset-browser';

const asset:AssetRow={
  assetId:'asset-a',assetLinkId:'link-a',assetVersionId:'version-a',versionNumber:1,
  name:'ROS_RAW.tif',stage:'raw',groupId:'group-a',replicateId:'rep-a',
  sha256:'a'.repeat(64),sizeBytes:50,referenced:false
};
describe('S12R live transfer safety',()=>{
  it('captures only visible asset links and versions',()=>{
    const intent=captureProductionTransfer('assay-a',[asset],['link-a'],'copy');
    expect(intent.items).toEqual([{
      assayId:'assay-a',stage:'raw',groupId:'group-a',replicateId:'rep-a',
      assetId:'asset-a',assetLinkId:'link-a',assetVersionId:'version-a'
    }]);
    expect(()=>captureProductionTransfer('assay-a',[asset],['link-hidden'],'copy')).toThrow();
    expect(()=>captureProductionTransfer('assay-a',[asset],['link-a','link-a'],'copy')).toThrow();
  });
  it('blocks sample scope without explicit replicate and protects assay scope',()=>{
    expect(()=>validatedProductionTarget('assay-a','raw','group-a','')).toThrow();
    expect(validatedProductionTarget('assay-a','statistics','group-a','rep-a')).toEqual({
      assayId:'assay-a',stage:'statistics',groupId:null,replicateId:null
    });
  });
  it('commits one exact request ID and rejects other assay',async()=>{
    const intent=captureProductionTransfer('assay-a',[asset],['link-a'],'move');
    const target=validatedProductionTarget('assay-a','processed','group-a','rep-a');
    const backend=vi.fn(async request=>({
      requestId:request.requestId,action:request.action,target:request.target,
      links:[{sourceLinkId:'link-a',resultLinkId:'link-a',assetId:'asset-a',assetVersionId:'version-a'}]
    }));
    await commitProductionTransfer(intent,target,backend);
    expect(backend).toHaveBeenCalledWith({
      action:'move',requestId:intent.requestId,items:intent.items,target
    });
    await expect(commitProductionTransfer(intent,{...target,assayId:'assay-other'},backend)).rejects.toThrow();
    expect(backend).toHaveBeenCalledTimes(1);
  });
});
