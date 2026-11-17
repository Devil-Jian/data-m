import {test,expect} from '@playwright/test';

test('@I1 integrated formal navigation retains both S8 statistics and S9 paper modules', async ({page}) => {
 await page.addInitScript(() => {
   (window as any).__TAURI_INTERNALS__ = {
    invoke: async (command:string,args:any) => {
      if(command==='project_tree') return {nodes:[{id:'p',kind:'project',parentId:null,name:'I1 Test Project'}]};
      if(command==='paper_workspace') return {papers:[{id:'paper',title:'I1 persisted paper',status:'draft',notes:'',figures:[]}],candidates:[]};
      if(command==='statistical_analysis') return {analyses:[],samples:[],availableVersions:[],inputVersionIds:[],resultVersionIds:[],selectedId:null};
      throw new Error('Unexpected IPC: '+command+' '+JSON.stringify(args));
    }, transformCallback:()=>1, metadata:{currentWindow:{label:'main'}}
   };
 });
 await page.goto('/');
 await page.getByRole('button',{name:'论文管理'}).click();
 await expect(page.getByLabel('S9 真实论文管理工作区')).toBeVisible();
 await expect(page.getByText('I1 persisted paper')).toBeVisible();
 await page.getByRole('button',{name:'项目管理'}).first().click();
 await expect(page.getByLabel('S9 真实论文管理工作区')).toBeHidden();
});
