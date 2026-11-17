import {test,expect} from '@playwright/test';
test('@I1 formal project workspace opens S8 and S9 without using demo data',async({page})=>{
 await page.addInitScript(()=>{
  (window as any).__S1_PROJECT_TREE_MOCK__=async()=>({nodes:[
    {id:'p',kind:'project',parentId:null,name:'I1 Project'},
    {id:'t',kind:'theme',parentId:'p',name:'Theme'},
    {id:'a',kind:'assay',parentId:'t',name:'ROS'}
  ]});
  (window as any).__S8_ANALYSIS_MOCK__=async()=>({
    assayId:'a',selectedId:null,analyses:[],samples:[],availableVersions:[],
    inputVersionIds:[],resultVersionIds:[]
  });
  (window as any).__TAURI_INTERNALS__={
   invoke:async(command:string)=>{
    if(command==='project_tree')return {nodes:[{id:'p',kind:'project',parentId:null,name:'I1 Project'}]};
    if(command==='paper_workspace')return {papers:[{id:'paper',title:'I1 persisted paper',status:'draft',notes:'',figures:[]}],candidates:[]};
    throw Error('Unexpected IPC '+command);
   },transformCallback:()=>1,metadata:{currentWindow:{label:'main'}}
  };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'项目管理',exact:true}).click();
 await page.getByRole('button',{name:'实验：ROS'}).click();
 await page.getByRole('button',{name:'统计分析 (S8)'}).click();
 await expect(page.getByRole('main',{name:'S8 统计分析工作台'})).toBeVisible();
 await page.getByRole('button',{name:'返回项目管理'}).click();
 await page.getByRole('button',{name:'论文管理 (S9)'}).click();
 await expect(page.getByLabel('S9 真实论文管理工作区')).toBeVisible();
 await expect(page.getByText('I1 persisted paper')).toBeVisible();
 await page.getByRole('button',{name:'返回项目管理'}).click();
 await expect(page.getByRole('main',{name:'项目管理工作区'})).toBeVisible();
});
