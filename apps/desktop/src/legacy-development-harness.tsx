/** Development-only historical UI harness. Never import this module in a release bundle. */
import React,{useState} from 'react';
import {Workbench} from '@data-m/core';
import {ProjectTreeManager} from './project-tree.tsx';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import type {ImportDropRegistrar} from './import-jobs.ts';

export default function LegacyDevelopmentHarness({registerImportDrop}:{registerImportDrop:ImportDropRegistrar}){
  const [managingProjects,setManagingProjects]=useState(false);
  const [projectBusy,setProjectBusy]=useState(false);
  return <>
    <nav className="dm-desktop-modebar" aria-label="工作区模式">
      <shadcnUI.Button disabled={projectBusy} variant={managingProjects?'primary':'secondary'} small onClick={()=>setManagingProjects(true)}>项目管理</shadcnUI.Button>
      <shadcnUI.Button disabled={projectBusy} variant={managingProjects?'secondary':'primary'} small onClick={()=>setManagingProjects(false)}>演示工作区</shadcnUI.Button>
      <span>{managingProjects?'Project / Theme / Assay':'演示数据与文件交互，不代表真实入库'}</span>
      <small className="dm-release-candidate" aria-label="安装及版本信息">0.1.0 S11 测试构建 · 未签名 · 支持仓库 Schema v1–v3 · 非正式 v1.0</small>
    </nav>
    <div className="dm-desktop-content">
      {managingProjects?<ProjectTreeManager onBusyChange={setProjectBusy} registerImportDrop={registerImportDrop}/>:<Workbench ui={shadcnUI} variant="shadcn" legacyNativePaper/>}
    </div>
  </>;
}
