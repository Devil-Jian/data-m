<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";

interface ArchiveStatus {
  initialized: boolean;
  data_dir: string | null;
  archive_id: string | null;
  schema_version: number | null;
  error: string | null;
}

interface DataRecord {
  data_id: string;
  display_id: string;
  sha256: string;
  original_filename: string;
  file_extension: string;
  category: string;
  file_size: number;
  created_at: string;
}

const status = ref<ArchiveStatus | null>(null);
const records = ref<DataRecord[]>([]);
const loading = ref(true);
const choosing = ref(false);
const importing = ref(false);
const importProgress = ref("");
const errorMessage = ref("");
const notice = ref("");

const isReady = computed(() => status.value?.initialized === true);
const totalBytes = computed(() => records.value.reduce((sum, row) => sum + row.file_size, 0));

function explainError(err: unknown): string {
  return err instanceof Error ? err.message : String(err);
}

function formatBytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  const exponent = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 4);
  return `${(value / 1024 ** exponent).toFixed(exponent === 1 ? 0 : 2)} ${units[exponent - 1]}`;
}

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString("zh-CN");
}

function categoryName(category: string): string {
  const categories: Record<string, string> = {
    image: "图像", spreadsheet: "表格", document: "文档", other: "其他",
  };
  return categories[category] ?? "其他";
}

async function refreshRecords(): Promise<void> {
  if (!isReady.value) {
    records.value = [];
    return;
  }
  records.value = await invoke<DataRecord[]>("list_data_records");
}

async function refreshStatus(): Promise<void> {
  loading.value = true;
  errorMessage.value = "";
  try {
    status.value = await invoke<ArchiveStatus>("get_archive_status");
    await refreshRecords();
  } catch (error) {
    errorMessage.value = `无法读取归档库状态：${explainError(error)}`;
  } finally {
    loading.value = false;
  }
}

async function chooseDirectory(): Promise<void> {
  if (choosing.value || importing.value) return;
  errorMessage.value = "";
  notice.value = "";

  try {
    const selected = await open({ directory: true, multiple: false, title: "选择本地数据仓库目录" });
    if (typeof selected !== "string") return;
    choosing.value = true;
    const initialized = await invoke<ArchiveStatus>("initialize_archive", { path: selected });
    status.value = initialized;
    await refreshRecords();
    notice.value = `数据仓库已就绪：${initialized.data_dir ?? selected}`;
  } catch (error) {
    errorMessage.value = `无法初始化数据仓库：${explainError(error)}`;
  } finally {
    choosing.value = false;
  }
}

async function importFiles(): Promise<void> {
  if (!isReady.value || importing.value || choosing.value) return;
  errorMessage.value = "";
  notice.value = "";
  try {
    const selected = await open({ multiple: true, directory: false, title: "选择需要归档的原始文件" });
    if (!selected) return;
    const paths = typeof selected === "string" ? [selected] : selected;
    if (paths.length === 0) return;

    importing.value = true;
    const failures: string[] = [];
    let imported = 0;
    for (let index = 0; index < paths.length; index += 1) {
      const path = paths[index];
      importProgress.value = `正在导入 ${index + 1} / ${paths.length}：${path.split(/[\\/]/).pop() ?? path}`;
      try {
        await invoke<DataRecord>("import_data_file", { path });
        imported += 1;
      } catch (error) {
        failures.push(`${path}: ${explainError(error)}`);
      }
    }

    await refreshRecords();
    if (imported > 0) notice.value = `成功归档 ${imported} 个文件，每条记录已获得独立 Data ID。`;
    if (failures.length > 0) {
      errorMessage.value = `${failures.length} 个文件导入失败：\n${failures.join("\n")}`;
    }
  } catch (error) {
    errorMessage.value = `无法选择或导入文件：${explainError(error)}`;
  } finally {
    importing.value = false;
    importProgress.value = "";
  }
}

onMounted(refreshStatus);
</script>

<template>
  <main class="app-shell">
    <header class="topbar">
      <div class="brand-mark" aria-hidden="true">RD</div>
      <div class="brand-copy">
        <p class="eyebrow">LOCAL RESEARCH ARCHIVE</p>
        <h1>论文原始数据归档平台</h1>
      </div>
      <div class="topbar-right">
        <span class="offline-indicator">本地离线模式</span>
        <span class="version">v0.1.0</span>
      </div>
    </header>

    <div class="content">
      <section class="overview">
        <div>
          <p class="section-kicker">ARCHIVE OVERVIEW</p>
          <h2>科研数据资源库</h2>
          <p class="subtitle">统一保存、核验与追踪论文原始数据，原始文件始终保留在本地磁盘。</p>
        </div>
        <button class="primary-button" :disabled="!isReady || importing || choosing" @click="importFiles">
          {{ importing ? "正在归档…" : "＋ 导入原始文件" }}
        </button>
      </section>

      <div v-if="errorMessage" class="alert alert-error" role="alert">{{ errorMessage }}</div>
      <div v-if="notice" class="alert alert-success" role="status">{{ notice }}</div>

      <section class="archive-panel">
        <div class="archive-heading">
          <div>
            <p class="section-kicker">STORAGE</p>
            <h3>本地仓库状态</h3>
          </div>
          <span class="status-chip" :class="isReady ? 'ready' : 'pending'">
            <span class="dot"></span>
            {{ loading ? "检查中" : isReady ? "已初始化" : "未初始化" }}
          </span>
        </div>

        <template v-if="loading">
          <p class="muted">正在读取本地数据仓库信息…</p>
        </template>
        <template v-else>
          <div class="storage-path">
            <div class="storage-icon" aria-hidden="true">▣</div>
            <div class="path-copy">
              <span class="field-label">数据存储目录</span>
              <strong>{{ status?.data_dir || "尚未选择" }}</strong>
            </div>
            <button class="secondary-button" :disabled="choosing || importing" @click="chooseDirectory">
              {{ choosing ? "正在初始化…" : isReady ? "选择其他目录" : "选择本地目录" }}
            </button>
          </div>
          <div v-if="!isReady" class="setup-notice">
            {{ status?.error || "请先选择本地目录以建立归档库。" }}
            <span>不会自动切换至 C 盘，也不会上传任何文件。</span>
          </div>
          <div v-else class="archive-details">
            <span><span class="field-label">Archive ID</span><code>{{ status?.archive_id }}</code></span>
            <span><span class="field-label">Schema</span>v{{ status?.schema_version }}</span>
            <span><span class="field-label">数据完整性</span>SHA-256 内容校验</span>
          </div>
        </template>
      </section>

      <section class="stats-grid">
        <div class="stat-card">
          <span class="field-label">已归档 Data Record</span>
          <strong>{{ records.length }}</strong>
          <span>每次导入保留独立数据身份</span>
        </div>
        <div class="stat-card">
          <span class="field-label">归档数据逻辑大小</span>
          <strong>{{ formatBytes(totalBytes) }}</strong>
          <span>相同内容在磁盘中只保存一个 Blob</span>
        </div>
        <div class="stat-card">
          <span class="field-label">工作状态</span>
          <strong class="status-value">{{ importing ? "正在导入" : isReady ? "可用" : "待设置" }}</strong>
          <span>{{ importing ? importProgress : "无需互联网连接" }}</span>
        </div>
      </section>

      <section class="records-panel">
        <div class="records-heading">
          <div>
            <p class="section-kicker">DATA RESOURCE LIBRARY</p>
            <h3>原始数据列表 <span class="count">({{ records.length }})</span></h3>
          </div>
          <button class="text-button" :disabled="!isReady || importing" @click="refreshRecords">刷新列表</button>
        </div>

        <div v-if="!isReady" class="empty-state">
          <div class="empty-icon">▤</div>
          <strong>尚未建立数据仓库</strong>
          <p>选择本地目录并完成初始化后，即可归档科研文件。</p>
        </div>
        <div v-else-if="records.length === 0" class="empty-state">
          <div class="empty-icon">▤</div>
          <strong>暂无归档数据</strong>
          <p>选择“导入原始文件”，文件将通过 SHA-256 校验后保存。</p>
          <button class="secondary-button" :disabled="importing" @click="importFiles">开始导入</button>
        </div>
        <div v-else class="table-scroll">
          <table>
            <thead>
              <tr>
                <th>文件名 / Data ID</th>
                <th>类型</th>
                <th>大小</th>
                <th>SHA-256</th>
                <th>归档时间</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="record in records" :key="record.data_id">
                <td class="name-cell">
                  <strong :title="record.original_filename">{{ record.original_filename }}</strong>
                  <code :title="record.data_id">{{ record.display_id }}</code>
                </td>
                <td><span class="category">{{ categoryName(record.category) }}</span></td>
                <td class="nowrap">{{ formatBytes(record.file_size) }}</td>
                <td><code :title="record.sha256">{{ record.sha256.slice(0, 16) }}…</code></td>
                <td class="nowrap">{{ formatDate(record.created_at) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </div>

    <footer>Windows 本地单机应用 · SQLite 元数据 · SHA-256 不可变原始文件</footer>
  </main>
</template>

<style>
:root {
  font-family: Inter, "Microsoft YaHei UI", "PingFang SC", system-ui, sans-serif;
  color: #1b2b3a;
  background: #f6f8fb;
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
}
* { box-sizing: border-box; }
body { min-width: 720px; margin: 0; }
button { font: inherit; cursor: pointer; }
button:disabled { cursor: not-allowed; opacity: .55; }
.app-shell { display: flex; min-height: 100vh; flex-direction: column; }
.topbar { display: flex; align-items: center; gap: 13px; padding: 19px clamp(30px, 5vw, 76px); background: #fff; border-bottom: 1px solid #e7ecf2; }
.brand-mark { display: grid; place-items: center; width: 42px; height: 42px; border-radius: 11px; background: #194469; color: white; font-size: 15px; font-weight: 750; }
.eyebrow, .section-kicker { margin: 0 0 6px; color: #7d8b9e; font-size: 10px; font-weight: 750; letter-spacing: .13em; }
h1 { margin: 0; font-size: 16px; font-weight: 700; }
.topbar-right { display: flex; align-items: center; gap: 22px; margin-left: auto; color: #7c8b9b; font-size: 12px; }
.offline-indicator::before { content: ""; display: inline-block; width: 7px; height: 7px; margin-right: 8px; border-radius: 50%; background: #47a58b; }
.content { width: min(100%, 1480px); margin: 0 auto; padding: 36px clamp(26px, 5vw, 76px) 55px; }
.overview { display: flex; justify-content: space-between; gap: 24px; align-items: center; margin-bottom: 28px; }
h2 { margin: 0; font-size: 29px; letter-spacing: -.03em; }
.subtitle { margin: 10px 0 0; color: #708092; font-size: 13px; line-height: 1.8; }
.primary-button { padding: 13px 21px; border: 1px solid #1d4b74; border-radius: 9px; background: #194469; color: #fff; font-weight: 650; font-size: 13px; white-space: nowrap; }
.primary-button:hover:not(:disabled) { background: #143955; }
.alert { margin-bottom: 18px; padding: 14px 18px; border-radius: 9px; font-size: 12px; line-height: 1.7; white-space: pre-wrap; overflow-wrap: anywhere; }
.alert-error { color: #9a3429; background: #fff1ef; border: 1px solid #f2cbc5; }
.alert-success { color: #286c57; background: #effaf5; border: 1px solid #c8e9d9; }
.archive-panel, .records-panel, .stat-card { border: 1px solid #e4eaf1; border-radius: 13px; background: #fff; box-shadow: 0 3px 14px rgba(40,60,80,.02); }
.archive-panel { padding: 24px 27px 21px; }
.archive-heading, .records-heading { display: flex; justify-content: space-between; align-items: center; gap: 16px; }
h3 { margin: 0; font-size: 16px; font-weight: 700; }
.status-chip { display: inline-flex; align-items: center; gap: 8px; padding: 7px 11px; border-radius: 22px; font-size: 11px; font-weight: 650; }
.status-chip.ready { color: #24785d; background: #eaf8f1; }
.status-chip.pending { color: #926b1d; background: #fff5dd; }
.dot { height: 7px; width: 7px; border-radius: 50%; background: currentColor; }
.storage-path { display: flex; align-items: center; gap: 17px; margin-top: 23px; padding: 19px 20px; border: 1px solid #e9eef3; border-radius: 10px; background: #f9fbfd; }
.storage-icon { display: grid; place-items: center; width: 43px; height: 43px; flex: 0 0 43px; border-radius: 10px; color: #2d658f; background: #e9f1f9; font-size: 24px; }
.path-copy { display: flex; flex: 1; flex-direction: column; gap: 8px; min-width: 0; }
.path-copy strong { overflow-wrap: anywhere; font-weight: 600; font-size: 14px; }
.field-label { display: block; color: #8a98a9; font-size: 11px; font-weight: 600; }
.secondary-button { padding: 10px 15px; border: 1px solid #ccd9e6; border-radius: 8px; color: #235578; background: #fff; font-size: 12px; font-weight: 650; white-space: nowrap; }
.secondary-button:hover:not(:disabled) { border-color: #83abc8; background: #f5f9fc; }
.setup-notice { display: flex; flex-direction: column; gap: 6px; margin: 16px 2px 0; color: #956822; font-size: 12px; line-height: 1.7; }
.setup-notice span { color: #8390a0; }
.archive-details { display: flex; gap: 40px; align-items: center; padding-top: 18px; font-size: 12px; color: #596c7e; }
.archive-details > span { display: flex; align-items: center; gap: 10px; }
code { font-family: Consolas, "SFMono-Regular", monospace; font-size: 11px; }
.archive-details code { overflow-wrap: anywhere; }
.muted { color: #8391a1; font-size: 13px; }
.stats-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; margin: 19px 0 30px; }
.stat-card { display: flex; flex-direction: column; gap: 9px; min-height: 135px; padding: 22px 24px; }
.stat-card strong { font-size: 27px; line-height: 1.25; font-weight: 720; }
.stat-card strong.status-value { font-size: 23px; }
.stat-card > span:last-child { color: #8492a3; font-size: 11px; line-height: 1.7; overflow-wrap: anywhere; }
.records-panel { min-height: 300px; overflow: hidden; }
.records-heading { padding: 25px 27px 23px; }
.count { color: #8e9aab; font-weight: 500; }
.text-button { border: 0; background: transparent; color: #275c84; font-size: 12px; font-weight: 600; }
.empty-state { display: flex; align-items: center; justify-content: center; flex-direction: column; min-height: 245px; padding: 38px; border-top: 1px solid #f0f2f5; }
.empty-icon { display: grid; place-items: center; width: 49px; height: 49px; margin-bottom: 14px; border-radius: 14px; background: #f0f5fa; color: #6c94b4; font-size: 27px; }
.empty-state strong { font-size: 14px; }
.empty-state p { margin: 9px 0 17px; color: #8c99a9; font-size: 12px; }
.table-scroll { overflow-x: auto; }
table { width: 100%; border-collapse: collapse; text-align: left; }
th { padding: 14px 19px; background: #f8fafc; border-top: 1px solid #eef1f5; border-bottom: 1px solid #eef1f5; color: #8290a0; font-size: 11px; font-weight: 650; white-space: nowrap; }
td { padding: 17px 19px; border-bottom: 1px solid #eff2f5; color: #596b7c; font-size: 12px; }
tr:last-child td { border-bottom: 0; }
.name-cell { display: flex; flex-direction: column; gap: 8px; min-width: 260px; max-width: 430px; }
.name-cell strong { color: #263f54; font-size: 12px; font-weight: 650; overflow-wrap: anywhere; }
.name-cell code { color: #8999aa; overflow-wrap: anywhere; }
.category { display: inline-block; padding: 5px 9px; border-radius: 5px; background: #edf3f8; color: #3e6788; }
.nowrap { white-space: nowrap; }
footer { margin-top: auto; padding: 22px; color: #99a5b4; text-align: center; font-size: 11px; }
@media (max-width: 960px) {
  .content { padding: 28px 26px 40px; }
  .archive-details { flex-wrap: wrap; gap: 13px 25px; }
  .stat-card { padding: 18px; }
}
</style>
