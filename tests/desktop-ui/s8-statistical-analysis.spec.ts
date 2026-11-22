import {test,expect} from '@playwright/test';

async function open(page:import('@playwright/test').Page){
 await page.addInitScript(()=>{
  (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
    {id:'p',kind:'project',parentId:null,name:'Test'},
    {id:'t',kind:'theme',parentId:'p',name:'Theme'},
    {id:'a',kind:'assay',parentId:'t',name:'ROS'}]});
  const rows=Array.from({length:6},(_,i)=>({
    replicateId:'r'+(i+1),groupId:'g',groupName:'Control',label:'R'+(i+1),included:false
  }));
  const analyses:any[]=[];
  const samples:Record<string,string[]>={};
  const versions:Record<string,{inputs:string[];results:string[]}>={};
  const log:any[]=[];
  (window as any).__S8_TEST__={analyses,samples,versions,log};
  (window as any).__S8_ANALYSIS_MOCK__=async(req:any)=>{
    log.push({...req});
    let id=req.analysisId??null;
    const existing=analyses.find(a=>a.id===id);
    if(req.action==='create'){
      id='analysis'+(analyses.length+1);
      analyses.push({id,name:req.name,notes:req.notes,frozenAt:null,updatedAt:'2026-11-17',includedN:req.replicateIds.length});
      samples[id]=[...req.replicateIds];versions[id]={inputs:[...req.inputVersionIds],results:[...req.resultVersionIds]};
    }
    if(req.action==='save'){
      if(!existing||existing.frozenAt)throw {code:'FROZEN'};
      Object.assign(existing,{name:req.name,notes:req.notes,includedN:req.replicateIds.length});
      samples[id]=[...req.replicateIds];versions[id]={inputs:[...req.inputVersionIds],results:[...req.resultVersionIds]};
    }
    if(req.action==='freeze'){
      if(!existing||existing.frozenAt)throw {code:'FROZEN'};
      existing.frozenAt='2026-11-17';
    }
    if(req.action==='revise'){
      if(!existing?.frozenAt)throw {code:'NOT_FROZEN'};
      id='analysis'+(analyses.length+1);
      analyses.push({...existing,id,name:existing.name+' (revision)',frozenAt:null});
      samples[id]=[...samples[req.analysisId]];
      versions[id]={...versions[req.analysisId]};
    }
    return {
      assayId:'a',selectedId:id,analyses:[...analyses],
      samples:rows.map(s=>({...s,included:Boolean(id&&samples[id]?.includes(s.replicateId))})),
      availableVersions:[{id:'v1',assetName:'raw.dat',versionNumber:1,deleted:false}],
      inputVersionIds:id?versions[id]?.inputs??[]:[],
      resultVersionIds:id?versions[id]?.results??[]:[],
    };
  };
 });
 await page.goto('/?legacy=1');
 await page.getByRole('button',{name:'项目管理',exact:true}).click();
 await page.getByRole('button',{name:'实验：ROS'}).click();
 await page.getByRole('button',{name:'统计分析 (S8)'}).click();
 return page.getByRole('main',{name:'S8 统计分析工作台'});
}
test('@S8 independent inclusion n in same assay (explicit browser mock)',async({page})=>{
 const root=await open(page);
 await root.getByRole('textbox',{name:'分析名称'}).fill('A');
 for(let i=1;i<=5;i++)await root.getByLabel('R'+i,{exact:true}).check();
 await expect(root).toContainText('实验 n=6 · 本次纳入 n=5 · 未纳入 1');
 await root.getByRole('button',{name:'保存分析'}).click();
 await root.getByRole('button',{name:'+ 新建分析'}).click();
 await root.getByRole('textbox',{name:'分析名称'}).fill('B');
 for(let i=1;i<=6;i++)await root.getByLabel('R'+i,{exact:true}).check();
 await root.getByRole('button',{name:'保存分析'}).click();
 await expect(root).toContainText('实验 n=6 · 本次纳入 n=6 · 未纳入 0');
 await root.getByRole('button',{name:/A.*5 纳入/}).click();
 await expect(root).toContainText('实验 n=6 · 本次纳入 n=5 · 未纳入 1');
});
test('@S8 frozen analysis is read-only and revised separately (explicit browser mock)',async({page})=>{
 const root=await open(page);
 await root.getByRole('textbox',{name:'分析名称'}).fill('Frozen sample');
 await root.getByLabel('R1',{exact:true}).check();
 await root.getByRole('button',{name:'保存分析'}).click();
 await root.getByRole('button',{name:'冻结分析'}).click();
 await expect(root.getByRole('textbox',{name:'分析名称'})).toBeDisabled();
 await expect(root.getByRole('button',{name:'保存分析'})).toBeDisabled();
 await root.getByRole('button',{name:'创建修订'}).click();
 await expect(root.getByRole('textbox',{name:'分析名称'})).toBeEnabled();
 await expect(root).toContainText('Frozen sample (revision)');
});
test('@S8 pins explicit input and result version IDs (explicit browser mock)',async({page})=>{
 const root=await open(page);
 await root.getByRole('textbox',{name:'分析名称'}).fill('With files');
 await root.getByRole('region',{name:'输入结果版本'}).getByRole('checkbox').first().check();
 await root.getByRole('button',{name:'保存分析'}).click();
 const saved=await page.evaluate(()=>(window as any).__S8_TEST__.log.find((r:any)=>r.action==='create'));
 expect(saved.inputVersionIds).toEqual(['v1']);
 expect(saved.resultVersionIds).toEqual([]);
});
