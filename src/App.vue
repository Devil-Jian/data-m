<script setup lang="ts">
const appVersion = "0.1.0";
</script>

<template>
  <main class="app-shell">
    <header class="topbar">
      <div class="brand-mark" aria-hidden="true">D</div>
      <div>
        <p class="eyebrow">LOCAL RESEARCH ARCHIVE</p>
        <h1>论文原始数据归档平台</h1>
      </div>
      <span class="version">v{{ appVersion }}</span>
    </header>

    <section class="welcome-card">
      <div class="card-icon" aria-hidden="true">⌂</div>
      <p class="eyebrow">WORKSPACE INITIALIZING</p>
      <h2>本地科研数据仓库</h2>
      <p class="description">
        用统一的数据编号管理论文原始文件、图表素材与数据引用关系。
        所有原始文件保存在本地数据目录。
      </p>
      <div class="status-row">
        <span class="status-dot"></span>
        <span>桌面应用框架已启动，数据仓库模块正在接入</span>
      </div>
    </section>

    <footer>数据默认保存在 D:\PaperArchiveData · 离线可用</footer>
  </main>
</template>

<style>
:root {
  font-family:
    Inter, "Microsoft YaHei UI", "PingFang SC", "Noto Sans CJK SC",
    system-ui, sans-serif;
  color: #182230;
  background: #f5f7fb;
  font-synthesis: none;
  text-rendering: optimizeLegibility;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

* {
  box-sizing: border-box;
}

body {
  min-width: 360px;
  min-height: 100vh;
  margin: 0;
}

button,
input {
  font: inherit;
}

.app-shell {
  display: flex;
  min-height: 100vh;
  flex-direction: column;
  padding: 32px clamp(24px, 6vw, 88px) 24px;
}

.topbar {
  display: flex;
  align-items: center;
  gap: 14px;
}

.brand-mark {
  display: grid;
  width: 42px;
  height: 42px;
  place-items: center;
  border-radius: 12px;
  background: #183b63;
  color: white;
  font-size: 20px;
  font-weight: 700;
}

.eyebrow {
  margin: 0 0 6px;
  color: #718096;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.16em;
}

h1 {
  margin: 0;
  font-size: 17px;
  font-weight: 650;
}

.version {
  margin-left: auto;
  color: #718096;
  font-size: 12px;
}

.welcome-card {
  width: min(100%, 720px);
  margin: auto;
  padding: clamp(28px, 5vw, 52px);
  border: 1px solid #e4e9f0;
  border-radius: 20px;
  background: white;
  box-shadow: 0 16px 48px rgb(26 44 68 / 6%);
}

.card-icon {
  display: grid;
  width: 48px;
  height: 48px;
  margin-bottom: 28px;
  place-items: center;
  border-radius: 14px;
  background: #edf3fa;
  color: #244e78;
  font-size: 25px;
}

h2 {
  margin: 0;
  font-size: clamp(25px, 4vw, 34px);
  letter-spacing: -0.04em;
}

.description {
  max-width: 560px;
  margin: 16px 0 28px;
  color: #5d6b7d;
  font-size: 14px;
  line-height: 1.9;
}

.status-row {
  display: flex;
  align-items: center;
  gap: 10px;
  padding-top: 18px;
  border-top: 1px solid #edf0f4;
  color: #536477;
  font-size: 12px;
}

.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: #d19a35;
  box-shadow: 0 0 0 4px #fbf4e8;
}

footer {
  color: #8491a2;
  font-size: 11px;
  text-align: center;
}

@media (max-width: 560px) {
  .app-shell {
    padding: 22px 18px 18px;
  }

  .welcome-card {
    padding: 28px 22px;
  }
}
</style>
