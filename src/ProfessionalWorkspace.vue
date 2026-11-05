<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { open } from "@tauri-apps/plugin-dialog";
import { invoke } from "@tauri-apps/api/core";
import type { FolderScan, ScanEntry, PlannedFile, Paper, ImportResult } from "./types";

const emit = defineEmits<{ (event: "archive-completed", id: string): void }>();
type Row = PlannedFile & { file_size: number; issue: string | null };
const roles = [
  ["composite", "最终组合图"], ["display", "展示子图"],
  ["stat_image", "统计原始图"], ["statistics", "统计数据"],
  ["other", "其他附件"], ["unspecified", "待分类"]
];
const folders = ["全部文件", "Figure/组合图", "Figure/子图", "Figure/统计原始图", "Table", "Others", "待处理"];
const scan = ref<FolderScan | null>(null);
const rows = ref<Row[]>([]);
const selected = ref<string[]>([]);
const active = ref<string | null>(null);
const folder = ref("全部文件");
const search = ref("");
const busy = ref(false);
const message = ref("");
const error = ref("");
const undoStack = ref<Row[][]>([]);
const redoStack = ref<Row[][]>([]);
const showRename = ref(false);
const figure = ref(1);
const panel = ref("A");
const sequence = ref(1);
const keepDescription = ref(true);
const context = ref<{ x: number; y: number } | null>(null);
const page = ref(0);
const drafts = ref<Paper[]>([]);
const preview = ref<string|null>(null);
let previewRequest = 0;
const size = (n: number) => n < 1024 ? n + " B" : n < 1048576 ? (n / 1024).toFixed(1) + " KB" : (n / 1048576).toFixed(2) + " MB";
const fileName = (path: string) => path.split("/").pop() || path;
const parent = (path: string) => path.includes("/") ? path.slice(0, path.lastIndexOf("/")) : "";
const clone = (list: Row[]) => list.map(r => ({...r}));
const selectedSet = computed(() => new Set(selected.value));
const chosen = computed(() => rows.value.filter(r => selectedSet.value.has(r.source_relative_path)));
const current = computed(() => rows.value.find(r => r.source_relative_path === active.value));
const issues = computed(() => rows.value.filter(r => r.issue || r.role === "unspecified"));
const conflicts = computed(() => {
  const counts = new Map<string, number>();
  rows.value.forEach(r => {
    const key = r.logical_relative_path.toLowerCase();
    counts.set(key, (counts.get(key) || 0) + 1);
  });
  return rows.value.filter(r => (counts.get(r.logical_relative_path.toLowerCase()) || 0) > 1);
});
const visible = computed(() => rows.value.filter(r => {
  const matchFolder = folder.value === "全部文件" ||
    (folder.value === "待处理" ? Boolean(r.issue || r.role === "unspecified" || conflicts.value.includes(r)) :
    r.logical_relative_path.startsWith(folder.value + "/"));
  return matchFolder && (r.logical_relative_path + " " + r.role).toLowerCase().includes(search.value.toLowerCase());
}).sort((a,b) => a.logical_relative_path.localeCompare(b.logical_relative_path, "zh-CN", {numeric:true})));
const pageRows = computed(() => visible.value.slice(page.value * 150, page.value * 150 + 150));
const pages = computed(() => Math.max(1, Math.ceil(visible.value.length / 150)));
const renamePreview = computed(() => chosen.value.map((r,i) => {
  const original = fileName(r.logical_relative_path);
  const dot = original.lastIndexOf(".");
  const ext = dot > 0 ? original.slice(dot) : "";
  const stem = dot > 0 ? original.slice(0,dot) : original;
  const suffix = stem.replace(/^\d+[-_][A-Za-z][-_]?\d*[-_]?/, "");
  const renamed = String(figure.value) + "-" + panel.value.toUpperCase() + "-" + (sequence.value + i) +
    (keepDescription.value && suffix ? "-" + suffix : "") + ext;
  return {id:r.source_relative_path,before:r.logical_relative_path,after:(parent(r.logical_relative_path) ? parent(r.logical_relative_path)+"/" : "")+renamed};
}));
let rangeAnchor = 0;
function apply(next:Row[],label:string) {
  undoStack.value.push(clone(rows.value));
  if(undoStack.value.length>30)undoStack.value.shift();
  redoStack.value=[];
  rows.value=next;
  message.value=label;
  error.value="";
}
function undo() {const old=undoStack.value.pop();if(!old)return;redoStack.value.push(clone(rows.value));rows.value=old;}
function redo() {const old=redoStack.value.pop();if(!old)return;undoStack.value.push(clone(rows.value));rows.value=old;}
function choose(row:Row,e:MouseEvent) {
  const id=row.source_relative_path;
  active.value=id;
  void loadPreview(row);
  const index=visible.value.findIndex(r=>r.source_relative_path===id);
  if(e.shiftKey) selected.value=visible.value.slice(Math.min(rangeAnchor,index),Math.max(rangeAnchor,index)+1).map(r=>r.source_relative_path);
  else if(e.ctrlKey||e.metaKey){selected.value=selectedSet.value.has(id)?selected.value.filter(v=>v!==id):[...selected.value,id];rangeAnchor=index;}
  else{selected.value=[id];rangeAnchor=index;}
}
function mutate(transform:(r:Row,i:number)=>Row,label:string){
  let idx=0;
  const next=rows.value.map(row=>selectedSet.value.has(row.source_relative_path)?transform({...row},idx++):{...row});
  apply(next,label+"（"+idx+" 个文件）");context.value=null;
}
function move(to:string){
  mutate(r=>{
    r.logical_relative_path=to+"/"+fileName(r.logical_relative_path);
    r.role=to==="Table"?"statistics":to==="Others"?"other":to==="Figure/组合图"?"composite":to==="Figure/子图"?"display":"stat_image";
    r.issue=null;return r;
  },"批量移动");
}
function setRole(role:string){mutate(r=>({...r,role,issue:null}),"批量设置角色");}
function assign(){
  if(!Number.isInteger(figure.value)||figure.value<1||!/^[A-Z]$/i.test(panel.value)){error.value="Figure/Panel 编号不合法";return;}
  mutate(r=>({...r,figure_number:figure.value,panel_label:panel.value.toUpperCase(),issue:null}),"批量设置 Figure/Panel");
}
function rename(){
  if(!Number.isInteger(figure.value)||figure.value<1||!/^[A-Z]$/i.test(panel.value)||!Number.isInteger(sequence.value)||sequence.value<1){
    error.value="请检查编号参数";return;
  }
  const updates=new Map(renamePreview.value.map(x=>[x.id,x.after]));
  const next=rows.value.map(r=>{
    const newName=updates.get(r.source_relative_path);
    if(!newName)return {...r};
    const index=renamePreview.value.findIndex(x=>x.id===r.source_relative_path);
    return {...r,logical_relative_path:newName,figure_number:figure.value,panel_label:panel.value.toUpperCase(),sequence:sequence.value+index,issue:null};
  });
  const names=next.map(r=>r.logical_relative_path.toLowerCase());
  if(new Set(names).size!==names.length){error.value="新名称出现冲突，未执行任何修改";return;}
  apply(next,"批量重命名（仅逻辑名称，不改源文件）");showRename.value=false;
}
function clearPreview(){
  previewRequest++;
  if(preview.value)URL.revokeObjectURL(preview.value);
  preview.value=null;
}
async function loadPreview(row:Row){
  clearPreview();
  const ext=fileName(row.source_relative_path).split(".").pop()?.toLowerCase();
  if(!scan.value || !ext || !["png","jpg","jpeg","webp"].includes(ext))return;
  const token=++previewRequest;
  try{
    const bytes=await invoke<ArrayBuffer>("read_scan_image",{
      root:scan.value.folder,
      relativePath:row.source_relative_path
    });
    if(token!==previewRequest)return;
    const mime=ext==="png"?"image/png":ext==="webp"?"image/webp":"image/jpeg";
    preview.value=URL.createObjectURL(new Blob([bytes],{type:mime}));
  }catch{ /* file metadata remains visible for TIFF and unsupported formats */ }
}
async function refreshDrafts(){
  try{drafts.value=(await invoke<Paper[]>("list_papers")).filter(p=>p.archive_status!=="archived");}
  catch{drafts.value=[];}
}
async function loadFolder(){
  const dir=await open({directory:true,multiple:false,title:"打开论文文件夹"});
  if(typeof dir!=="string")return;
  busy.value=true;error.value="";message.value="扫描中";
  try{
    scan.value=await invoke<FolderScan>("scan_paper_folder",{path:dir});
    rows.value=scan.value.entries.map((e:ScanEntry)=>({
      source_relative_path:e.relative_path,logical_relative_path:e.relative_path,
      figure_number:e.figure_number,panel_label:e.panel_label,sequence:e.sequence,
      role:e.role,file_size:e.file_size,issue:e.issue
    }));
    selected.value=[];active.value=null;folder.value="全部文件";page.value=0;
    undoStack.value=[];redoStack.value=[];
    clearPreview();
    message.value="扫描完成：自动识别 "+rows.value.length+" 个文件";
  }catch(e){error.value=String(e);}finally{busy.value=false;}
}
async function finalize(){
  if(!scan.value||busy.value)return;
  if(conflicts.value.length){error.value="存在重复逻辑文件名，请先处理";return;}
  busy.value=true;message.value="正在归档，原始文件保持不变";error.value="";
  try{
    const result=await invoke<ImportResult>("import_paper_plan",{
      path:scan.value.folder,
      edits:rows.value.map(r=>({
        source_relative_path:r.source_relative_path,logical_relative_path:r.logical_relative_path,
        role:r.role,figure_number:r.figure_number,panel_label:r.panel_label,sequence:r.sequence
      }))
    });
    await refreshDrafts();
    if(result.failed.length){error.value="部分文件失败，论文保留为草稿："+result.failed.join("; ");return;}
    const check=await invoke<{can_archive:boolean;issues:{severity:string;message:string}[]}>("check_paper_archive",{paperId:result.paper.paper_id});
    if(!check.can_archive){error.value="已保存草稿；检查未通过："+check.issues.filter(i=>i.severity==="error").map(i=>i.message).join("; ");return;}
    await invoke("finalize_paper_archive",{paperId:result.paper.paper_id});
    scan.value=null;rows.value=[];message.value="归档完成";
    emit("archive-completed",result.paper.paper_id);
  }catch(e){error.value=String(e);}finally{busy.value=false;}
}
function keydown(e:KeyboardEvent){
  if(["INPUT","TEXTAREA","SELECT"].includes((e.target as HTMLElement).tagName)||showRename.value)return;
  if(e.ctrlKey&&e.key.toLowerCase()==="o"){e.preventDefault();void loadFolder();}
  if(e.ctrlKey&&e.key.toLowerCase()==="a"){e.preventDefault();selected.value=visible.value.map(r=>r.source_relative_path);}
  if(e.ctrlKey&&e.key.toLowerCase()==="z"){e.preventDefault();undo();}
  if(e.ctrlKey&&e.key.toLowerCase()==="y"){e.preventDefault();redo();}
  if(e.ctrlKey&&e.key.toLowerCase()==="f"){e.preventDefault();document.getElementById("file-search")?.focus();}
  if(e.key==="F2"&&selected.value.length){e.preventDefault();showRename.value=true;}
  if(e.key==="Escape"){context.value=null;selected.value=[];}
}
onMounted(()=>{window.addEventListener("keydown",keydown);void refreshDrafts();});
onUnmounted(()=>{window.removeEventListener("keydown",keydown);clearPreview();});
</script>

<template>
<div class="workspace">
  <header class="toolbar">
    <div class="commands">
      <button class="highlight" :disabled="busy" @click="loadFolder">▤　打开论文文件夹 <small>Ctrl+O</small></button>
      <button :disabled="!selected.length" @click="showRename=true">批量重命名 <small>F2</small></button>
      <button :disabled="!selected.length" @click="assign">设置 Figure / Panel</button>
      <button :disabled="!undoStack.length" @click="undo">↶ 撤销</button>
      <button :disabled="!redoStack.length" @click="redo">↷ 重做</button>
    </div>
    <div class="commands">
      <button @click="folder='待处理';page=0">问题检查 <span v-if="issues.length" class="problem">{{ issues.length }}</span></button>
      <button class="highlight" :disabled="!scan||busy||!!conflicts.length" @click="finalize">{{ busy?"处理中…":"完成归档" }}</button>
    </div>
  </header>
  <div class="columns">
    <aside class="tree">
      <strong class="pane-title">目录</strong>
      <p class="category">当前论文</p>
      <div class="paper-label">{{ scan?.paper_title||"尚未打开" }}</div>
      <button v-for="name in folders" :key="name"
        :class="{active:folder===name,child:name.startsWith('Figure/')}"
        @click="folder=name;page=0;selected=[]"
        @dragover.prevent @drop.prevent="name.startsWith('Figure/')||name==='Table'||name==='Others'?move(name):undefined">
        <span>▤</span>{{ name }}
        <small>{{ name==='全部文件'?rows.length:name==='待处理'?issues.length:rows.filter(r=>r.logical_relative_path.startsWith(name+'/')).length }}</small>
      </button>
      <p class="category">待完成草稿</p>
      <p v-for="draft in drafts" :key="draft.paper_id" class="draft">▧ {{ draft.title }}</p>
      <p v-if="!drafts.length" class="quiet">无草稿</p>
    </aside>
    <section class="center">
      <div class="crumb">▤　{{ scan?.folder||"选择包含 Figure、Table、Others 的论文目录" }}</div>
      <div class="searchline">
        <input id="file-search" v-model="search" placeholder="搜索文件、图表编号、角色　Ctrl+F" @input="page=0" />
        <span>{{ visible.length }} / {{ rows.length }} 文件</span>
      </div>
      <div v-if="!scan" class="welcome">
        <span class="welcome-icon">▤</span>
        <h2>按目录组织，整篇论文一次归档</h2>
        <p>保持 Figure / 组合图 / 子图 / 统计原始图、Table、Others 结构。导入后可多选、批量整理并检查，系统自动建立图表关联。</p>
        <button class="highlight" @click="loadFolder">选择论文文件夹</button>
      </div>
      <template v-else>
        <div v-if="selected.length" class="batchbar">
          <span>选中 {{ selected.length }} 个文件</span>
          <select @change="e=>{const el=e.target as HTMLSelectElement;if(el.value)setRole(el.value);el.value='';}">
            <option value="">修改角色…</option><option v-for="role in roles" :key="role[0]" :value="role[0]">{{ role[1] }}</option>
          </select>
          <select @change="e=>{const el=e.target as HTMLSelectElement;if(el.value)move(el.value);el.value='';}">
            <option value="">逻辑移动到…</option><option v-for="name in folders.filter(n=>n.startsWith('Figure/')||n==='Table'||n==='Others')" :key="name" :value="name">{{ name }}</option>
          </select>
          <button @click="showRename=true">批量命名</button>
          <button @click="selected=[]">取消</button>
        </div>
        <div class="table-scroll">
          <table><thead><tr>
            <th><input type="checkbox" :checked="!!selected.length&&selected.length===visible.length"
              @change="selected.length===visible.length?selected=[]:selected=visible.map(r=>r.source_relative_path)" /></th>
            <th>文件名称</th><th>归属</th><th>文件角色</th><th>大小</th><th>状态</th>
          </tr></thead>
          <tbody><tr v-for="row in pageRows" :key="row.source_relative_path"
            :class="{selected:selectedSet.has(row.source_relative_path)}" draggable="true"
            @dragstart="selectedSet.has(row.source_relative_path)?undefined:(selected=[row.source_relative_path])"
            @click="choose(row,$event)"
            @contextmenu.prevent.stop="e=>{if(!selectedSet.has(row.source_relative_path))selected=[row.source_relative_path];active=row.source_relative_path;context={x:e.clientX,y:e.clientY};}">
            <td><input type="checkbox" :checked="selectedSet.has(row.source_relative_path)" @click.stop="choose(row,$event)" /></td>
            <td class="name"><span>▧</span><div><strong>{{ fileName(row.logical_relative_path) }}</strong><small>{{ parent(row.logical_relative_path)||'论文根目录' }}</small></div></td>
            <td>{{ row.figure_number ? 'Figure '+row.figure_number+(row.panel_label||'') : '—' }}</td>
            <td><span class="role">{{ roles.find(x=>x[0]===row.role)?.[1]||'论文正文' }}</span></td>
            <td>{{ size(row.file_size) }}</td>
            <td :class="conflicts.includes(row)?'bad':row.issue?'warn':'good'">{{ conflicts.includes(row)?"命名冲突":row.issue?"待检查":"已识别" }}</td>
          </tr></tbody></table>
        </div>
        <div class="pager"><span>每页 150 条</span><div><button :disabled="page===0" @click="page--">上一页</button> {{ page+1 }} / {{ pages }} <button :disabled="page>=pages-1" @click="page++">下一页</button></div></div>
      </template>
    </section>
    <aside class="inspector">
      <header>文件检查器 <small>INSPECTOR</small></header>
      <template v-if="current">
        <div class="preview-icon"><img v-if="preview" :src="preview" alt="原始文件预览" /><span v-else>▧</span></div>
        <h3>{{ fileName(current.logical_relative_path) }}</h3>
        <dl>
          <dt>原始文件名</dt><dd>{{ fileName(current.source_relative_path) }}</dd>
          <dt>整理目录</dt><dd>{{ parent(current.logical_relative_path) }}</dd>
          <dt>文件角色</dt><dd>{{ roles.find(x=>x[0]===current?.role)?.[1]||current.role }}</dd>
          <dt>归属</dt><dd>{{ current.figure_number||'—' }} / {{ current.panel_label||'—' }}</dd>
          <dt>内部序号</dt><dd>{{ current.sequence||'—' }}</dd>
          <dt>大小</dt><dd>{{ size(current.file_size) }}</dd>
          <dt v-if="current.issue">识别提醒</dt><dd v-if="current.issue" class="warn">{{ current.issue }}</dd>
        </dl>
      </template>
      <div v-else class="inspector-empty">▧<p>单击文件查看信息</p></div>
      <div class="audit">
        <strong>整理概况</strong>
        <p>扫描文件 <b>{{ rows.length }}</b></p>
        <p>识别问题 <b>{{ issues.length }}</b></p>
        <p>命名冲突 <b>{{ conflicts.length }}</b></p>
        <p>可撤销操作 <b>{{ undoStack.length }}</b></p>
        <small>修改只作用于归档映射，不改变源文件。</small>
      </div>
    </aside>
  </div>
  <footer class="status"><span>{{ error||message||'就绪' }}</span><span>{{ scan?size(scan.total_bytes):'离线单机' }} · 源文件只读 · V0.5</span></footer>
  <div v-if="context" class="context" :style="{left:Math.min(context.x,innerWidth-210)+'px',top:Math.min(context.y,innerHeight-300)+'px'}" @click.stop>
    <button @click="showRename=true;context=null">批量重命名　F2</button>
    <button @click="assign();context=null">设置 Figure / Panel</button>
    <hr />
    <button @click="move('Figure/子图')">移入展示子图</button>
    <button @click="move('Figure/统计原始图')">移入统计原始图</button>
    <button @click="move('Table')">移入统计表格</button>
    <button @click="move('Others')">移入 Others</button>
    <hr /><button @click="undo();context=null">撤销　Ctrl+Z</button>
  </div>
  <div v-if="showRename" class="scrim" @click.self="showRename=false">
    <div class="dialog">
      <header><h2>批量重命名</h2><button @click="showRename=false">×</button></header>
      <p>预览 {{ selected.length }} 个文件的新名称。仅修改归档映射，不修改源文件。</p>
      <div class="form">
        <label>Figure<input v-model.number="figure" type="number" min="1" /></label>
        <label>Panel<input v-model="panel" maxlength="1" /></label>
        <label>起始序号<input v-model.number="sequence" type="number" min="1" /></label>
        <label><input v-model="keepDescription" type="checkbox" /> 保留原描述</label>
      </div>
      <div class="changes"><p v-for="item in renamePreview" :key="item.id">{{ fileName(item.before) }}　→　<strong>{{ fileName(item.after) }}</strong></p></div>
      <footer><button @click="showRename=false">取消</button><button class="highlight" @click="rename">应用整理计划</button></footer>
    </div>
  </div>
</div>
</template>

<style scoped>
.workspace{display:flex;flex-direction:column;height:100%;min-height:0;background:#f5f7fa;color:#30465b;font-size:12px}
.toolbar{min-height:54px;display:flex;justify-content:space-between;gap:10px;padding:7px 14px;border-bottom:1px solid #dfe6ed;background:#fff}
.commands{display:flex;align-items:center;gap:4px;flex-wrap:wrap}
button{cursor:pointer;font:inherit}button:disabled{opacity:.45;cursor:not-allowed}
.commands button{padding:9px 11px;border:1px solid transparent;border-radius:5px;background:transparent;color:#45617a;white-space:nowrap}
.commands button:hover{background:#f1f6fa}.commands button.highlight,.highlight{background:#1e5b85;color:#fff;border:1px solid #1e5b85;border-radius:6px;padding:10px 14px}
.commands small{color:#91a4b5;font-size:10px}.commands .highlight small{color:#bfd7e8}
.problem{padding:2px 6px;border-radius:9px;background:#fdf0d9;color:#a8771f}
.columns{display:grid;grid-template-columns:220px minmax(390px,1fr) 265px;flex:1;min-height:0}
.tree{padding:18px 9px;background:#f8fafc;border-right:1px solid #dde7ef;overflow:auto}
.pane-title{display:block;margin:0 11px 20px;font-size:14px}.category{margin:20px 11px 10px;font-size:10px;color:#91a3b3;font-weight:700}
.paper-label{padding:9px 12px;font-weight:650;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.tree button{display:flex;align-items:center;gap:8px;width:100%;padding:10px 12px;border:0;border-radius:5px;background:transparent;text-align:left;color:#45637a;font-size:11px}
.tree button.child{padding-left:27px}.tree button.active{background:#e2f0fa;color:#23648e;font-weight:700}.tree button small{margin-left:auto;color:#9eb0be}
.draft{padding:6px 12px;font-size:10px;line-height:1.6}.quiet{color:#acb5c0;font-size:11px;padding:0 12px}
.center{display:flex;flex-direction:column;min-width:0;min-height:0;background:#fff}
.crumb{height:47px;padding:15px;border-bottom:1px solid #e3eaf0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#59778d}
.searchline{display:flex;align-items:center;justify-content:space-between;padding:11px 15px;gap:16px;color:#8a9eac;font-size:11px}
.searchline input{width:min(430px,75%);padding:10px;border:1px solid #d9e4ec;border-radius:7px;background:#f9fbfd;outline:none;font-size:12px}
.welcome{flex:1;display:flex;align-items:center;justify-content:center;flex-direction:column;text-align:center;padding:30px}
.welcome-icon{font-size:54px;color:#83a3bb}.welcome h2{font-size:18px}.welcome p{max-width:470px;line-height:1.8;color:#91a2b0}
.batchbar{display:flex;align-items:center;gap:10px;padding:10px 15px;background:#e9f4fc;border-top:1px solid #d7eafa;border-bottom:1px solid #d7eafa;color:#2f698e}
.batchbar select,.batchbar button{padding:7px;border:1px solid #c3dce9;border-radius:5px;background:white;color:#3f6b85;font-size:11px}
.table-scroll{flex:1;min-height:0;overflow:auto}.table-scroll table{width:100%;border-collapse:collapse}
.table-scroll th{position:sticky;top:0;z-index:1;background:#f3f6f9;border-bottom:1px solid #dee7ee;color:#8799a8;font-size:11px;padding:12px;text-align:left;white-space:nowrap}
.table-scroll td{border-bottom:1px solid #edf2f6;padding:11px 12px;color:#526e82;white-space:nowrap;font-size:11px}
.table-scroll tr{cursor:pointer}.table-scroll tr.selected{background:#e7f2fc}.table-scroll tbody tr:hover{background:#f0f7fd}
.name{display:flex;align-items:center;gap:9px;min-width:200px;max-width:420px}.name>span{font-size:22px;color:#779bb6}
.name div{min-width:0;display:flex;flex-direction:column;gap:6px}.name strong,.name small{overflow:hidden;white-space:nowrap;text-overflow:ellipsis}
.name strong{font-size:12px;color:#355169}.name small{font-size:10px;color:#9eabb6}
.role{background:#f0f4f7;border-radius:5px;padding:5px 8px;color:#60819a}.good{color:#409a74!important}.warn{color:#b77c25!important}.bad{color:#be4234!important}
.pager{display:flex;justify-content:space-between;align-items:center;padding:9px 14px;border-top:1px solid #e6edf2;color:#93a5b3;font-size:11px}
.pager button{padding:6px 9px;margin:0 7px;border:1px solid #d7e3eb;background:white;border-radius:5px}
.inspector{display:flex;flex-direction:column;min-height:0;overflow:auto;background:#fafcfe;border-left:1px solid #dfe7ef;padding:0 15px}
.inspector header{display:flex;justify-content:space-between;align-items:center;padding:18px 0;border-bottom:1px solid #e4eaf0;font-size:14px;font-weight:700}
.inspector header small{font-size:9px;color:#9eafbc}.preview-icon{display:grid;place-items:center;height:180px;margin-top:18px;border-radius:8px;background:#edf3f7;font-size:44px;color:#9fb7c8}
.inspector h3{font-size:12px;overflow-wrap:anywhere}.inspector dl{margin:0}.inspector dt{color:#9daab7;font-size:10px;margin-top:14px}
.inspector dd{font-size:11px;color:#4e697e;margin:6px 0;overflow-wrap:anywhere}
.inspector-empty{flex:1;display:grid;align-content:center;justify-content:center;text-align:center;color:#abc0d0;font-size:37px}
.inspector-empty p{font-size:11px;color:#9eafb9}.audit{margin-top:auto;border-top:1px solid #e5ebf1;padding:17px 0}
.audit p{display:flex;justify-content:space-between;color:#8297aa;font-size:11px}.audit b{color:#46677f}
.audit small{display:block;line-height:1.7;color:#95a6b4}
.status{min-height:30px;display:flex;justify-content:space-between;align-items:center;gap:20px;padding:0 13px;border-top:1px solid #dce7ed;background:#edf3f7;color:#66829a;font-size:10px}
.context{position:fixed;z-index:30;width:205px;padding:6px;border:1px solid #d6e2eb;border-radius:8px;background:#fff;box-shadow:0 12px 35px #27394f25}
.context button{width:100%;padding:9px;border:0;background:transparent;text-align:left;font-size:11px}.context button:hover{background:#eaf4fc}.context hr{border:0;border-top:1px solid #e8edf2}
.scrim{position:fixed;inset:0;z-index:40;display:grid;place-items:center;background:#152a416b}
.dialog{width:650px;max-width:90vw;padding:20px 25px;background:#fff;border-radius:12px;box-shadow:0 16px 60px #0003}
.dialog header,.dialog footer{display:flex;align-items:center;justify-content:space-between}
.dialog header h2{font-size:18px}.dialog header button{border:0;background:transparent;font-size:22px}
.dialog>p{color:#8093a5;font-size:12px}.form{display:flex;gap:12px;align-items:center;margin:20px 0}
.form label{display:flex;flex-direction:column;gap:6px;color:#6b859b;font-size:11px}.form input:not([type=checkbox]){width:90px;padding:9px;border:1px solid #cfdce7;border-radius:5px}
.changes{height:240px;overflow:auto;border:1px solid #e0e7ed;border-radius:6px;padding:0 12px}.changes p{font-size:11px;overflow-wrap:anywhere;border-bottom:1px solid #e8edf2;padding:5px}
.changes strong{color:#286891}.dialog footer{justify-content:flex-end;gap:10px;margin-top:16px}
.dialog footer button{padding:10px 15px;border:1px solid #d0deea;background:white;border-radius:6px}.dialog footer .highlight{background:#1e5b85;color:white}
</style>
<style scoped>
.preview-icon img{max-width:100%;max-height:100%;object-fit:contain}
</style>
