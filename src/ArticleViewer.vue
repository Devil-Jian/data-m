<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref, shallowRef, watch } from "vue";
import { invoke } from "@tauri-apps/api/core";
import { renderAsync } from "docx-preview";
import { getDocument, GlobalWorkerOptions } from "pdfjs-dist/legacy/build/pdf.mjs";
import workerUrl from "pdfjs-dist/legacy/build/pdf.worker.min.mjs?url";
import type { ItemAnchor, Paper, PaperItem } from "./types";

GlobalWorkerOptions.workerSrc = workerUrl;

const props = defineProps<{
  paper: Paper | null;
  items: PaperItem[];
  bindingItemId: string | null;
}>();
const emit = defineEmits<{
  (e: "select-item", id: string): void;
  (e: "bind-anchor", itemId: string, anchor: ItemAnchor): void;
}>();

type LoadedPdf = Awaited<ReturnType<typeof getDocument>["promise"]>;
const pdfDoc = shallowRef<LoadedPdf | null>(null);
const currentPage = ref(1);
const loading = ref(false);
const error = ref("");
const canvas = ref<HTMLCanvasElement | null>(null);
const pdfArea = ref<HTMLDivElement | null>(null);
const docxArea = ref<HTMLDivElement | null>(null);
const canvasSize = ref({ width: 0, height: 0 });
const drawBox = ref<{ x: number; y: number; w: number; h: number } | null>(null);
const dragOrigin = ref<{ x: number; y: number } | null>(null);
const captions = ref<{ item_id: string; x: number; y: number; w: number; h: number }[]>([]);

let openToken = 0;
let paintToken = 0;
let activeRender: { cancel: () => void } | null = null;
let oldPdf: LoadedPdf | null = null;

const isPdf = () => props.paper?.manuscript_extension === "pdf";
const isDocx = () => props.paper?.manuscript_extension === "docx";

function clamp(value: number): number {
  return Math.max(0, Math.min(1, value));
}

function pointerCoord(event: PointerEvent): { x: number; y: number } {
  const box = pdfArea.value?.getBoundingClientRect();
  if (!box) return { x: 0, y: 0 };
  return {
    x: clamp((event.clientX - box.left) / box.width),
    y: clamp((event.clientY - box.top) / box.height),
  };
}

function dragStart(event: PointerEvent) {
  if (!isPdf() || !props.bindingItemId) return;
  const at = pointerCoord(event);
  dragOrigin.value = at;
  drawBox.value = { ...at, w: 0, h: 0 };
  pdfArea.value?.setPointerCapture(event.pointerId);
  event.preventDefault();
}

function dragMove(event: PointerEvent) {
  if (!dragOrigin.value) return;
  const to = pointerCoord(event);
  const origin = dragOrigin.value;
  drawBox.value = {
    x: Math.min(origin.x, to.x),
    y: Math.min(origin.y, to.y),
    w: Math.abs(to.x - origin.x),
    h: Math.abs(to.y - origin.y),
  };
}

function dragFinish(event: PointerEvent) {
  if (!dragOrigin.value) return;
  dragMove(event);
  const box = drawBox.value;
  const id = props.bindingItemId;
  dragOrigin.value = null;
  drawBox.value = null;
  if (box && id && box.w > 0.01 && box.h > 0.01) {
    emit("bind-anchor", id, {
      kind: "pdf",
      page: currentPage.value,
      x: box.x, y: box.y, w: box.w, h: box.h,
      index: null,
    });
  }
}

function anchoredOnThisPage(item: PaperItem): boolean {
  return item.anchor_kind === "pdf"
    && item.anchor_page === currentPage.value
    && item.anchor_x !== null
    && item.anchor_y !== null
    && item.anchor_w !== null
    && item.anchor_h !== null;
}

function hotspotStyle(item: PaperItem) {
  return {
    left: `${(item.anchor_x ?? 0) * 100}%`,
    top: `${(item.anchor_y ?? 0) * 100}%`,
    width: `${(item.anchor_w ?? 0) * 100}%`,
    height: `${(item.anchor_h ?? 0) * 100}%`,
  };
}

async function renderPdfPage() {
  const document = pdfDoc.value;
  if (!document || !isPdf()) return;
  const token = ++paintToken;
  activeRender?.cancel();
  activeRender = null;
  captions.value = [];
  await nextTick();
  if (!canvas.value || !pdfArea.value) return;

  const page = await document.getPage(currentPage.value);
  if (token !== paintToken) return;
  const base = page.getViewport({ scale: 1 });
  const containerWidth = pdfArea.value.parentElement?.clientWidth ?? 700;
  const scale = Math.min(1.6, Math.max(0.5, (containerWidth - 36) / base.width));
  const viewport = page.getViewport({ scale });
  const pixelRatio = Math.min(window.devicePixelRatio || 1, 2);
  const target = canvas.value;
  const ctx = target.getContext("2d");
  if (!ctx) throw new Error("画布不可用");
  target.width = Math.floor(viewport.width * pixelRatio);
  target.height = Math.floor(viewport.height * pixelRatio);
  target.style.width = `${viewport.width}px`;
  target.style.height = `${viewport.height}px`;
  canvasSize.value = { width: viewport.width, height: viewport.height };
  ctx.setTransform(pixelRatio, 0, 0, pixelRatio, 0, 0);

  const task = page.render({ canvasContext: ctx, viewport, canvas: target });
  activeRender = task;
  try {
    await task.promise;
  } catch (cause) {
    if (token !== paintToken) return;
    throw cause;
  }
  if (token !== paintToken) return;

  // Exact caption text can be clicked without a user-drawn rectangle.
  // Image-area selection still requires an explicit one-time region binding.
  const content = await page.getTextContent();
  const found: typeof captions.value = [];
  for (const run of content.items) {
    if (!("str" in run) || !("transform" in run)) continue;
    const label = run.str.trim().toLowerCase();
    if (!label || !run.transform) continue;
    for (const item of props.items) {
      if (label === item.label.trim().toLowerCase()) {
        const at = viewport.convertToViewportPoint(run.transform[4], run.transform[5]);
        const size = Math.max(12, Math.abs(run.transform[3] * scale));
        found.push({
          item_id: item.item_id,
          x: Math.max(0, at[0]),
          y: Math.max(0, at[1] - size),
          w: Math.min(viewport.width, Math.max(70, run.width * scale)),
          h: size * 1.5,
        });
      }
    }
  }
  captions.value = found;
}

function markDocxElements() {
  const container = docxArea.value;
  if (!container) return;
  let imageIndex = 0;
  let tableIndex = 0;
  for (const node of Array.from(container.querySelectorAll("img, table"))) {
    const element = node as HTMLElement;
    const kind: ItemAnchor["kind"] =
      element.tagName.toLowerCase() === "img" ? "docx_image" : "docx_table";
    const index = kind === "docx_image" ? imageIndex++ : tableIndex++;
    const linked = props.items.find(
      item => item.anchor_kind === kind && item.anchor_index === index,
    );
    element.classList.toggle("doc-linked-element", Boolean(linked));
    element.title = linked
      ? `点击跳转至资源：${linked.label}`
      : "选择右侧图表条目并启动绑定后，点击此内容建立关联";
    element.onclick = (event: MouseEvent) => {
      event.preventDefault();
      event.stopPropagation();
      if (props.bindingItemId) {
        emit("bind-anchor", props.bindingItemId, {
          kind, page: null, x: null, y: null, w: null, h: null, index,
        });
      } else if (linked) {
        emit("select-item", linked.item_id);
      }
    };
  }
}

async function loadArticle() {
  const paper = props.paper;
  const token = ++openToken;
  paintToken++;
  activeRender?.cancel();
  activeRender = null;
  pdfDoc.value = null;
  oldPdf?.destroy().catch(() => undefined);
  oldPdf = null;
  currentPage.value = 1;
  captions.value = [];
  error.value = "";
  if (docxArea.value) docxArea.value.replaceChildren();
  if (!paper?.manuscript_sha256) return;

  loading.value = true;
  try {
    const bytes = new Uint8Array(
      await invoke<ArrayBuffer>("read_paper_document", { paperId: paper.paper_id }),
    );
    if (token !== openToken) return;

    if (paper.manuscript_extension === "pdf") {
      const task = getDocument({ data: bytes, useSystemFonts: true });
      const document = await task.promise;
      if (token !== openToken) {
        await document.destroy();
        return;
      }
      pdfDoc.value = document;
      oldPdf = document;
      loading.value = false;
      await renderPdfPage();
    } else if (paper.manuscript_extension === "docx") {
      await nextTick();
      if (!docxArea.value) throw new Error("DOCX 容器不可用");
      await renderAsync(bytes, docxArea.value, undefined, {
        renderAltChunks: false,
        breakPages: true,
        ignoreLastRenderedPageBreak: false,
      });
      if (token !== openToken) return;
      markDocxElements();
    } else {
      error.value = "当前仅支持 PDF / DOCX 内嵌预览";
    }
  } catch (cause) {
    if (token === openToken) {
      error.value = `论文预览失败：${String(cause)}`;
    }
  } finally {
    if (token === openToken) loading.value = false;
  }
}

async function changePage(page: number) {
  if (!pdfDoc.value || page < 1 || page > pdfDoc.value.numPages) return;
  currentPage.value = page;
  error.value = "";
  try {
    await renderPdfPage();
  } catch (cause) {
    if (!String(cause).includes("RenderingCancelledException")) {
      error.value = `页面渲染失败：${String(cause)}`;
    }
  }
}

watch(
  () => [props.paper?.paper_id, props.paper?.manuscript_sha256],
  () => { void loadArticle(); },
  { immediate: true, flush: "post" },
);
watch(
  () => props.items,
  () => {
    if (isDocx() && docxArea.value) markDocxElements();
  },
  { deep: true },
);
onBeforeUnmount(() => {
  openToken++;
  paintToken++;
  activeRender?.cancel();
  oldPdf?.destroy().catch(() => undefined);
});
</script>

<template>
  <div class="article-viewer">
    <header class="article-toolbar">
      <div class="article-title">
        <span class="mini-label">文章阅读</span>
        <strong>{{ paper?.manuscript_filename || "尚未导入论文正文" }}</strong>
      </div>
      <div v-if="pdfDoc && isPdf()" class="page-nav">
        <button :disabled="currentPage <= 1" @click="changePage(currentPage - 1)">上一页</button>
        <span>{{ currentPage }} / {{ pdfDoc.numPages }}</span>
        <button :disabled="currentPage >= pdfDoc.numPages" @click="changePage(currentPage + 1)">下一页</button>
      </div>
      <span v-else-if="isDocx()" class="preview-type">DOCX · 只读</span>
    </header>

    <div v-if="!paper" class="placeholder">
      <div class="placeholder-symbol">▤</div>
      <h3>从左侧选择一篇论文</h3>
      <p>论文列表、正文预览和右侧原始资源库会同步切换。</p>
    </div>
    <div v-else-if="!paper.manuscript_sha256" class="placeholder">
      <div class="placeholder-symbol">▤</div>
      <h3>该论文尚未附加正文</h3>
      <p>导入 PDF 或 DOCX 文章后，可以直接在正文中绑定 Figure、Panel 和 Table。</p>
    </div>
    <div v-if="error" class="preview-error" role="alert">{{ error }}</div>
    <div v-if="loading" class="preview-loading">正在读取并渲染论文…</div>
    <div v-show="paper && isPdf() && pdfDoc && !loading" class="pdf-scroll">
      <p v-if="bindingItemId" class="bind-notice">框选论文中的图像或表格区域以建立关联；绑定后点击该区域即可定位资源。</p>
      <div
        ref="pdfArea"
        class="pdf-stage"
        :class="{ binding: Boolean(bindingItemId) }"
        :style="{ width: canvasSize.width + 'px', height: canvasSize.height + 'px' }"
        @pointerdown="dragStart"
        @pointermove="dragMove"
        @pointerup="dragFinish"
        @pointercancel="dragFinish"
      >
        <canvas ref="canvas"></canvas>
        <template v-for="item in items" :key="item.item_id">
          <button
            v-if="anchoredOnThisPage(item)"
            class="paper-hotspot"
            :class="{ disabled: Boolean(bindingItemId) }"
            :style="hotspotStyle(item)"
            :title="`点击打开资源：${item.label}`"
            @click.stop="emit('select-item', item.item_id)"
          >
            <span>{{ item.label }}</span>
          </button>
        </template>
        <button
          v-for="(caption, index) in captions"
          :key="'caption-' + index"
          class="caption-hotspot"
          :class="{ disabled: Boolean(bindingItemId) }"
          :style="{
            left: caption.x + 'px',
            top: caption.y + 'px',
            width: caption.w + 'px',
            height: caption.h + 'px'
          }"
          :title="'点击查看图表原始数据'"
          @click.stop="emit('select-item', caption.item_id)"
        ></button>
        <div v-if="drawBox" class="draw-rect" :style="{
          left: drawBox.x * 100 + '%', top: drawBox.y * 100 + '%',
          width: drawBox.w * 100 + '%', height: drawBox.h * 100 + '%',
        }"></div>
      </div>
    </div>
    <div v-show="paper && isDocx() && !loading" class="docx-scroll">
      <p v-if="bindingItemId" class="bind-notice">点击文章中的图片或表格，即可绑定当前选中的资源条目。</p>
      <div ref="docxArea" class="docx-document"></div>
    </div>
  </div>
</template>

<style scoped>
.article-viewer { display: flex; flex-direction: column; min-width: 0; min-height: 0; height: 100%; background: #eaf0f5; }
.article-toolbar { display: flex; align-items: center; gap: 12px; justify-content: space-between; flex: 0 0 auto; min-height: 70px; padding: 14px 22px; border-bottom: 1px solid #e2e8ee; background: #fff; }
.article-title { display: flex; flex-direction: column; gap: 5px; min-width: 0; }
.article-title strong { font-size: 13px; overflow: hidden; white-space: nowrap; text-overflow: ellipsis; }
.mini-label, .preview-type { color: #8a98a8; font-size: 10px; font-weight: 650; letter-spacing: .04em; }
.page-nav { display: flex; align-items: center; gap: 10px; flex-shrink: 0; color: #51657b; font-size: 12px; }
.page-nav button { padding: 6px 9px; border: 1px solid #d8e2eb; border-radius: 6px; background: white; color: #37536f; cursor: pointer; }
.page-nav button:disabled { opacity: .35; cursor: default; }
.pdf-scroll, .docx-scroll { flex: 1; min-height: 0; overflow: auto; padding: 20px; text-align: center; }
.pdf-stage { position: relative; margin: 0 auto 25px; background: #fff; box-shadow: 0 8px 24px #31465a26; }
.pdf-stage canvas { display: block; }
.pdf-stage.binding { cursor: crosshair; touch-action: none; user-select: none; }
.paper-hotspot { position: absolute; display: block; padding: 0; border: 2px solid #3189c4; outline: 2px solid #ffffff80; background: #2f96d21c; cursor: pointer; text-align: left; }
.paper-hotspot span { position: absolute; top: -22px; left: -2px; max-width: 100%; padding: 3px 8px; background: #276a99; color: white; font-size: 10px; white-space: nowrap; }
.paper-hotspot:hover { border-color: #e59a32; background: #edb65236; }
.caption-hotspot { position: absolute; border: 1px dashed #509ac6; background: #4b9bca12; cursor: pointer; }
.caption-hotspot:hover { background: #4b9bca35; }
.paper-hotspot.disabled, .caption-hotspot.disabled { pointer-events: none; opacity: .35; }
.draw-rect { position: absolute; pointer-events: none; border: 2px dashed #e58f21; background: #f5b2413d; }
.placeholder { display: flex; flex: 1; min-height: 380px; justify-content: center; align-items: center; flex-direction: column; text-align: center; padding: 35px; }
.placeholder-symbol { display: grid; place-items: center; width: 60px; height: 60px; margin-bottom: 22px; border-radius: 18px; background: #dce7f0; color: #65829c; font-size: 30px; }
.placeholder h3 { margin: 0 0 12px; font-size: 16px; }
.placeholder p { max-width: 330px; color: #8090a1; font-size: 12px; line-height: 1.8; }
.preview-loading, .preview-error, .bind-notice { padding: 12px 18px; margin: 0 0 16px; border-radius: 8px; color: #465f79; background: #fff; font-size: 12px; text-align: center; }
.preview-error { margin: 20px; color: #a33c35; background: #ffede9; }
.bind-notice { background: #fff2d9; color: #906122; text-align: left; }
.docx-document { text-align: left; }
.docx-document :deep(.docx-wrapper) { background: transparent; padding: 0; }
.docx-document :deep(.docx) { max-width: 100%; margin: 0 auto 22px; box-shadow: 0 8px 24px #31465a26; }
.docx-document :deep(img), .docx-document :deep(table) { cursor: pointer; }
.docx-document :deep(.doc-linked-element) { outline: 2px solid #3d9ecc; outline-offset: 2px; }
.docx-document :deep(img:hover), .docx-document :deep(table:hover) { outline: 2px solid #e59a32; }
</style>
