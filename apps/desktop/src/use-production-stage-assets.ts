import {useCallback,useEffect,useRef,useState} from 'react';
import {assetApi} from './asset-browser';
import type {AssetApi,AssetPage,AssetRow} from './asset-browser';
import type {ProductionStage} from './production-adapter';

export interface StageScope{
  assayId:string;stage:ProductionStage;groupId?:string;search?:string;
}
export interface StagePageState{
  items:AssetRow[];total:number;cursor:string|null;seen:string[];
}
export const emptyStagePage:StagePageState={items:[],total:0,cursor:null,seen:[]};

/** Continue *real* S4 server pagination. An invalid/repeated cursor never produces a success state. */
export async function fetchProductionStagePage(
  scope:StageScope, previous:StagePageState|null=null, api:AssetApi=assetApi
):Promise<StagePageState>{
  if(!scope.assayId||(scope.stage==='raw'||scope.stage==='processed')&&!scope.groupId)
    throw Error('请选择有效实验及分组');
  if(previous&&!previous.cursor)throw Error('没有下一页');
  const req={assayId:scope.assayId,stage:scope.stage,
    ...(scope.stage==='raw'||scope.stage==='processed'?{groupId:scope.groupId}:{}),
    ...(scope.search?.trim()?{search:scope.search.trim()}:{}),
    limit:100,...(previous?.cursor?{cursor:previous.cursor}:{})};
  const page:AssetPage=await api.list(req);
  if(page.items.some(row=>row.stage!==scope.stage ||
    ((scope.stage==='raw'||scope.stage==='processed')&&row.groupId!==scope.groupId) ||
    ((scope.stage==='statistics'||scope.stage==='display')&&row.replicateId!==null)))
    throw Error('原生资产查询返回了不同作用域的文件');
  const seen=previous?.seen??[];
  const cursor=page.nextCursor;
  if(cursor&&(seen.includes(cursor)||cursor===previous?.cursor))throw Error('资产分页游标重复');
  const ids=new Set((previous?.items??[]).map(row=>row.assetLinkId));
  if(page.items.some(row=>ids.has(row.assetLinkId)))throw Error('资产分页出现重复 ID');
  const items=[...(previous?.items??[]),...page.items];
  if(items.length>page.total||page.total<(previous?.total??0))
    throw Error('资产分页总数无效');
  return {items,total:page.total,cursor,seen:cursor?[...seen,cursor]:seen};
}
export interface StageView{
  items:AssetRow[];total:number;hasMore:boolean;loading:boolean;error:string;
  loadMore:()=>void;refresh:()=>void;
}
/** Epoch guard rejects old assay/group/stage/search responses on navigation. */
export function useProductionStageAssets(scope:StageScope,active:boolean):StageView{
  const [state,setState]=useState<StagePageState>(emptyStagePage);
  const [loading,setLoading]=useState(false);
  const [error,setError]=useState('');
  const [refreshId,setRefreshId]=useState(0);
  const generation=useRef(0);
  const busy=useRef(false);
  const current=useRef<StagePageState>(emptyStagePage);
  const scopeKey=[scope.assayId,scope.stage,scope.groupId??'',scope.search??''].join('\u0001');
  useEffect(()=>{
    const token=++generation.current;
    current.current=emptyStagePage;busy.current=false;
    setState(emptyStagePage);setError('');setLoading(false);
    if(!active||!scope.assayId||(scope.stage==='raw'||scope.stage==='processed')&&!scope.groupId)return;
    busy.current=true;setLoading(true);
    void fetchProductionStagePage(scope).then(page=>{
      if(generation.current!==token)return;
      current.current=page;setState(page);
    }).catch(reason=>{if(generation.current===token)setError(String(reason instanceof Error?reason.message:reason));})
      .finally(()=>{if(generation.current===token){setLoading(false);busy.current=false;}});
    return ()=>{generation.current++;busy.current=false;};
  // scopeKey is a stable primitive key, and scope carries the same values.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[scopeKey,active,refreshId]);
  const loadMore=useCallback(()=>{
    const prev=current.current;
    if(!active||busy.current||!prev.cursor)return;
    const token=generation.current;busy.current=true;setLoading(true);setError('');
    void fetchProductionStagePage(scope,prev).then(page=>{
      if(generation.current!==token)return;
      current.current=page;setState(page);
    }).catch(reason=>{if(generation.current===token)setError(String(reason instanceof Error?reason.message:reason));})
      .finally(()=>{if(generation.current===token){busy.current=false;setLoading(false);}});
  // eslint-disable-next-line react-hooks/exhaustive-deps
  },[scopeKey,active]);
  const refresh=useCallback(()=>setRefreshId(n=>n+1),[]);
  return {items:state.items,total:state.total,hasMore:state.cursor!==null,loading,error,loadMore,refresh};
}
