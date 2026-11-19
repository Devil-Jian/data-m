import {test,expect} from '@playwright/test';
test('@S9 native paper workspace uses persisted IPC rather than demo paper data',async({page})=>{
 await page.addInitScript(()=>{
   const papers=[{id:'paper1',title:'测试论文',figures:[]}];
   (window as any).__TAURI_INTERNALS__={
     invoke: async (command:string,args:any)=>{
       if(command==='project_tree') return {nodes:[{id:'p',kind:'project',parentId:null,name:'测试项目'}]};
       if(command==='paper_workspace'){
         const action=args.request.action;
         if(action==='create' && args.request.kind==='figure') papers[0].figures.push({id:'fig1',label:args.request.name,panels:[]} as never);
         return {papers,candidates:[]};
       }
       throw Error('Unexpected command: '+command);
     },
     transformCallback:()=>1,
     metadata:{currentWindow:{label:'main'}}
   };
 });
 await page.goto('/?legacy=1');
 await page.getByRole('button',{name:'论文管理'}).click();
 await expect(page.getByLabel('S9 真实论文管理工作区')).toBeVisible();
 await expect(page.getByText('测试论文')).toBeVisible();
 await page.getByLabel('新增或修改名称').fill('Figure 1');
 await page.getByRole('button',{name:'+ Figure'}).click();
 await expect(page.getByLabel('S9 真实论文管理工作区').locator('.s9-node strong').filter({hasText:'Figure 1'})).toBeVisible();
});
