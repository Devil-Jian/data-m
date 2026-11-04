<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";
import ArticleViewer from "./ArticleViewer.vue";
import type {
  ArchiveStatus, DataDetails, DataRecord, ItemAnchor,
  Paper, PaperItem, PaperResource,
} from "./types";

const status = ref<ArchiveStatus | null>(null);
const papers = ref<Paper[]>([]);
const items = ref<PaperItem[]>([]);
const resources = ref<PaperResource[]>([]);
const allData = ref<DataRecord[]>([]);
const dataDetails = ref<DataDetails | null>(null);
const selectedPaperId = ref<string | null>(null);
const selectedItemId = ref<string | null>(null);
const selectedDataId = ref<string | null>(null);
const bindingItemId = ref<string | null>(null);
const previewUrl = ref<string | null>(null);
const paperSearch = ref("");
const resourceSearch = ref("");
const busy = ref(false);
const startupLoading = ref(true);
const paperLoading = ref(false);
const successMessage = ref("");
const errorMessage = ref("");
const showNewPaper = ref(false);
const showNewItem = ref(false);
const newTitle = ref("");
const newDoi = ref("");
const newItemType = ref<"figure" | "panel" | "table">("figure");
const newItemLabel = ref("");
const newItemParent = ref("");
const existingDataId = ref("");

let loadToken = 0;

const currentPaper = computed(() => papers.value.find(p => p.paper_id === selectedPaperId.value) ?? null);
const selectedItem = computed(() => items.value.find(item => item.item_id === selectedItemId.value) ?? null);
const ready = computed(() => status.value?.initialized === true);
const filteredPapers = computed(() => papers.value.filter(paper =>
  [paper.title, paper.doi ?? ""].some(value => value.toLowerCase().includes(paperSearch.value.toLowerCase())),
));
const topItems = computed(() => items.value.filter(i => i.item_type !== "panel"));
const figureItems = computed(() => items.value.filter(i => i.item_type === "figure"));
const orderedItems = computed(() => topItems.value.flatMap(item => [
  item, ...items.value.filter(child => child.parent_id === item.item_id),
]));
const availableData = computed(() => allData.value.filter(
  data => !resources.value.some(r => r.item_id === selectedItemId.value && r.data_id === data.data_id),
));
const filteredResources = (itemId: string) => resources.value.filter(r =>
  r.item_id === itemId && r.original_filename.toLowerCase().includes(resourceSearch.value.toLowerCase()),
);

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const power = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 4);
  return `${(value / 1024 ** power).toFixed(2)} ${["B", "KB", "MB", "GB", "TB"][power]}`;
}
function briefId(id: string): string {
  return id.length > 19 ? `${id.slice(0, 13)}…${id.slice(-7)}` : id;
}
function report(error: unknown, prefix = "操作失败") {
  errorMessage.value = `${prefix}：${String(error)}`;
}
function clearFeedback() {
  successMessage.value = "";
  errorMessage.value = "";
}
function releasePreview() {
  if (previewUrl.value) URL.revokeObjectURL(previewUrl.value);
  previewUrl.value = null;
}

async function refreshPapers() {
  papers.value = await invoke<Paper[]>("list_papers");
}
async function refreshData() {
  allData.value = await invoke<DataRecord[]>("list_data_records");
}
async function refreshPaperContents(paperId: string) {
  const token = ++loadToken;
  paperLoading.value = true;
  try {
    const [newItems, newResources] = await Promise.all([
      invoke<PaperItem[]>("list_paper_items", { paperId }),
      invoke<PaperResource[]>("list_paper_resources", { paperId }),
    ]);
    if (token !== loadToken || selectedPaperId.value !== paperId) return;
    items.value = newItems;
    resources.value = newResources;
    if (selectedItemId.value && !items.value.some(i => i.item_id === selectedItemId.value)) {
      selectedItemId.value = null;
    }
    await nextTick();
  } catch (error) {
    report(error, "读取论文资源失败");
  } finally {
    if (token === loadToken) paperLoading.value = false;
  }
}
async function selectPaper(paperId: string) {
  if (selectedPaperId.value !== paperId) {
    selectedPaperId.value = paperId;
    selectedItemId.value = null;
    selectedDataId.value = null;
    dataDetails.value = null;
    bindingItemId.value = null;
    releasePreview();
    items.value = [];
    resources.value = [];
  }
  await refreshPaperContents(paperId);
}
async function initialize() {
  startupLoading.value = true;
  try {
    status.value = await invoke<ArchiveStatus>("get_archive_status");
    if (ready.value) {
      await Promise.all([refreshPapers(), refreshData()]);
      if (papers.value.length) await selectPaper(papers.value[0].paper_id);
    }
  } catch (error) {
    report(error, "初始化失败");
  } finally {
    startupLoading.value = false;
  }
}
async function chooseArchive() {
  if (busy.value) return;
  clearFeedback();
  const directory = await open({ directory: true, multiple: false, title: "选择本地论文归档库" });
  if (typeof directory !== "string") return;
  busy.value = true;
  try {
    status.value = await invoke<ArchiveStatus>("initialize_archive", { path: directory });
    selectedPaperId.value = null;
    selectedItemId.value = null;
    bindingItemId.value = null;
    releasePreview();
    await Promise.all([refreshPapers(), refreshData()]);
    if (papers.value.length) await selectPaper(papers.value[0].paper_id);
    successMessage.value = "归档库已初始化，已有论文和文件均可继续使用。";
  } catch (error) {
    report(error, "无法切换归档库");
  } finally {
    busy.value = false;
  }
}

async function createPaper() {
  if (!newTitle.value.trim() || busy.value) return;
  busy.value = true;
  clearFeedback();
  try {
    const paper = await invoke<Paper>("create_paper", {
      title: newTitle.value.trim(),
      doi: newDoi.value.trim() || null,
    });
    showNewPaper.value = false;
    newTitle.value = "";
    newDoi.value = "";
    await refreshPapers();
    await selectPaper(paper.paper_id);
    successMessage.value = "论文条目已建立，现在可以导入正文并建立 Figure / Panel / Table。";
  } catch (error) {
    report(error, "创建论文失败");
  } finally {
    busy.value = false;
  }
}
async function importManuscript() {
  if (!currentPaper.value || busy.value) return;
  const path = await open({
    multiple: false,
    title: "为当前论文选择 PDF 或 DOCX 正文",
    filters: [{ name: "论文正文", extensions: ["pdf", "docx"] }],
  });
  if (typeof path !== "string") return;
  busy.value = true;
  clearFeedback();
  try {
    await invoke<Paper>("attach_paper_manuscript", {
      paperId: currentPaper.value.paper_id,
      path,
    });
    await Promise.all([refreshPapers(), refreshData()]);
    successMessage.value = "论文正文已归档，可以在中间阅读并绑定图表区域。";
  } catch (error) {
    report(error, "导入论文正文失败");
  } finally {
    busy.value = false;
  }
}

async function createItem() {
  if (!currentPaper.value || !newItemLabel.value.trim() || busy.value) return;
  busy.value = true;
  clearFeedback();
  try {
    const item = await invoke<PaperItem>("create_paper_item", {
      paperId: currentPaper.value.paper_id,
      itemType: newItemType.value,
      label: newItemLabel.value.trim(),
      parentId: newItemType.value === "panel" ? newItemParent.value || null : null,
    });
    await refreshPaperContents(currentPaper.value.paper_id);
    await refreshPapers();
    showNewItem.value = false;
    newItemLabel.value = "";
    await selectItem(item.item_id);
    successMessage.value = "图表条目已建立。可以导入原始数据，并在文章中绑定对应图像位置。";
  } catch (error) {
    report(error, "创建图表条目失败");
  } finally {
    busy.value = false;
  }
}
async function selectItem(itemId: string) {
  selectedItemId.value = itemId;
  bindingItemId.value = null;
  existingDataId.value = "";
  const resource = resources.value.find(r => r.item_id === itemId);
  if (resource) await selectData(resource.data_id);
  else {
    selectedDataId.value = null;
    dataDetails.value = null;
    releasePreview();
  }
  await nextTick();
  document.getElementById(`resource-group-${itemId}`)?.scrollIntoView({
    behavior: "smooth", block: "center",
  });
}
function toggleBinding(itemId: string) {
  selectedItemId.value = itemId;
  bindingItemId.value = bindingItemId.value === itemId ? null : itemId;
  clearFeedback();
}
async function bindAnchor(itemId: string, anchor: ItemAnchor) {
  if (!currentPaper.value) return;
  clearFeedback();
  try {
    await invoke<void>("set_item_anchor", {
      paperId: currentPaper.value.paper_id,
      itemId,
      anchor,
    });
    bindingItemId.value = null;
    await refreshPaperContents(currentPaper.value.paper_id);
    successMessage.value = "定位绑定成功。以后点击论文中的该图像或表格即可跳转至右侧资源目录。";
    await selectItem(itemId);
  } catch (error) {
    report(error, "无法保存图表位置");
  }
}
async function linkExistingData() {
  if (!currentPaper.value || !selectedItemId.value || !existingDataId.value || busy.value) return;
  busy.value = true;
  clearFeedback();
  try {
    await invoke<string>("link_data_to_item", {
      paperId: currentPaper.value.paper_id,
      itemId: selectedItemId.value,
      dataId: existingDataId.value,
    });
    await refreshPaperContents(currentPaper.value.paper_id);
    await refreshPapers();
    await selectData(existingDataId.value);
    successMessage.value = "已有文件已关联到论文图表。";
    existingDataId.value = "";
  } catch (error) {
    report(error, "关联数据失败");
  } finally {
    busy.value = false;
  }
}
async function importItemFiles() {
  if (!currentPaper.value || !selectedItemId.value || busy.value) return;
  const files = await open({ multiple: true, title: "导入当前图表的原始数据" });
  if (!files) return;
  const paths = typeof files === "string" ? [files] : files;
  const paperId = currentPaper.value.paper_id;
  const itemId = selectedItemId.value;
  if (!paths.length) return;
  busy.value = true;
  clearFeedback();
  const failures: string[] = [];
  let count = 0;
  try {
    for (const path of paths) {
      try {
        const record = await invoke<DataRecord>("import_data_file", { path });
        await invoke<string>("link_data_to_item", {
          paperId, itemId, dataId: record.data_id,
        });
        count += 1;
      } catch (error) {
        failures.push(`${path}: ${String(error)}`);
      }
    }
    await Promise.all([refreshData(), refreshPapers()]);
    await refreshPaperContents(paperId);
    if (count) successMessage.value = `已归档并关联 ${count} 个文件。`;
    if (failures.length) errorMessage.value = `${failures.length} 个文件未成功关联：\n${failures.join("\n")}`;
  } finally {
    busy.value = false;
  }
}
async function selectData(dataId: string) {
  selectedDataId.value = dataId;
  dataDetails.value = null;
  releasePreview();
  try {
    const details = await invoke<DataDetails>("get_data_details", { dataId });
    if (selectedDataId.value !== dataId) return;
    dataDetails.value = details;
    if (["png", "jpg", "jpeg", "webp"].includes(details.record.file_extension)) {
      try {
        const binary = await invoke<ArrayBuffer>("read_data_image", { dataId });
        if (selectedDataId.value !== dataId) return;
        const mime = details.record.file_extension === "png"
          ? "image/png" : details.record.file_extension === "webp" ? "image/webp" : "image/jpeg";
        previewUrl.value = URL.createObjectURL(new Blob([binary], { type: mime }));
      } catch {
        // Unsupported or oversized images still show full metadata and file access.
      }
    }
  } catch (error) {
    report(error, "无法读取文件详情");
  }
}
async function revealFile() {
  if (!selectedDataId.value) return;
  try {
    await invoke<void>("reveal_data_file", { dataId: selectedDataId.value });
  } catch (error) {
    report(error, "定位原始文件失败");
  }
}
async function jumpToUsage(paperId: string, itemId: string) {
  if (selectedPaperId.value !== paperId) await selectPaper(paperId);
  await selectItem(itemId);
}

onMounted(() => { void initialize(); });
onBeforeUnmount(() => { releasePreview(); });
</script>

<template>
  <div class="app-frame">
    <header class="topbar">
      <div class="brand"><span class="brand-mark">RD</span><span>论文原始数据归档平台</span></div>
      <div class="top-actions">
        <span class="local-status"><span class="status-dot"></span>Windows 本地单机</span>
        <button class="text-button" :disabled="busy" @click="chooseArchive">数据仓库设置</button>
        <span class="version">v0.2.0</span>
      </div>
    </header>

    <div v-if="!ready" class="setup-screen">
      <div class="setup-card">
        <div class="setup-symbol">▤</div>
        <p class="eyebrow">LOCAL ARCHIVE</p>
        <h1>初始化本地论文数据仓库</h1>
        <p>{{ status?.error || "默认位置 D:\\PaperArchiveData。请选择本地目录，所有原始数据不会上传云端。" }}</p>
        <div class="default-dir">{{ status?.data_dir || "D:\\PaperArchiveData" }}</div>
        <button class="primary-button" :disabled="busy || startupLoading" @click="chooseArchive">
          选择本地数据目录
        </button>
        <p v-if="errorMessage" class="error-msg">{{ errorMessage }}</p>
      </div>
    </div>

    <main v-else class="three-columns">
      <aside class="papers-pane">
        <div class="pane-header">
          <div><p class="eyebrow">LIBRARY</p><h2>论文条目</h2></div>
          <button class="icon-button" title="新建论文" @click="showNewPaper = !showNewPaper">＋</button>
        </div>
        <div v-if="showNewPaper" class="inline-form">
          <label>论文标题<input v-model="newTitle" placeholder="请输入论文题目" @keyup.enter="createPaper" /></label>
          <label>DOI（可选）<input v-model="newDoi" placeholder="10.xxxx/..." /></label>
          <div class="form-actions">
            <button class="secondary-button" @click="showNewPaper = false">取消</button>
            <button class="primary-button" :disabled="!newTitle.trim() || busy" @click="createPaper">保存论文</button>
          </div>
        </div>
        <input v-model="paperSearch" class="search-input" placeholder="检索论文题目或 DOI" aria-label="检索论文" />
        <div class="paper-list">
          <button
            v-for="paper in filteredPapers"
            :key="paper.paper_id"
            class="paper-entry"
            :class="{ active: selectedPaperId === paper.paper_id }"
            @click="selectPaper(paper.paper_id)"
          >
            <span class="entry-doc">▤</span>
            <span class="entry-content">
              <strong>{{ paper.title }}</strong>
              <span class="entry-sub">{{ paper.doi || "未填写 DOI" }}</span>
              <span class="entry-count">{{ paper.item_count }} 个图表 · {{ paper.resource_count }} 条原始引用</span>
            </span>
          </button>
          <div v-if="!filteredPapers.length" class="small-empty">
            {{ papers.length ? "没有找到匹配的论文" : "尚无论文，点击上方 ＋ 创建" }}
          </div>
        </div>
        <div class="library-footer">
          <div class="library-stat"><span>论文条目</span><strong>{{ papers.length }}</strong></div>
          <div class="library-stat"><span>数据记录（含历史导入）</span><strong>{{ allData.length }}</strong></div>
          <p class="library-path" :title="status?.data_dir ?? ''">{{ status?.data_dir }}</p>
        </div>
      </aside>

      <section class="paper-pane">
        <div v-if="currentPaper" class="paper-topline">
          <div class="paper-meta">
            <p class="eyebrow">SELECTED PAPER</p>
            <h2 :title="currentPaper.title">{{ currentPaper.title }}</h2>
            <small>{{ currentPaper.doi || "DOI 未填写" }}</small>
          </div>
          <button class="primary-button" :disabled="busy" @click="importManuscript">
            {{ currentPaper.manuscript_sha256 ? "替换正文文件" : "导入论文 PDF / DOCX" }}
          </button>
        </div>
        <div v-else class="paper-topline">
          <div class="paper-meta"><p class="eyebrow">READER</p><h2>文章预览</h2><small>请选择一篇论文</small></div>
        </div>

        <div v-if="errorMessage || successMessage" class="feedback" :class="{ error: !!errorMessage }" role="status">
          {{ errorMessage || successMessage }}
          <button @click="clearFeedback" title="关闭提示">×</button>
        </div>

        <div class="reader-host">
          <ArticleViewer
            :paper="currentPaper"
            :items="items"
            :binding-item-id="bindingItemId"
            @select-item="selectItem"
            @bind-anchor="bindAnchor"
          />
        </div>

        <div class="element-strip">
          <span class="strip-label">文章要素</span>
          <button
            v-for="item in orderedItems"
            :key="item.item_id"
            class="element-chip"
            :class="{ selected: selectedItemId === item.item_id }"
            @click="selectItem(item.item_id)"
          >
            {{ item.label }}
            <span v-if="item.anchor_kind" class="bound-indicator" title="已关联到文章位置">●</span>
          </button>
          <span v-if="!items.length" class="strip-empty">右侧可建立 Figure、Panel、Table</span>
        </div>
      </section>

      <aside class="resources-pane">
        <div class="resource-header">
          <div><p class="eyebrow">PAPER ASSETS</p><h2>论文资源库</h2></div>
          <button class="icon-button" title="创建 Figure / Panel / Table" :disabled="!currentPaper" @click="showNewItem = !showNewItem">＋</button>
        </div>
        <p class="resource-intro">每篇论文独立归档图表与原始数据；点击正文已绑定的图像即可跳转到对应条目。</p>
        <div v-if="showNewItem && currentPaper" class="inline-form item-form">
          <label>图表类型
            <select v-model="newItemType">
              <option value="figure">Figure（组合图）</option>
              <option value="panel" :disabled="!figureItems.length">Panel（子图）</option>
              <option value="table">Table（统计表）</option>
            </select>
          </label>
          <label>编号或名称<input v-model="newItemLabel" placeholder="例如 Figure 1A" @keyup.enter="createItem" /></label>
          <label v-if="newItemType === 'panel'">所属 Figure
            <select v-model="newItemParent">
              <option value="">请选择 Figure</option>
              <option v-for="figure in figureItems" :key="figure.item_id" :value="figure.item_id">{{ figure.label }}</option>
            </select>
          </label>
          <div class="form-actions">
            <button class="secondary-button" @click="showNewItem = false">取消</button>
            <button class="primary-button" :disabled="!newItemLabel.trim() || busy" @click="createItem">保存</button>
          </div>
        </div>

        <div v-if="!currentPaper" class="small-empty">请先从左侧选择论文。</div>
        <template v-else>
          <input v-model="resourceSearch" class="search-input resources-search" placeholder="筛选文件名称" aria-label="筛选原始资源" />
          <div class="resource-tree">
            <div v-if="paperLoading" class="small-empty">正在加载资源目录…</div>
            <div v-else-if="!orderedItems.length" class="resource-empty">
              <strong>尚未建立图表目录</strong>
              <p>先点击右上角 ＋，创建 Figure、Panel 或 Table，再将原始文件关联到条目。</p>
            </div>
            <div
              v-for="item in orderedItems"
              :id="'resource-group-' + item.item_id"
              :key="item.item_id"
              class="resource-group"
              :class="{ selected: selectedItemId === item.item_id, panel: item.item_type === 'panel' }"
            >
              <button class="group-heading" @click="selectItem(item.item_id)">
                <span class="group-icon">{{ item.item_type === "table" ? "▦" : item.item_type === "panel" ? "◇" : "▣" }}</span>
                <span class="group-text"><strong>{{ item.label }}</strong><small>
                  {{ item.item_type.toUpperCase() }} · {{ resources.filter(r => r.item_id === item.item_id).length }} 个文件
                </small></span>
                <span v-if="item.anchor_kind" class="linked-dot" title="已经与论文原文绑定">●</span>
              </button>
              <div v-if="selectedItemId === item.item_id" class="group-controls">
                <button class="small-button" :disabled="busy" @click="importItemFiles">＋ 导入数据</button>
                <button class="small-button" :class="{ active: bindingItemId === item.item_id }" @click="toggleBinding(item.item_id)">
                  {{ bindingItemId === item.item_id ? "取消框选" : item.anchor_kind ? "重新绑定原文" : "绑定文章位置" }}
                </button>
                <div class="attach-existing">
                  <select v-model="existingDataId" aria-label="选择已导入的数据">
                    <option value="">关联已导入的数据…</option>
                    <option v-for="data in availableData" :key="data.data_id" :value="data.data_id">
                      {{ data.original_filename }} · {{ briefId(data.display_id) }}
                    </option>
                  </select>
                  <button :disabled="!existingDataId || busy" @click="linkExistingData">关联</button>
                </div>
              </div>
              <div v-for="resource in filteredResources(item.item_id)" :key="resource.reference_id" class="file-row-wrap">
                <button
                  class="resource-file"
                  :class="{ active: selectedDataId === resource.data_id }"
                  :title="resource.display_id"
                  @click="selectItem(item.item_id).then(() => selectData(resource.data_id))"
                >
                  <span class="file-icon">{{ resource.category === "image" ? "▧" : resource.category === "spreadsheet" ? "▤" : "◫" }}</span>
                  <span class="file-copy"><strong>{{ resource.original_filename }}</strong><small>{{ formatBytes(resource.file_size) }} · {{ briefId(resource.display_id) }}</small></span>
                </button>
              </div>
              <p v-if="selectedItemId === item.item_id && !resources.some(r => r.item_id === item.item_id)" class="no-files">
                尚无原始文件；可导入新文件或关联旧版已导入的数据。
              </p>
            </div>
          </div>

          <div class="file-details">
            <div class="details-title">
              <p class="eyebrow">RESOURCE INSPECTOR</p>
              <h3>文件信息</h3>
            </div>
            <template v-if="dataDetails">
              <img v-if="previewUrl" class="resource-image" :src="previewUrl" alt="归档图片只读预览" />
              <div class="detail-line"><span>原始文件名</span><strong>{{ dataDetails.record.original_filename }}</strong></div>
              <div class="detail-line"><span>数据编号</span><code :title="dataDetails.record.data_id">{{ dataDetails.record.display_id }}</code></div>
              <div class="detail-line"><span>文件大小</span><strong>{{ formatBytes(dataDetails.record.file_size) }}</strong></div>
              <div class="detail-line"><span>文件分类</span><strong>{{ dataDetails.record.category }}</strong></div>
              <div class="detail-line"><span>SHA-256</span><code class="hash-text">{{ dataDetails.record.sha256 }}</code></div>
              <div class="detail-line"><span>归档时间</span><strong>{{ new Date(dataDetails.record.created_at).toLocaleString("zh-CN") }}</strong></div>
              <div class="detail-line"><span>仓库路径</span><code class="hash-text">{{ dataDetails.relative_path }}</code></div>
              <button class="primary-button full-width" @click="revealFile">在 Windows 资源管理器中定位文件</button>
              <div class="usage-list">
                <strong>被引用位置（{{ dataDetails.usages.length }}）</strong>
                <button v-for="usage in dataDetails.usages" :key="usage.item_id" @click="jumpToUsage(usage.paper_id, usage.item_id)">
                  {{ usage.paper_title }} / {{ usage.item_label }}
                </button>
              </div>
            </template>
            <p v-else class="no-details">点击右侧原始文件，可查看完整元数据和引用位置。</p>
          </div>
        </template>
      </aside>
    </main>
    <footer v-if="!ready" class="footer">本地 SQLite · 文件 SHA-256 校验 · 不依赖互联网</footer>
  </div>
</template>

<style>
:root {
  font-family: "Segoe UI", "Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif;
  color: #243747; background: #f5f7fa; font-synthesis: none;
  text-rendering: optimizeLegibility; -webkit-font-smoothing: antialiased;
}
* { box-sizing: border-box; }
body { margin: 0; min-width: 1010px; }
button, input, select { font: inherit; }
button { cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .5; }
.app-frame { height: 100vh; display: flex; flex-direction: column; overflow: hidden; }
.topbar { height: 63px; min-height: 63px; display: flex; justify-content: space-between; align-items: center; background: #ffffff; padding: 0 22px; border-bottom: 1px solid #dfe7ee; }
.brand { display: flex; align-items: center; gap: 10px; color: #203a55; font-weight: 730; font-size: 15px; }
.brand-mark { display: grid; place-items: center; width: 35px; height: 35px; border-radius: 9px; background: #1d4d72; color: white; font-size: 13px; font-weight: 800; }
.top-actions { display: flex; align-items: center; gap: 19px; color: #71869b; font-size: 11px; }
.local-status { display: inline-flex; align-items: center; gap: 7px; }
.status-dot { height: 7px; width: 7px; background: #49ae89; border-radius: 50%; }
.version { color: #9dadba; }
.text-button { padding: 5px; border: none; background: transparent; color: #2c7095; font-weight: 640; }
.three-columns { flex: 1; min-height: 0; display: grid; grid-template-columns: 268px minmax(330px, 1fr) 390px; }
.papers-pane { display: flex; min-height: 0; flex-direction: column; overflow: hidden; border-right: 1px solid #dfe6ee; background: #fff; }
.pane-header, .resource-header { display: flex; justify-content: space-between; align-items: center; padding: 21px 19px 15px; }
.eyebrow { margin: 0 0 5px; color: #97a7b5; font-size: 10px; font-weight: 750; letter-spacing: .12em; }
h2 { margin: 0; font-size: 17px; font-weight: 720; }
.icon-button { width: 30px; height: 30px; border: 1px solid #d8e5ee; border-radius: 8px; color: #32769e; background: #f7fafc; font-weight: 700; font-size: 19px; }
.search-input { display: block; width: calc(100% - 36px); margin: 0 18px 12px; padding: 10px 12px; outline: none; border: 1px solid #dce6ef; border-radius: 8px; background: #f9fbfd; color: #34495b; font-size: 11px; }
.search-input:focus, input:focus, select:focus { border-color: #71abd0; }
.paper-list { flex: 1; min-height: 0; overflow-y: auto; padding: 3px 9px; }
.paper-entry { display: flex; align-items: flex-start; gap: 9px; width: 100%; padding: 13px 10px; margin-bottom: 3px; border: 1px solid transparent; border-radius: 9px; text-align: left; background: white; }
.paper-entry:hover { background: #f4f8fb; }
.paper-entry.active { border-color: #cbe1f3; background: #eaf4fc; }
.entry-doc { color: #5382a7; font-size: 19px; }
.entry-content { display: flex; flex-direction: column; min-width: 0; gap: 5px; }
.entry-content strong { color: #274057; font-size: 12px; font-weight: 700; line-height: 1.6; overflow-wrap: anywhere; }
.entry-sub { color: #8194a5; font-size: 10px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.entry-count { color: #66849d; font-size: 10px; }
.small-empty { padding: 24px 16px; font-size: 12px; color: #8595a5; line-height: 1.8; }
.library-footer { border-top: 1px solid #e8eef3; padding: 17px 20px; }
.library-stat { display: flex; align-items: center; justify-content: space-between; margin-bottom: 9px; color: #7c8e9e; font-size: 11px; }
.library-stat strong { color: #31566f; font-size: 13px; }
.library-path { margin: 17px 0 0; color: #a1adbb; font-size: 10px; word-break: break-all; }
.paper-pane { display: flex; flex-direction: column; min-width: 0; min-height: 0; }
.paper-topline { display: flex; align-items: center; justify-content: space-between; min-height: 95px; gap: 14px; padding: 17px 24px; background: #fff; border-bottom: 1px solid #dfe7ef; }
.paper-meta { min-width: 0; }
.paper-meta h2 { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 18px; }
.paper-meta small { display: block; margin-top: 7px; color: #8e9eae; font-size: 11px; }
.primary-button { padding: 10px 15px; border: 1px solid #1b537c; border-radius: 7px; background: #1c537a; color: white; font-size: 11px; font-weight: 670; white-space: nowrap; }
.primary-button:hover:not(:disabled) { background: #144365; }
.secondary-button { padding: 9px 12px; border: 1px solid #cbdce9; border-radius: 7px; background: white; color: #396381; font-size: 11px; }
.reader-host { flex: 1; min-height: 0; overflow: hidden; }
.element-strip { display: flex; flex-wrap: nowrap; align-items: center; gap: 7px; min-height: 57px; padding: 9px 17px; overflow-x: auto; border-top: 1px solid #e0e7ed; background: #fff; }
.strip-label { margin-right: 8px; font-size: 11px; color: #7d90a1; white-space: nowrap; }
.element-chip { padding: 6px 9px; border: 1px solid #dce6ef; border-radius: 5px; background: #f8fafc; color: #577187; font-size: 11px; white-space: nowrap; }
.element-chip.selected { background: #dcefff; border-color: #96c8ea; color: #225e8a; }
.bound-indicator, .linked-dot { margin-left: 5px; color: #41a890; font-size: 10px; }
.strip-empty { font-size: 10px; color: #a1adb9; }
.feedback { display: flex; justify-content: space-between; padding: 9px 15px; white-space: pre-wrap; background: #eaf8f1; color: #347a5d; font-size: 11px; border-bottom: 1px solid #c6e4d3; }
.feedback.error { color: #a04339; background: #fff1ee; border-color: #efcac5; }
.feedback button { border: none; background: transparent; font-size: 14px; color: inherit; }
.resources-pane { display: flex; flex-direction: column; min-height: 0; background: #fff; border-left: 1px solid #dfe6ee; }
.resource-header { flex: 0 0 auto; padding-bottom: 8px; }
.resource-intro { margin: 0 19px 17px; color: #8798aa; font-size: 11px; line-height: 1.8; }
.resources-search { margin-bottom: 7px; }
.resource-tree { flex: 1; min-height: 100px; overflow-y: auto; padding: 10px 13px 25px; }
.resource-empty { padding: 27px 19px; color: #667f93; font-size: 12px; line-height: 1.8; }
.resource-empty p { color: #91a0af; font-size: 11px; }
.resource-group { padding: 5px 5px 8px; margin-bottom: 9px; border-radius: 8px; border: 1px solid transparent; }
.resource-group.selected { border-color: #bad9ee; background: #f1f8fe; }
.resource-group.panel { margin-left: 15px; }
.group-heading { display: flex; align-items: center; width: 100%; gap: 11px; padding: 9px 7px; border: none; text-align: left; background: transparent; }
.group-icon { display: grid; place-items: center; width: 29px; height: 29px; flex: 0 0 29px; border-radius: 7px; background: #eaf2f8; color: #3d7da4; font-size: 15px; }
.group-text { display: flex; flex: 1; flex-direction: column; gap: 4px; }
.group-text strong { font-size: 12px; color: #38586f; }
.group-text small { font-size: 9px; color: #98a6b3; }
.group-controls { display: flex; flex-wrap: wrap; gap: 7px; margin: 3px 7px 11px; }
.small-button { padding: 7px 9px; border: 1px solid #cee0ec; border-radius: 6px; background: #fff; color: #336d94; font-size: 10px; font-weight: 600; }
.small-button.active { border-color: #ddab48; background: #fff2d9; color: #8e6320; }
.attach-existing { display: flex; gap: 4px; width: 100%; }
.attach-existing select { flex: 1; min-width: 0; padding: 7px 4px; border: 1px solid #d9e3ec; border-radius: 5px; background: white; font-size: 10px; }
.attach-existing button { padding: 6px 8px; border: 1px solid #b7d1e2; border-radius: 5px; background: #e9f4fc; color: #316b91; font-size: 10px; }
.resource-file { display: flex; align-items: center; gap: 10px; width: calc(100% - 10px); margin: 4px 5px; padding: 9px; border: 1px solid #e5edf3; border-radius: 7px; text-align: left; background: #fff; }
.resource-file:hover, .resource-file.active { border-color: #90bee2; background: #eaf4fc; }
.file-icon { color: #608eac; font-size: 18px; }
.file-copy { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.file-copy strong { font-size: 11px; color: #486276; font-weight: 610; overflow-wrap: anywhere; }
.file-copy small { font-size: 9px; color: #a3b0bc; }
.no-files { margin: 8px 13px; color: #9baab7; font-size: 10px; line-height: 1.8; }
.file-details { padding: 17px 20px 22px; border-top: 1px solid #e5ecf1; max-height: 40%; overflow-y: auto; flex: 0 1 auto; min-height: 138px; }
.details-title { margin-bottom: 12px; }
.details-title h3 { margin: 0; font-size: 13px; }
.detail-line { display: flex; flex-direction: column; gap: 5px; padding: 7px 0; border-bottom: 1px solid #f0f4f7; font-size: 11px; }
.detail-line span { color: #9ba8b5; }
.detail-line strong { font-weight: 580; color: #425b72; overflow-wrap: anywhere; }
.detail-line code { font-size: 10px; overflow-wrap: anywhere; color: #5b7388; }
.hash-text { word-break: break-all; }
.resource-image { display: block; max-width: 100%; max-height: 230px; margin: 0 auto 12px; object-fit: contain; background: #f0f2f5; }
.full-width { width: 100%; margin-top: 15px; }
.no-details { font-size: 11px; color: #a1afb9; line-height: 1.8; }
.usage-list { margin: 17px 0; }
.usage-list > strong { display: block; margin-bottom: 8px; font-size: 11px; color: #587188; }
.usage-list button { display: block; padding: 8px; width: 100%; border: none; text-align: left; background: #f4f8fc; color: #326e96; border-radius: 5px; margin-bottom: 6px; font-size: 10px; }
.inline-form { padding: 14px; margin: 4px 13px 13px; border: 1px solid #cbdce9; border-radius: 9px; background: #f7fafc; }
.inline-form label { display: block; color: #5d7489; margin-bottom: 10px; font-size: 11px; }
.inline-form input, .inline-form select { display: block; width: 100%; margin-top: 6px; padding: 8px 9px; border: 1px solid #d8e4ed; border-radius: 6px; background: #fff; font-size: 11px; }
.form-actions { display: flex; justify-content: flex-end; gap: 7px; }
.setup-screen { flex: 1; display: grid; place-items: center; background: #f3f7fa; }
.setup-card { width: min(560px, 90%); padding: 45px; border: 1px solid #e1e8ef; border-radius: 16px; background: #fff; box-shadow: 0 18px 54px #20364a10; }
.setup-symbol { display: grid; place-items: center; width: 54px; height: 54px; margin-bottom: 24px; border-radius: 14px; background: #e4f0f8; color: #447b9d; font-size: 27px; }
.setup-card h1 { margin: 0; font-size: 23px; }
.setup-card p { color: #8595a5; font-size: 12px; line-height: 1.8; }
.default-dir { margin: 20px 0; padding: 13px; border-radius: 8px; background: #f3f6f9; color: #466782; font-size: 12px; }
.error-msg { color: #aa493c !important; }
.footer { text-align: center; padding: 18px; font-size: 11px; color: #899aa9; }
@media (max-width: 1250px) {
  .three-columns { grid-template-columns: 226px minmax(320px, 1fr) 348px; }
  .topbar { padding: 0 16px; }
  .paper-topline { padding: 17px 14px; }
}
</style>
