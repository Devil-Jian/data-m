import {invoke,isTauri} from '@tauri-apps/api/core';

export type ProjectTreeKind='project'|'theme'|'assay';
export interface ProjectTreeNode {id:string;kind:ProjectTreeKind;parentId:string|null;name:string}
export interface ProjectTreeRequest {
  action:'list'|'create'|'rename'|'delete';
  kind?:ProjectTreeKind;id?:string;parentId?:string;name?:string;requestId?:string;
}
export interface ProjectTreeResponse {nodes:ProjectTreeNode[]}
export type ProjectTreeApi=(request:ProjectTreeRequest)=>Promise<ProjectTreeResponse>;
declare global {interface Window {__S1_PROJECT_TREE_MOCK__?:ProjectTreeApi}}

const messages:Record<string,string>={
  UNAVAILABLE:'浏览器未连接原生项目存储。请在桌面应用中打开项目管理。',
  INVALID_NAME:'名称不能为空，且须符合名称长度要求。',
  INVALID_PARENT:'上级节点不可用。请重新加载后再试。',
  NOT_FOUND:'节点已不存在。请重新加载后再试。',
  HAS_CHILDREN:'该节点仍有下级，请先删除下级节点。',
  HAS_ASSETS:'该节点有关联资产，无法删除。',
  NODE_IN_USE:'该节点有下级或关联资产，无法删除。',
  REQUEST_CONFLICT:'操作请求发生冲突。请取消后重新操作。',
  DATABASE_UNAVAILABLE:'暂时无法访问项目存储，请重试。',
  UNKNOWN_DATABASE:'项目存储格式无法识别，已拒绝打开。请使用受控的 S1 测试数据库。',
  INVALID_RESPONSE:'项目数据暂时无法读取，请重新加载。'
};
/** Reject untrusted backend text, including filesystem paths, at the API boundary. */
export class ProjectTreeError extends Error {
  constructor(public readonly code:string){super(messages[code]??'暂时无法完成操作，请重试或重新加载。');this.name='ProjectTreeError';}
}
export function safeProjectTreeError(error:unknown):ProjectTreeError {
  if(error instanceof ProjectTreeError)return error;
  const code=error&&typeof error==='object'&&'code' in error&&typeof error.code==='string'?error.code:'UNKNOWN';
  return new ProjectTreeError(code);
}
export function validateProjectTreeResponse(value:unknown):ProjectTreeResponse {
  if(!value||typeof value!=='object'||!('nodes' in value)||!Array.isArray(value.nodes))throw new ProjectTreeError('INVALID_RESPONSE');
  const ids=new Set<string>();
  for(const node of value.nodes){
    if(!node||typeof node.id!=='string'||!node.id||ids.has(node.id)||!['project','theme','assay'].includes(node.kind)||typeof node.name!=='string'||!(node.parentId===null||typeof node.parentId==='string'))throw new ProjectTreeError('INVALID_RESPONSE');
    ids.add(node.id);
  }
  const nodes=value.nodes as ProjectTreeNode[];
  for(const node of nodes){
    if(node.kind==='project'?node.parentId!==null:!nodes.some(parent=>parent.id===node.parentId&&parent.kind===(node.kind==='theme'?'project':'theme')))throw new ProjectTreeError('INVALID_RESPONSE');
  }
  return {nodes};
}
export const projectTreeApi:ProjectTreeApi=async request=>{
  try{
    const response=isTauri()
      ?await invoke<ProjectTreeResponse>('project_tree',{request})
      :window.__S1_PROJECT_TREE_MOCK__?await window.__S1_PROJECT_TREE_MOCK__(request):(()=>{throw new ProjectTreeError('UNAVAILABLE');})();
    return validateProjectTreeResponse(response);
  }catch(error){throw safeProjectTreeError(error);}
};
export function projectTreeMode(){
  return isTauri()?'原生 SQLite 项目存储':window.__S1_PROJECT_TREE_MOCK__?'浏览器 Mock（不写入原生数据库）':'浏览器：原生项目存储不可用';
}
