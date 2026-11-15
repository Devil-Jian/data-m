/**
 * Shared, deterministic demonstration fixtures derived from the Gemini prototype.
 * Never represents real patient or experimental files.
 */
export type ModuleName = 'experiment' | 'paper';
export type Stage = 'raw' | 'processed' | 'stats' | 'display';
export type ViewMode = 'grid' | 'list' | 'table';
export type ExperimentMode = 'overview' | 'stage';
export interface Replica {
  id: string;
  name: string;
  procName: string;
  value: number;
  inStats: boolean;
  isRep: boolean;
  visual: string;
  updated: string;
}
export interface ExpGroup { id: string; name: string; replicates: Replica[] }
export interface DataFile { id: string; name: string; visual: string; note: string; updated: string; }
export interface Assay {
  id: string; code: string; name: string; modality: string; metricName: string;
  metricUnit: string; groups: ExpGroup[]; statFiles: DataFile[]; displayFiles: DataFile[];
}
export interface Theme { id: string; name: string; code: string; assays: Assay[] }
export interface Panel {
  id: string; label: string; title: string; assayId: string;
  displayIds: string[]; statIds: string[]; highlightRepIds: string[];
  visual: string; note: string;
}
export interface Figure { id: string; figNum: string; caption: string; panels: Panel[] }
export interface Paper { id: string; code: string; title: string; journal: string; status: string; figures: Figure[] }
export interface Project { id: string; code: string; name: string; type: string; themes: Theme[]; papers: Paper[] }

export const STAGES: { id: Stage; label: string; short: string }[] = [
  { id: 'raw', label: '01_Raw', short: '原始数据' },
  { id: 'processed', label: '02_Processed', short: '处理数据' },
  { id: 'stats', label: '03_Statistics', short: '统计数据' },
  { id: 'display', label: '04_Display', short: '展示数据' }
];
const group = (
  id: string, name: string, prefix: string, values: number[],
  visual: string, repIndex = -1, ext = 'tif', updated = '2026-10-12'
): ExpGroup => ({
  id, name,
  replicates: values.map((value, i) => ({
    id: `${id}-r${i + 1}`,
    name: `${prefix}_Rep${i + 1}.${ext}`,
    procName: `${prefix}_Rep${i + 1}_ROI.tif`,
    value, inStats: true, isRep: i === repIndex, visual, updated
  }))
});
const datafile = (id: string, name: string, visual: string, note: string, updated = '2026-10-18'): DataFile =>
  ({ id, name, visual, note, updated });

const ros: Assay = {
  id: 'assay-ros', code: 'ROS-DCFH', name: 'ROS 活性氧荧光染色',
  modality: 'Fluorescence Microscopy (20x)', metricName: '平均荧光强度 (MFI)', metricUnit: 'AU',
  groups: [
    group('ros-g1','Control','Control',[18.4,19.1,17.9,20.2,18.8,19.5],'ros_low',2),
    group('ros-g2','H2O2 Model','Model',[84.6,88.1,81.9,86.4,83.7,85.2],'ros_high',1),
    group('ros-g3','Drug-L (5uM)','DrugL',[52.1,54.8,49.9,53.4,51.7,50.6],'ros_mid',0),
    group('ros-g4','Drug-H (20uM)','DrugH',[26.4,24.8,27.1,25.3,26.9,25.7],'ros_low',3)
  ],
  statFiles: [datafile('st-ros-1','ROS_MFI_Quantification_n6.xlsx','bar_ros','4 组 × n=6，全部生物学重复统计')],
  displayFiles: [
    datafile('dp-ros-1','Fig1a_ROS_Fluorescence_Panel.tiff','ros_panel_combo','每组选择 1 张代表图，原始数据全部保留'),
    datafile('dp-ros-2','Fig1b_ROS_MFI_Barplot.svg','bar_ros','基于全部 24 个样本的统计图')
  ]
};
const wb: Assay = {
  id:'assay-wb', code:'WB-NRF2', name:'WB 蛋白印迹 (Nrf2 / HO-1)',
  modality:'Chemiluminescence Blot (16-bit)', metricName:'相对灰度比值 (Nrf2/Actin)', metricUnit:'Ratio',
  groups:[
    group('wb-g1','Control','WB_Ctrl',[1,0.96,1.04,0.98,1.02,0.99],'wb_ctrl',0,'scn'),
    group('wb-g2','H2O2 Model','WB_Model',[0.42,0.39,0.45,0.41,0.44,0.40],'wb_model',0,'scn'),
    group('wb-g3','Drug-H (20uM)','WB_DrugH',[1.68,1.74,1.61,1.70,1.65,1.72],'wb_drug',0,'scn')
  ],
  statFiles:[datafile('st-wb-1','WB_Nrf2_HO1_Densitometry_n6.csv','bar_ros','各组 n=6，灰度定量汇总')],
  displayFiles:[datafile('dp-wb-1','Fig1c_WB_Nrf2_HO1_Blots.tiff','wb_combo','完整全膜图保留在 Raw')]
};
const mda: Assay = {
  id:'assay-mda', code:'BIO-MDA', name:'MDA / SOD 生化试剂盒检测',
  modality:'Microplate Reader', metricName:'MDA 含量', metricUnit:'nmol/mg',
  groups:[
    group('mda-g1','Control','Plate_A',[1.42,1.38,1.45,1.40,1.36,1.44],'plate_curve',-1,'csv'),
    group('mda-g2','H2O2 Model','Plate_B',[4.85,5.02,4.76,4.91,4.88,4.95],'plate_curve',-1,'csv')
  ],
  statFiles:[datafile('st-mda-1','MDA_SOD_Biochemical_Summary_n6.xlsx','bar_mda','每组 n=6，生化指标统计')],
  displayFiles:[datafile('dp-mda-1','Fig2a_MDA_Level_Chart.svg','bar_mda','全部有效读数统计')]
};
const jc1: Assay = {
  id:'assay-jc1', code:'FLOW-JC1', name:'JC-1 线粒体膜电位流式检测',
  modality:'Flow Cytometry (PE / FITC)', metricName:'Red/Green 比值', metricUnit:'Ratio',
  groups:[group('jc1-g1','Control','JC1_Ctrl',[3.82,3.75,3.90,3.88,3.79,3.84],'flow_scatter',0,'fcs')],
  statFiles:[datafile('st-jc1-1','JC1_MMP_Flow_Stats_n6.csv','bar_ros','6 个独立生物学重复')],
  displayFiles:[datafile('dp-jc1-1','Fig2b_JC1_Flow_DotPlot.tiff','flow_scatter','代表性门控图')]
};
const he: Assay = {
  id:'assay-he', code:'HIST-HE', name:'H&E 组织病理与 TUNEL 凋亡染色',
  modality:'Whole Slide Imaging (40x)', metricName:'Suzuki 病理损伤评分', metricUnit:'Score',
  groups:[
    group('he-g1','Sham 假手术组','Mouse_Sham',[0.5,0.4,0.6,0.5,0.3,0.5],'ros_low',0,'svs','2026-11-02'),
    group('he-g2','IRI 6h 模型组','Mouse_IRI',[3.8,3.9,3.6,3.7,3.8,3.9],'ros_high',1,'svs','2026-11-02')
  ],
  statFiles:[datafile('st-iri-1','IRI_Suzuki_Pathology_Score_n6.xlsx','bar_mda','每组 n=6 只小鼠')],
  displayFiles:[datafile('dp-iri-1','Fig1a_Liver_IRI_Histology.tiff','ros_panel_combo','肝小叶代表性视野')]
};

const rosReps = ros.groups.map(g => g.replicates.find(r => r.isRep)!.id);
export const INITIAL_PROJECTS: Project[] = [{
  id:'prj-01', code:'PRJ-2026-01', name:'Nrf2/HO-1 抗氧化通路课题 (FRBM 论文)', type:'课题 + 在投论文',
  themes:[
    {id:'theme-ox',name:'氧化应激机制研究',code:'THM-OX-01',assays:[ros,wb,mda]},
    {id:'theme-mito',name:'线粒体自噬通路验证',code:'THM-MITO-02',assays:[jc1]}
  ],
  papers:[{
    id:'p1',code:'FRBM-2026',
    title:'Activation of Nrf2/HO-1 Signaling Attenuates Oxidative Stress and Mitochondrial Dysfunction',
    journal:'Free Radical Biology and Medicine',status:'Under Revision',
    figures:[
      {id:'fig-1',figNum:'Figure 1',caption:'药物干预抑制 ROS 累积并激活 Nrf2/HO-1 通路',panels:[
        {id:'pan-1a',label:'Fig 1a',title:'DCFH-DA 荧光染色代表性图像',assayId:'assay-ros',displayIds:['dp-ros-1'],statIds:['st-ros-1'],highlightRepIds:rosReps,visual:'ros_panel_combo',note:'每组选择 1 张代表图，共 4 组 × n=6 计入统计'},
        {id:'pan-1b',label:'Fig 1b',title:'ROS 平均荧光强度定量统计',assayId:'assay-ros',displayIds:['dp-ros-2'],statIds:['st-ros-1'],highlightRepIds:[],visual:'bar_ros',note:'24 个生物学重复样本参与统计'},
        {id:'pan-1c',label:'Fig 1c',title:'Nrf2 与 HO-1 蛋白表达 Western Blot',assayId:'assay-wb',displayIds:['dp-wb-1'],statIds:['st-wb-1'],highlightRepIds:wb.groups.map(g=>g.replicates[0].id),visual:'wb_combo',note:'代表性条带与 n=6 灰度定量'}
      ]},
      {id:'fig-2',figNum:'Figure 2',caption:'氧化损伤标志物及线粒体膜电位评价',panels:[
        {id:'pan-2a',label:'Fig 2a',title:'MDA 水平定量测定',assayId:'assay-mda',displayIds:['dp-mda-1'],statIds:['st-mda-1'],highlightRepIds:[],visual:'bar_mda',note:'每组 n=6 的完整数据'},
        {id:'pan-2b',label:'Fig 2b',title:'JC-1 线粒体膜电位流式检测',assayId:'assay-jc1',displayIds:['dp-jc1-1'],statIds:['st-jc1-1'],highlightRepIds:[jc1.groups[0].replicates[0].id],visual:'flow_scatter',note:'代表性门控图，6 个重复进入统计'}
      ]}
    ]
  }]
},{
  id:'prj-02',code:'PRJ-2026-02',name:'肝脏缺血再灌注损伤课题 (Redox Biology 论文)',type:'动物实验 + 撰稿中',
  themes:[{id:'theme-iri',name:'肝缺血再灌注动物模型表型',code:'THM-IRI-01',assays:[he]}],
  papers:[{id:'p2',code:'RedoxBiol-2026',title:'Spatiotemporal Dynamics of Redox Homeostasis in Hepatic Ischemia-Reperfusion Injury',
    journal:'Redox Biology',status:'Drafting',
    figures:[{id:'fig-p2-1',figNum:'Figure 1',caption:'小鼠肝脏缺血再灌注 6h 组织病理学变化',panels:[
      {id:'pan-p2-1a',label:'Fig 1a',title:'肝脏组织 H&E 染色代表性视野',assayId:'assay-he',displayIds:['dp-iri-1'],statIds:['st-iri-1'],highlightRepIds:[he.groups[0].replicates[0].id,he.groups[1].replicates[1].id],visual:'ros_panel_combo',note:'每组 1 张代表图，全部 n=6 进入统计'}
    ]}]}]
}];
export function findAssay(project: Project, assayId: string): Assay | undefined {
  return project.themes.flatMap(t=>t.assays).find(a=>a.id===assayId);
}
export function allPanels(project: Project): Panel[] {
  return project.papers.flatMap(p=>p.figures.flatMap(f=>f.panels));
}
export function summary(values:number[]) {
  if (!values.length) return {n:0,mean:0,sd:0};
  const mean=values.reduce((a,b)=>a+b,0)/values.length;
  const sd=values.length>1 ? Math.sqrt(values.reduce((a,b)=>a+(b-mean)**2,0)/(values.length-1)) : 0;
  return {n:values.length,mean,sd};
}
