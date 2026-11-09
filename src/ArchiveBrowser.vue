<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from "vue";
import { invoke } from "@tauri-apps/api/core";
import ArticleViewer from "./ArticleViewer.vue";
import type {
  DataDetails, ItemAnchor, OtherResource, Paper, PaperItem, PaperResource,
} from "./types";

const emit=defineEmits<{(event:"revise",paperId:string):void}>();
const papers=ref<Paper[]>([]);
const selectedPaperId=ref<string|null>(null);
const items=ref<PaperItem[]>([]);
const resources=ref<PaperResource[]>([]);
const others=ref<OtherResource[]>([]);
const selectedItemId=ref<string|null>(null);
const selectedDataId=ref<string|null>(null);
const selectedDetails=ref<DataDetails|null>(null);
const imageUrl=ref<string|null>(null);
const search=ref("");
const showRelation=ref(false);
const busy=ref(false);
const error=ref("");

const archivedPapers=computed(()=>papers.value.filter(p=>p.archive_status==="archived"));
const filteredPapers=computed(()=>archivedPapers.value.filter(p=>
  [p.title,p.doi||""].some(text=>text.toLowerCase().includes(search.value.toLowerCase()))
));
const paper=computed(()=>archivedPapers.value.find(p=>p.paper_id===selectedPaperId.value)||null);
const structuredItems=computed(()=>items.value.filter(i=>!i.parent_id).flatMap(p=>[
  p,...items.value.filter(c=>c.parent_id===p.item_id)
]));
const selectedResource=computed(()=>resources.value.find(r=>r.data_id===selectedDataId.value)||null);
const roleLabels:Record<string,string>={
  composite:"最终组合图",display:"展示子图",stat_image:"统计原始图",
  statistics:"统计数据",other:"其他文件",unspecified:"未区分图片",
};
let loadCounter=0;

function formatBytes(value:number){
  if(value<1024)return value+" B";
  if(value<1048576)return (value/1024).toFixed(1)+" KB";
  return (value/1048576).toFixed(2)+" MB";
}
function releaseImage(){
  if(imageUrl.value)URL.revokeObjectURL(imageUrl.value);
  imageUrl.value=null;
}
async function refresh(){
  papers.value=await invoke<Paper[]>("list_papers");
  if(!selectedPaperId.value&&archivedPapers.value.length)await choosePaper(archivedPapers.value[0].paper_id);
}
async function choosePaper(paperId:string){
  const token=++loadCounter;
  selectedPaperId.value=paperId;
  selectedItemId.value=null;selectedDataId.value=null;selectedDetails.value=null;
  releaseImage();error.value="";
  busy.value=true;
  try{
    const [newItems,newResources,newOthers]=await Promise.all([
      invoke<PaperItem[]>("list_paper_items",{paperId}),
      invoke<PaperResource[]>("list_paper_resources",{paperId}),
      invoke<OtherResource[]>("list_other_resources",{paperId}),
    ]);
    if(token!==loadCounter)return;
    items.value=newItems;resources.value=newResources;others.value=newOthers;
  }catch(e){error.value=String(e);}finally{if(token===loadCounter)busy.value=false;}
}
async function selectItem(itemId:string){
  selectedItemId.value=itemId;
  const first=resources.value.find(r=>r.item_id===itemId);
  if(first)await selectData(first.data_id);
  else{selectedDataId.value=null;selectedDetails.value=null;releaseImage();}
  requestAnimationFrame(()=>{
    document.getElementById("archive-resource-"+itemId)?.scrollIntoView({
      behavior:"smooth",block:"center",
    });
  });
}
async function selectData(dataId:string){
  selectedDataId.value=dataId;selectedDetails.value=null;releaseImage();
  try{
    const details=await invoke<DataDetails>("get_data_details",{dataId});
    if(selectedDataId.value!==dataId)return;
    selectedDetails.value=details;
    const ext=details.record.file_extension.toLowerCase();
    if(["png","jpg","jpeg","webp"].includes(ext)){
      try{
        const bytes=await invoke<ArrayBuffer>("read_data_image",{dataId});
        if(selectedDataId.value!==dataId)return;
        const mime=ext==="png"?"image/png":ext==="webp"?"image/webp":"image/jpeg";
        imageUrl.value=URL.createObjectURL(new Blob([bytes],{type:mime}));
      }catch{
        // Not all laboratory formats can be rendered inline.
      }
    }
  }catch(e){error.value=String(e);}
}
async function reveal(){
  if(!selectedDataId.value)return;
  try{await invoke("reveal_data_file",{dataId:selectedDataId.value});}
  catch(e){error.value=String(e);}
}
async function startRevision(){
  if(!paper.value)return;
  try{
    const chosen=paper.value.paper_id;
    await invoke("revise_archived_paper",{paperId:chosen});
    emit("revise",chosen);
  }catch(e){error.value=String(e);}
}
onMounted(()=>{refresh().catch(e=>error.value=String(e));});
onBeforeUnmount(()=>{loadCounter++;releaseImage();});
</script>

<template>
  <div class="browser">
    <aside class="library">
      <div class="side-title"><p class="eyebrow">ARCHIVED PAPERS</p><h2>论文资源库</h2><span>{{ archivedPapers.length }} 篇已归档</span></div>
      <input v-model="search" class="search-box" placeholder="搜索论文标题 / DOI" />
      <div class="paper-list">
        <button v-for="record in filteredPapers" :key="record.paper_id" class="paper-entry"
          :class="{active:record.paper_id===selectedPaperId}" @click="choosePaper(record.paper_id)">
          <strong>{{ record.title }}</strong>
          <span>{{ record.doi||"无 DOI" }}</span>
          <small>v{{ record.archive_revision }} · {{ record.item_count }} 个图表 · {{ record.resource_count }} 项引用</small>
        </button>
        <p v-if="!filteredPapers.length" class="empty-text">没有匹配的已归档论文。请在“论文归档”中完成归档。</p>
      </div>
      <div class="side-foot">当前资源库只读。需要修改时使用“创建修订”。</div>
    </aside>

    <section class="reader">
      <div class="reader-head">
        <div class="title-section"><p class="eyebrow">PAPER READER</p><h2>{{ paper?.title||"请选择论文" }}</h2>
          <small>{{ paper?.archived_at?"归档于 "+new Date(paper.archived_at).toLocaleString("zh-CN"):"" }}</small>
        </div>
        <div v-if="paper" class="reader-actions">
          <button :class="{active:showRelation}" @click="showRelation=!showRelation">
            {{ showRelation?"文章阅读":"数据关系视图" }}
          </button>
          <button @click="startRevision">创建修订</button>
        </div>
      </div>
      <div class="reading-area">
        <template v-if="showRelation&&paper">
          <div class="relation-view">
            <h3>Figure / Panel / Table 数据关系总览</h3>
            <p>点击一个图表即可在右侧定位其全部原始数据。</p>
            <button v-for="item in structuredItems" :key="item.item_id" class="relation-card"
              :class="{panel:item.item_type==='panel',active:selectedItemId===item.item_id}"
              @click="selectItem(item.item_id)">
              <strong>{{ item.label }}</strong>
              <span>{{ resources.filter(r=>r.item_id===item.item_id).length }} 个原始文件</span>
              <div v-for="resource in resources.filter(r=>r.item_id===item.item_id)" :key="resource.reference_id">
                {{ roleLabels[resource.resource_role]||"其他" }} · {{ resource.original_filename }}
              </div>
            </button>
          </div>
        </template>
        <ArticleViewer v-else :paper="paper" :items="items" :binding-item-id="null"
          @select-item="selectItem" @bind-anchor="(_id:string,_anchor:ItemAnchor)=>undefined" />
      </div>
      <div class="figure-strip">
        <span>论文图表</span>
        <button v-for="item in structuredItems" :key="item.item_id" :class="{active:item.item_id===selectedItemId}"
          @click="selectItem(item.item_id)">
          {{ item.label }}
        </button>
      </div>
    </section>

    <aside class="resources">
      <div class="side-title"><p class="eyebrow">ORIGINAL RESOURCES</p><h2>图表原始数据</h2>
        <span>点击论文 Figure / Panel，即可快速定位</span></div>
      <div class="resource-scroll">
        <div v-if="!paper" class="empty-text">选择左侧论文，浏览其原始数据与对应关系。</div>
        <section v-for="item in structuredItems" :id="'archive-resource-'+item.item_id" :key="item.item_id"
          class="resource-group" :class="{active:selectedItemId===item.item_id,panel:item.item_type==='panel'}">
          <button class="group-header" @click="selectItem(item.item_id)">
            <strong>{{ item.label }}</strong><small>{{ resources.filter(r=>r.item_id===item.item_id).length }} 个文件</small>
          </button>
          <button v-for="resource in resources.filter(r=>r.item_id===item.item_id)" :key="resource.reference_id"
            class="file" :class="{chosen:selectedDataId===resource.data_id}" @click="selectData(resource.data_id)">
            <span class="role">{{ roleLabels[resource.resource_role]||"其他" }}</span>
            <strong>{{ resource.original_filename }}</strong>
            <small>{{ formatBytes(resource.file_size) }} · {{ resource.source_relative_path||resource.display_id }}</small>
          </button>
        </section>
        <section v-if="others.length" class="resource-group">
          <div class="group-header"><strong>Others / 附件</strong><small>{{ others.length }} 个文件</small></div>
          <button v-for="other in others" :key="other.data_id" class="file" @click="selectData(other.data_id)">
            <strong>{{ other.original_filename }}</strong>
            <small>{{ other.source_relative_path }} · {{ formatBytes(other.file_size) }}</small>
          </button>
        </section>
      </div>
      <div class="inspector">
        <h3>文件预览与详情</h3>
        <template v-if="selectedDetails">
          <img v-if="imageUrl" :src="imageUrl" class="preview-img" alt="原始图片预览" />
          <p v-else class="no-preview">该类型暂不支持内嵌预览，可定位原文件打开。</p>
          <div class="detail"><span>文件名</span><strong>{{ selectedDetails.record.original_filename }}</strong></div>
          <div class="detail"><span>分类 / 角色</span><strong>{{ selectedResource?.resource_role||selectedDetails.record.category }}</strong></div>
          <div class="detail"><span>大小</span><strong>{{ formatBytes(selectedDetails.record.file_size) }}</strong></div>
          <div class="detail"><span>Data ID</span><code>{{ selectedDetails.record.data_id }}</code></div>
          <div class="detail"><span>SHA-256</span><code>{{ selectedDetails.record.sha256 }}</code></div>
          <div class="detail"><span>引用位置</span><strong>{{ selectedDetails.usages.length }} 处</strong></div>
          <p v-for="use in selectedDetails.usages" :key="use.item_id" class="usage">
            {{ use.paper_title }} / {{ use.item_label }}
          </p>
          <button class="reveal" @click="reveal">在资源管理器中定位原始文件</button>
        </template>
        <p v-else class="empty-text">点击某个资源查看预览、哈希、原始文件名及关联位置。</p>
      </div>
      <p v-if="error" class="error">{{ error }}</p>
    </aside>
  </div>
</template>

<style scoped>
.browser{display:grid;grid-template-columns:260px minmax(340px,1fr) 370px;height:100%;min-height:0;color:#283e53}
.library,.resources{background:#fff;display:flex;flex-direction:column;min-height:0;overflow:hidden}
.library{border-right:1px solid #dfe7ee}.resources{border-left:1px solid #dfe7ee}
.side-title{padding:22px 19px 15px}.side-title h2,.reader-head h2{font-size:17px;margin:0}
.side-title span,.title-section small{display:block;margin-top:8px;color:#90a0af;font-size:10px}
.eyebrow{font-size:10px;color:#91a3b4;letter-spacing:.13em;margin:0 0 5px}
.search-box{margin:0 17px 12px;padding:10px;border:1px solid #d8e4ed;border-radius:7px;font-size:11px}
.paper-list,.resource-scroll{flex:1;overflow:auto;padding:8px 11px;min-height:0}
.paper-entry{display:flex;flex-direction:column;gap:7px;width:100%;padding:14px 10px;border:1px solid transparent;border-radius:8px;background:white;text-align:left;cursor:pointer}
.paper-entry.active{background:#e9f4fb;border-color:#bddbee}
.paper-entry strong{font-size:12px;line-height:1.6}.paper-entry span,.paper-entry small{font-size:10px;color:#8b9daa}
.side-foot{padding:17px;border-top:1px solid #e9edf1;color:#8b9dad;font-size:11px}
.reader{display:flex;flex-direction:column;min-width:0;min-height:0;background:#edf2f6}
.reader-head{display:flex;align-items:center;justify-content:space-between;gap:10px;min-height:90px;padding:16px 20px;background:#fff;border-bottom:1px solid #dee7ef}
.title-section{min-width:0}.title-section h2{overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.reader-actions{display:flex;gap:8px;flex-shrink:0}
.reader-actions button,.figure-strip button{padding:8px 9px;border:1px solid #d2e0eb;border-radius:6px;background:white;color:#366b8b;font-size:11px;cursor:pointer}
.reader-actions button.active,.figure-strip button.active{background:#dcefff}
.reading-area{flex:1;min-height:0;overflow:hidden}.reading-area :deep(.article-viewer){height:100%}
.figure-strip{display:flex;align-items:center;gap:6px;overflow-x:auto;min-height:58px;padding:10px;background:#fff;border-top:1px solid #dce5ef}
.figure-strip span{font-size:11px;color:#97a4b2;white-space:nowrap}
.figure-strip button{white-space:nowrap}
.relation-view{height:100%;overflow:auto;padding:22px}.relation-view h3{margin:0;font-size:17px}
.relation-view p{font-size:12px;color:#8093a4}.relation-card{display:flex;flex-direction:column;gap:5px;width:100%;padding:14px;margin:12px 0;border:1px solid #dce7ef;border-radius:9px;background:white;text-align:left;cursor:pointer}
.relation-card.panel{margin-left:20px;width:calc(100% - 20px)}
.relation-card.active{border-color:#81b8dc;background:#edf6fb}
.relation-card strong{font-size:12px}.relation-card span,.relation-card div{font-size:11px;color:#7990a3}
.resource-group{margin:6px 0 12px;padding:5px;border:1px solid #e2eaf0;border-radius:8px}
.resource-group.active{border-color:#85bedf;background:#eff8fc}
.resource-group.panel{margin-left:15px}
.group-header{display:flex;align-items:center;justify-content:space-between;width:100%;padding:10px;border:0;background:transparent;text-align:left;cursor:pointer}
.group-header strong{font-size:12px}.group-header small{font-size:10px;color:#9baab6}
.file{display:flex;flex-direction:column;gap:5px;width:calc(100% - 10px);margin:4px;padding:10px;border:1px solid #e8edf3;border-radius:7px;background:white;text-align:left;cursor:pointer}
.file.chosen{background:#dceef9;border-color:#8ec3e4}
.file strong{font-size:11px;overflow-wrap:anywhere}.file small{font-size:10px;color:#93a2b0;overflow-wrap:anywhere}
.role{font-size:10px;color:#32769b}
.inspector{max-height:44%;overflow-y:auto;padding:17px;border-top:1px solid #e5ebef}
.inspector h3{font-size:13px;margin:0 0 12px}
.preview-img{display:block;max-width:100%;max-height:230px;object-fit:contain;margin:0 auto 12px}
.no-preview,.empty-text{padding:15px;color:#93a2b0;font-size:11px;line-height:1.7}
.detail{display:flex;justify-content:space-between;gap:12px;padding:7px 0;border-bottom:1px solid #f0f2f5}
.detail span{flex:0 0 74px;font-size:10px;color:#9aa7b6}
.detail strong,.detail code{font-size:10px;overflow-wrap:anywhere;word-break:break-all}
.usage{font-size:10px;color:#64839a}
.reveal{width:100%;margin-top:13px;padding:10px;border:1px solid #236189;border-radius:7px;color:white;background:#236189;cursor:pointer;font-size:11px}
.error{margin:5px 15px;color:#a7463c;font-size:11px}
</style>
