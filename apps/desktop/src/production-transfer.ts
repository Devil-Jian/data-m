/** S12R Workbench transfer intents: explicit target, frozen native identities,
 * one request ID per confirmed intent, and no implicit sample creation.
 */
import type {AssetRow} from './asset-browser';
import {sourcesFromRows,TransferError,transferApi} from './asset-transfer';
import type {AssetScope,TransferApi,TransferRequest,TransferSource} from './asset-transfer';

export interface ProductionTransferDraft {
  action:'copy'|'move';
  assayId:string;
  items:TransferSource[];
  requestId:string;
}
export function captureProductionTransfer(
  assayId:string, visible:readonly AssetRow[], ids:readonly string[],
  action:'copy'|'move'
):ProductionTransferDraft {
  if(!assayId||ids.length===0||ids.length>100||new Set(ids).size!==ids.length)
    throw new TransferError('INVALID_REQUEST');
  const rows=ids.map(id=>visible.find(file=>file.assetLinkId===id));
  if(rows.some(x=>!x))throw new TransferError('STALE_SELECTION');
  const items=(rows as AssetRow[]).flatMap(file=>
    sourcesFromRows({assayId,stage:file.stage},[file]));
  return {action,assayId,items,requestId:crypto.randomUUID()};
}
export function validatedProductionTarget(
  assayId:string, stage:AssetScope['stage'], groupId:string, replicateId:string
):AssetScope {
  if(!assayId)throw new TransferError('INVALID_TARGET');
  if(stage==='raw'||stage==='processed'){
    if(!groupId||!replicateId)throw new TransferError('INVALID_TARGET');
    return {assayId,stage,groupId,replicateId};
  }
  return {assayId,stage,groupId:null,replicateId:null};
}
export function buildProductionTransferRequest(
  draft:ProductionTransferDraft,target:AssetScope
):TransferRequest {
  if(draft.assayId!==target.assayId||!draft.items.length||draft.items.length>100)
    throw new TransferError('INVALID_TARGET');
  return {action:draft.action,requestId:draft.requestId,items:draft.items,target};
}
export async function commitProductionTransfer(
  draft:ProductionTransferDraft,target:AssetScope,api:TransferApi=transferApi
){
  return api(buildProductionTransferRequest(draft,target));
}
