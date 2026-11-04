<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";
import ArticleViewer from "./ArticleViewer.vue";
import type {
  ArchiveCheck, ArchiveIssue, DataRecord, FolderScan,
  ImportResult, ItemAnchor, OtherResource, Paper, PaperItem, PaperResource,
} from "./types";

const emit=defineEmits<{(event:"archive-completed",paperId:string):void}>();
const papers=ref<Paper[]>([]);
const selectedPaperId=ref<string|null>(null);
const items=ref<PaperItem[]>([]);
const resources=ref<PaperResource[]>([]);
const others=ref<OtherResource[]>([]);
const dataRecords=ref<DataRecord[]>([]);
const scan=ref<FolderScan|null>(null);
const check=ref<ArchiveCheck|null>(null);
const busy=ref(false);
const error=ref("");
const success=ref("");
const selectedItemId=ref<string|null>(null);
const activeResource=ref<string|null>(null);
const bindingItemId=ref<string|null>(null);
const classification=ref<Record<string,{target:string;role:string}>>({});
const selectedOther=ref<Record<string,{target:string;role:string}>>({});
const manualNewLabel=ref("");
const manualNewType=ref<"figure"|"panel"|"table">("figure");
const manualParent=ref("");
const manualDataId=ref("");

const currentPaper=computed(()=>papers.value.find(p=>p.paper_id===selectedPaperId.value)||null);
const draftPapers=computed(()=>papers.value.filter(p=>p.archive_status!=="archived"));
const sortedItems=computed(()=>items.value.filter(i=>!i.parent_id).flatMap(parent=>[
  parent,...items.value.filter(child=>child.parent_id===parent.item_id)
]));
const figureItems=computed(()=>items.value.filter(item=>item.item_type==="figure"));
const missingRequired=computed(()=>check.value?.issues.filter(i=>i.severity==="error")??[]);
const roleOptions=[
  {id:"composite",label:"最终组合图"}, {id:"display",label:"展示子图"},
  {id:"stat_image",label:"统计原始图"}, {id:"statistics",label:"统计数据"},
  {id:"other",label:"其他文件"}, {id:"unspecified",label:"暂未区分"},
];

function noticeError(err:unknown){error.value=String(err); success.value="";}
function resetNotice(){error.value="";success.value="";}
function size(bytes:number){return bytes<1024?bytes+" B":bytes<1048576?(bytes/1024).toFixed(1)+" KB":(bytes/1048576).toFixed(2)+" MB";}

async function loadPapers(){
  papers.value=await invoke<Paper[]>("list_papers");
  if(selectedPaperId.value && !papers.value.some(p=>p.paper_id===selectedPaperId.value)){
    selectedPaperId.value=null;
  }
}
async function loadContents(){
  const id=selectedPaperId.value;
  if(!id){items.value=[];resources.value=[];others.value=[];check.value=null;return;}
  const [figures,data,unassigned,report]=await Promise.all([
    invoke<PaperItem[]>("list_paper_items",{paperId:id}),
    invoke<PaperResource[]>("list_paper_resources",{paperId:id}),
    invoke<OtherResource[]>("list_other_resources",{paperId:id}),
    invoke<ArchiveCheck>("check_paper_archive",{paperId:id}),
  ]);
  if(id!==selectedPaperId.value)return;
  items.value=figures;resources.value=data;others.value=unassigned;check.value=report;
  classification.value=Object.fromEntries(data.map(r=>[r.reference_id,{target:r.item_id,role:r.resource_role}]));
  selectedOther.value=Object.fromEntries(unassigned.map(r=>[r.data_id,{target:"",role:"unspecified"}]));
}
async function choosePaper(id:string){
  selectedPaperId.value=id;selectedItemId.value=null;activeResource.value=null;bindingItemId.value=null;
  resetNotice();
  try{await loadContents();}catch(e){noticeError(e);}
}
async function chooseFolder(){
  if(busy.value)return;
  try{
    const folder=await open({directory:true,multiple:false,title:"选择整篇论文文件夹"});
    if(typeof folder!=="string")return;
    busy.value=true;resetNotice();scan.value=null;
    scan.value=await invoke<FolderScan>("scan_paper_folder",{path:folder});
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function importFolder(){
  if(!scan.value||busy.value)return;
  busy.value=true;resetNotice();
  try{
    const outcome=await invoke<ImportResult>("import_paper_folder",{path:scan.value.folder});
    scan.value=null;
    await loadPapers();
    await choosePaper(outcome.paper.paper_id);
    success.value="已导入 "+outcome.imported+" 个文件，进入待归档处理工作台。";
    if(outcome.failed.length)error.value="部分文件未成功导入：\n"+outcome.failed.join("\n");
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function saveClassification(reference:PaperResource){
  const draft=classification.value[reference.reference_id];
  if(!draft||busy.value||!selectedPaperId.value)return;
  busy.value=true;resetNotice();
  try{
    await invoke("update_resource_classification",{
      paperId:selectedPaperId.value,referenceId:reference.reference_id,
      targetItemId:draft.target,role:draft.role,
    });
    await loadContents();success.value="文件角色及归属已更新，原始文件保持不变。";
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function classifyOther(dataId:string){
  const chosen=selectedOther.value[dataId];
  if(!chosen?.target||busy.value||!selectedPaperId.value)return;
  busy.value=true;resetNotice();
  try{
    await invoke("classify_other_resource",{
      paperId:selectedPaperId.value,dataId,targetItemId:chosen.target,
      role:chosen.role==="unspecified"?"other":chosen.role,
    });
    await loadContents();success.value="待分类文件已建立图表关联。";
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function createItem(){
  if(!currentPaper.value||!manualNewLabel.value.trim()||busy.value)return;
  busy.value=true;resetNotice();
  try{
    const item=await invoke<PaperItem>("create_paper_item",{
      paperId:currentPaper.value.paper_id,itemType:manualNewType.value,
      label:manualNewLabel.value.trim(),parentId:manualNewType.value==="panel"?manualParent.value||null:null,
    });
    manualNewLabel.value="";await loadContents();selectedItemId.value=item.item_id;
    success.value="图表条目已添加。";
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function linkExisting(){
  if(!selectedPaperId.value||!selectedItemId.value||!manualDataId.value||busy.value)return;
  busy.value=true;resetNotice();
  try{
    await invoke("link_data_to_item",{
      paperId:selectedPaperId.value,itemId:selectedItemId.value,dataId:manualDataId.value,
    });
    manualDataId.value="";await loadContents();
    success.value="原始数据已关联到该图表。";
  }catch(e){noticeError(e);}finally{busy.value=false;}
}
async function bindAnchor(itemId:string,anchor:ItemAnchor){
  if(!selectedPaperId.value)return;
  try{
    await invoke("set_item_anchor",{paperId:selectedPaperId.value,itemId,anchor});
    bindingItemId.value=null;await loadContents();
    success.value="论文正文位置已绑定。";
  }catch(e){noticeError(e);}
}
async function showFile(dataId:string){
  activeResource.value=dataId;
  try{await invoke("reveal_data_file",{dataId});}catch(e){noticeError(e);}
}
async function runCheck(){
  if(!selectedPaperId.value)return;
  try{check.value=await invoke<ArchiveCheck>("check_paper_archive",{paperId:selectedPaperId.value});}catch(e){noticeError(e);}
}
async function finalize(){
  if(!selectedPaperId.value||busy.value)return;
  const id=selectedPaperId.value;
  busy.value=true;resetNotice();
  try{
    await runCheck();
    if(!check.value?.can_archive){error.value="归档检查未通过，请处理必须修复的问题。";return;}
    await invoke<Paper>("finalize_paper_archive",{paperId:id});
    await loadPapers();
    selectedPaperId.value=null;await loadContents();
    emit("archive-completed",id);
  }catch(e){noticeError(e);}finally{busy.value=false;}
}

watch(selectedPaperId,()=>{manualDataId.value="";});
onMounted(async()=>{
  try{
    await Promise.all([loadPapers(),invoke<DataRecord[]>("list_data_records").then(d=>{dataRecords.value=d;})]);
  }catch(e){noticeError(e);}
});
</script>

<template>
  <div class="processing">
    <aside class="left-panel">
      <div class="panel-heading"><h2>归档工作台</h2><span>{{ draftPapers.length }} 篇待处理</span></div>
      <button class="primary full" :disabled="busy" @click="chooseFolder">＋ 导入整篇论文文件夹</button>
      <p class="help">根目录放论文正文，按 Figure / Table / Others 自动分类。</p>
      <h3>待归档与修订中的论文</h3>
      <button v-for="paper in draftPapers" :key="paper.paper_id"
        class="paper-entry" :class="{active:selectedPaperId===paper.paper_id}"
        @click="choosePaper(paper.paper_id)">
        <strong>{{ paper.title }}</strong>
        <small>{{ paper.archive_status==="revising"?"修订中":"待归档" }} · {{ paper.item_count }} 个图表</small>
      </button>
      <p v-if="!draftPapers.length" class="help">暂无待处理论文。选择文件夹开始归档。</p>
      <p class="hint">处理阶段可调整文件角色、归属和文章图表位置；正式归档后转入只读资源库。</p>
    </aside>

    <section class="main-panel">
      <template v-if="scan">
        <div class="content-head">
          <div><span class="eyebrow">IMPORT PREVIEW</span><h2>导入预检查：{{ scan.paper_title }}</h2><p>{{ scan.folder }}</p></div>
          <div class="actions"><button @click="scan=null">取消</button><button class="primary" :disabled="busy" @click="importFolder">{{ busy?"导入中…":"确认并建立论文草稿" }}</button></div>
        </div>
        <div class="summary-bar">
          <span>文件 {{ scan.entries.length }}</span><span>总大小 {{ size(scan.total_bytes) }}</span>
          <span>正文 {{ scan.manuscript||"未找到" }}</span><span>提醒 {{ scan.warnings.length }}</span>
        </div>
        <div class="scan-table">
          <table><thead><tr><th>相对路径</th><th>识别对象</th><th>角色</th><th>归属</th><th>问题</th></tr></thead>
            <tbody><tr v-for="entry in scan.entries" :key="entry.relative_path">
              <td>{{ entry.relative_path }}</td><td>{{ entry.kind }}</td><td>{{ entry.role }}</td>
              <td>{{ entry.figure_number ? "Figure "+entry.figure_number+(entry.panel_label||"") : "待分类 / 正文" }}</td>
              <td :class="{warning:entry.issue}">{{ entry.issue||"✓ 已识别" }}</td>
            </tr></tbody>
          </table>
        </div>
        <section v-if="scan.warnings.length" class="warnings"><h3>识别提醒</h3><p v-for="w in scan.warnings" :key="w">{{ w }}</p></section>
      </template>

      <template v-else-if="currentPaper">
        <div class="content-head">
          <div><span class="eyebrow">ARCHIVE PREPARATION</span><h2>{{ currentPaper.title }}</h2>
            <p>{{ currentPaper.archive_status==="revising"?"正在编辑修订草稿":"待归档" }} · 数据不重复复制，修改仅作用于引用关系</p>
          </div>
          <div class="actions"><button :disabled="busy" @click="runCheck">重新检查</button><button class="primary" :disabled="busy||!check?.can_archive" @click="finalize">完成归档</button></div>
        </div>
        <div class="processing-main">
          <section class="mapping-panel">
            <h3>论文图表关系树</h3>
            <div class="new-item-row">
              <select v-model="manualNewType"><option value="figure">Figure</option><option value="panel">Panel</option><option value="table">Table</option></select>
              <input v-model="manualNewLabel" placeholder="图表编号" />
              <select v-if="manualNewType==='panel'" v-model="manualParent">
                <option value="">所属 Figure</option><option v-for="fig in figureItems" :key="fig.item_id" :value="fig.item_id">{{ fig.label }}</option>
              </select>
              <button :disabled="busy||!manualNewLabel.trim()" @click="createItem">新增</button>
            </div>
            <button v-for="item in sortedItems" :key="item.item_id" class="item-row"
              :class="{active:selectedItemId===item.item_id,panel:item.item_type==='panel'}"
              @click="selectedItemId=item.item_id;bindingItemId=null">
              <strong>{{ item.label }}</strong><span>{{ resources.filter(r=>r.item_id===item.item_id).length }} 个资源</span>
            </button>
            <div v-if="selectedItemId" class="inline-actions">
              <button @click="bindingItemId=bindingItemId===selectedItemId?null:selectedItemId">
                {{ bindingItemId?"取消绑定":"在文章中绑定位置" }}
              </button>
              <div class="add-existing"><select v-model="manualDataId"><option value="">选择已有 Data ID</option><option v-for="data in dataRecords" :key="data.data_id" :value="data.data_id">{{ data.original_filename }}</option></select><button :disabled="!manualDataId" @click="linkExisting">关联</button></div>
            </div>
            <h3>归档问题清单</h3>
            <div v-if="check?.issues.length" class="issue-list">
              <p v-for="(issue,index) in check.issues" :key="index" :class="issue.severity">{{ issue.severity==="error"?"必须处理":"提醒" }} · {{ issue.message }}</p>
            </div>
            <p v-else class="ok">未发现阻止归档的问题</p>
            <p v-if="missingRequired.length" class="help">解决上面的错误后，才能完成正式归档。</p>
          </section>
          <section class="article-area">
            <ArticleViewer :paper="currentPaper" :items="items" :binding-item-id="bindingItemId"
              @bind-anchor="bindAnchor" @select-item="id=>selectedItemId=id" />
            <p class="viewer-tip">正文图表位置：能用编号匹配时直接跳转；无法识别时在这里绑定一次。</p>
          </section>
          <section class="files-panel">
            <h3>文件绑定和分类修正</h3>
            <div class="files-scroll">
              <div v-for="resource in resources" :key="resource.reference_id" class="file-card">
                <strong>{{ resource.original_filename }}</strong>
                <small>{{ resource.source_relative_path||resource.display_id }} · {{ size(resource.file_size) }}</small>
                <div class="file-form">
                  <select v-model="classification[resource.reference_id].target">
                    <option v-for="item in sortedItems" :key="item.item_id" :value="item.item_id">{{ item.label }}</option>
                  </select>
                  <select v-model="classification[resource.reference_id].role">
                    <option v-for="role in roleOptions" :key="role.id" :value="role.id">{{ role.label }}</option>
                  </select>
                  <button :disabled="busy" @click="saveClassification(resource)">保存</button>
                </div>
                <button class="link-button" @click="showFile(resource.data_id)">在资源管理器定位</button>
              </div>
              <h3>待分类 / Others（{{ others.length }}）</h3>
              <div v-for="other in others" :key="other.data_id" class="file-card">
                <strong>{{ other.original_filename }}</strong><small>{{ other.source_relative_path }}</small>
                <div class="file-form">
                  <select v-model="selectedOther[other.data_id].target"><option value="">选择图表</option><option v-for="item in sortedItems" :key="item.item_id" :value="item.item_id">{{ item.label }}</option></select>
                  <select v-model="selectedOther[other.data_id].role"><option v-for="role in roleOptions" :key="role.id" :value="role.id">{{ role.label }}</option></select>
                  <button :disabled="busy||!selectedOther[other.data_id].target" @click="classifyOther(other.data_id)">关联</button>
                </div>
              </div>
              <p v-if="!resources.length&&!others.length" class="help">还没有原始文件。可以从左侧导入整篇论文文件夹。</p>
            </div>
          </section>
        </div>
      </template>

      <div v-else class="placeholder">
        <h2>整篇论文一次导入，自动建立资源对应关系</h2>
        <p>选择包含正文、Figure、Table、Others 的论文文件夹，扫描后一次导入，再集中处理少量异常关联。</p>
        <button class="primary" :disabled="busy" @click="chooseFolder">选择论文文件夹</button>
      </div>
      <div v-if="error||success" class="toast" :class="{error:!!error}">{{ error||success }} <button @click="resetNotice">×</button></div>
    </section>
  </div>
</template>

<style scoped>
.processing{display:grid;grid-template-columns:250px minmax(0,1fr);height:100%;min-height:0;background:#f4f7fa;color:#263e52}
.left-panel{padding:22px 16px;border-right:1px solid #dce6ed;background:white;overflow:auto}
.panel-heading,.content-head{display:flex;align-items:center;justify-content:space-between;gap:14px}
.panel-heading h2,.content-head h2{margin:0;font-size:18px}
.panel-heading span{color:#899bab;font-size:11px}
.primary{padding:10px 15px;border:1px solid #194a70;border-radius:7px;background:#1e547c;color:#fff;font-size:12px;cursor:pointer}
.full{width:100%;margin-top:23px}
.help,.hint{font-size:11px;color:#8192a2;line-height:1.75}
.left-panel h3,.mapping-panel h3,.files-panel h3{font-size:13px;margin:24px 0 13px}
.paper-entry{display:flex;flex-direction:column;gap:8px;width:100%;padding:13px;border:1px solid transparent;border-radius:8px;background:transparent;text-align:left;cursor:pointer}
.paper-entry strong{font-size:12px;line-height:1.5}.paper-entry small{color:#8294a6;font-size:10px}
.paper-entry.active{border-color:#b9d8eb;background:#eaf4fb}
.hint{border-top:1px solid #e9edf1;padding-top:15px;margin-top:24px}
.main-panel{min-width:0;min-height:0;display:flex;flex-direction:column;position:relative}
.content-head{padding:20px 24px;background:#fff;border-bottom:1px solid #dce6ed}
.content-head p{font-size:11px;color:#899aab;margin:9px 0 0;overflow-wrap:anywhere}
.eyebrow{color:#92a4b5;font-size:10px;letter-spacing:.12em}
.actions{display:flex;gap:9px;flex-shrink:0}.actions button,.new-item-row button,.file-form button,.inline-actions button{padding:8px 10px;border:1px solid #c9dce9;border-radius:6px;background:#fff;color:#295e80;font-size:11px;cursor:pointer}
.actions .primary{background:#1e547c;color:white}
.summary-bar{display:flex;gap:22px;padding:16px 23px;background:#ecf4fa;font-size:12px}
.scan-table{padding:18px;overflow:auto;flex:1}.scan-table table{width:100%;border-collapse:collapse;background:white;font-size:11px}
.scan-table td,.scan-table th{padding:12px;text-align:left;border-bottom:1px solid #e8eff3}
.scan-table th{background:#eaf0f5}.warning{color:#a56b22}
.warnings{padding:16px 22px;border-top:1px solid #dce6ed;max-height:25%;overflow-y:auto;background:#fff}
.warnings p{font-size:11px;color:#8b6a2f}
.processing-main{flex:1;min-height:0;display:grid;grid-template-columns:245px minmax(240px,1fr) 350px}
.mapping-panel,.files-panel{padding:17px 13px;overflow:auto;background:#fff}
.mapping-panel{border-right:1px solid #dce6ed}
.files-panel{border-left:1px solid #dce6ed;display:flex;flex-direction:column;min-height:0}
.files-panel h3{margin:0 0 12px}
.files-scroll{flex:1;overflow:auto}
.article-area{min-width:0;min-height:0;display:flex;flex-direction:column}
.article-area :deep(.article-viewer){flex:1;min-height:0}
.viewer-tip{margin:0;padding:12px;font-size:10px;color:#8192a4;background:white}
.new-item-row,.file-form,.add-existing{display:flex;flex-wrap:wrap;gap:6px;margin:9px 0}
.new-item-row input{width:90px}
.new-item-row input,.new-item-row select,.file-form select,.add-existing select{min-width:0;max-width:100%;padding:7px;border:1px solid #d6e2ed;border-radius:5px;font-size:11px}
.item-row{display:flex;justify-content:space-between;gap:7px;width:100%;padding:12px 9px;margin:2px 0;border:1px solid transparent;border-radius:6px;background:transparent;text-align:left;cursor:pointer}
.item-row.panel{padding-left:27px}.item-row.active{background:#e8f3fb;border-color:#bcd9eb}
.item-row strong{font-size:12px}.item-row span{font-size:10px;color:#8697a7}
.inline-actions{margin-top:10px;padding:9px;border:1px solid #dce5ec;border-radius:7px;background:#f8fafc}
.add-existing select{flex:1}
.issue-list{font-size:11px;line-height:1.6}.issue-list p{padding:8px;border-radius:5px;background:#f6f8fa}
.issue-list p.error{background:#fdf0ee;color:#a13b37}.issue-list p.warning{background:#fff6e6;color:#9c7125}.ok{font-size:11px;color:#3b8c69}
.file-card{padding:12px 10px;margin-bottom:9px;border:1px solid #e3eaf0;border-radius:7px}
.file-card strong,.file-card small{display:block;overflow-wrap:anywhere}
.file-card strong{font-size:12px}.file-card small{color:#93a2b0;font-size:10px;margin-top:6px}
.file-form select{flex:1 1 110px}.link-button{border:0;background:transparent;padding:5px;color:#3d759e;font-size:10px;cursor:pointer}
.placeholder{display:flex;align-items:center;justify-content:center;flex-direction:column;flex:1;padding:30px;text-align:center}
.placeholder h2{font-size:20px}.placeholder p{max-width:530px;color:#8194a6;font-size:12px;line-height:1.8}
.toast{position:absolute;left:24px;right:24px;bottom:20px;display:flex;justify-content:space-between;padding:13px 18px;border:1px solid #c4e2d4;border-radius:8px;background:#ecf9f3;color:#28734f;font-size:12px;white-space:pre-wrap;z-index:10}
.toast.error{background:#fff0ee;color:#a53e35;border-color:#edcbc8}.toast button{background:transparent;border:0;font-size:16px;cursor:pointer}
button:disabled{opacity:.55;cursor:not-allowed}
</style>
