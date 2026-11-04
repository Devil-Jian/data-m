<script setup lang="ts">
import { onMounted, ref } from "vue";
import { invoke } from "@tauri-apps/api/core";
import { open } from "@tauri-apps/plugin-dialog";
import ProcessingWorkspace from "./ProcessingWorkspace.vue";
import ArchiveBrowser from "./ArchiveBrowser.vue";
import type { ArchiveStatus } from "./types";

const tab=ref<"processing"|"library">("processing");
const status=ref<ArchiveStatus|null>(null);
const busy=ref(false);
const error=ref("");
const activeKey=ref(0);
const libraryKey=ref(0);

async function statusCheck(){
  try{status.value=await invoke<ArchiveStatus>("get_archive_status");}
  catch(cause){error.value=String(cause);}
}
async function initializeArchive(){
  if(busy.value)return;
  try{
    const dir=await open({directory:true,multiple:false,title:"选择论文归档数据仓库（默认 D 盘）"});
    if(typeof dir!=="string")return;
    busy.value=true;error.value="";
    status.value=await invoke<ArchiveStatus>("initialize_archive",{path:dir});
    activeKey.value++;libraryKey.value++;
  }catch(cause){error.value=String(cause);}finally{busy.value=false;}
}
function completed(){
  libraryKey.value++;
  tab.value="library";
}
function revise(){
  activeKey.value++;
  tab.value="processing";
}
onMounted(()=>{void statusCheck();});
</script>

<template>
  <div class="app">
    <header class="app-header">
      <div class="brand"><span class="logo">RD</span><strong>论文原始数据归档平台</strong></div>
      <nav class="main-nav" aria-label="主功能">
        <button :class="{selected:tab==='processing'}" @click="tab='processing'">论文归档</button>
        <button :class="{selected:tab==='library'}" @click="tab='library'">论文资源库</button>
      </nav>
      <div class="top-actions">
        <span class="offline">本地单机 · 离线</span>
        <button class="path-button" @click="initializeArchive">数据仓库设置</button>
        <span class="version">V0.4</span>
      </div>
    </header>

    <main v-if="status?.initialized" class="app-content">
      <ProcessingWorkspace v-if="tab==='processing'" :key="activeKey" @archive-completed="completed" />
      <ArchiveBrowser v-else :key="libraryKey" @revise="revise" />
    </main>
    <main v-else class="setup">
      <section class="setup-box">
        <span class="setup-icon">▤</span>
        <p class="eyebrow">OFFLINE PAPER ARCHIVE</p>
        <h1>初始化论文归档数据仓库</h1>
        <p>默认使用 D:\PaperArchiveData。选择一次本地目录后，即可导入整篇论文文件夹、自动识别 Figure / Panel / Table，并归档其全部原始数据。</p>
        <div class="folder">{{ status?.data_dir||"D:\\PaperArchiveData" }}</div>
        <button :disabled="busy" @click="initializeArchive">选择本地仓库目录</button>
        <p v-if="status?.error" class="status-error">{{ status.error }}</p>
      </section>
    </main>
    <div v-if="error" class="app-error">{{ error }} <button @click="error=''">×</button></div>
  </div>
</template>

<style>
:root{
  font-family:"Segoe UI","Microsoft YaHei UI","PingFang SC",system-ui,sans-serif;
  color:#263e52;background:#f5f7fa;
  font-synthesis:none;text-rendering:optimizeLegibility;-webkit-font-smoothing:antialiased;
}
*{box-sizing:border-box}
body{margin:0;min-width:1060px}
button,input,select{font:inherit}
button:disabled{cursor:not-allowed;opacity:.55}
.app{height:100vh;min-height:0;display:flex;flex-direction:column;overflow:hidden}
.app-header{display:flex;align-items:center;justify-content:space-between;gap:15px;min-height:64px;padding:0 22px;border-bottom:1px solid #e0e7ee;background:white}
.brand{display:flex;align-items:center;gap:11px;white-space:nowrap}
.brand strong{font-size:14px}
.logo{display:grid;place-items:center;width:36px;height:36px;background:#1c5075;color:#fff;font-size:12px;font-weight:800;border-radius:8px}
.main-nav{display:flex;align-items:center;gap:8px;height:100%}
.main-nav button{height:60px;padding:0 20px;border:0;border-bottom:3px solid transparent;background:transparent;color:#6c8498;font-size:13px;font-weight:640;cursor:pointer}
.main-nav button.selected{color:#1d5c86;border-bottom-color:#1d6899;background:#f7fbfe}
.top-actions{display:flex;align-items:center;gap:15px;font-size:11px;color:#8b9baa;white-space:nowrap}
.path-button{padding:7px 9px;border:1px solid #d5e2ed;border-radius:7px;background:#fff;color:#3b7393;cursor:pointer}
.offline::before{display:inline-block;content:"";width:7px;height:7px;margin-right:7px;border-radius:50%;background:#4aa589}
.version{color:#9daab5}
.app-content{flex:1;min-height:0;overflow:hidden}
.setup{flex:1;display:grid;place-items:center;padding:30px;background:#f1f5f9}
.setup-box{max-width:580px;padding:45px;border:1px solid #e0e7ef;border-radius:16px;background:#fff;box-shadow:0 12px 42px #2143630b}
.setup-icon{display:grid;place-items:center;width:54px;height:54px;border-radius:14px;background:#e8f3fa;color:#477c9c;font-size:29px}
.eyebrow{font-size:10px;letter-spacing:.15em;color:#96a7b6}
.setup-box h1{font-size:23px}
.setup-box p{color:#8191a0;font-size:13px;line-height:1.8}
.folder{padding:13px;margin:20px 0;background:#f2f6f9;color:#43637e;border-radius:8px;font-size:12px}
.setup-box button{padding:12px 17px;border:1px solid #1d547a;background:#1d547a;border-radius:7px;color:white;cursor:pointer}
.status-error,.app-error{color:#ab4e42!important}
.app-error{position:absolute;bottom:15px;left:25%;right:25%;display:flex;justify-content:space-between;padding:14px;border:1px solid #eccac2;border-radius:8px;background:#fff1ed;box-shadow:0 12px 25px #0001;font-size:12px}
.app-error button{border:none;background:none;cursor:pointer}
</style>
