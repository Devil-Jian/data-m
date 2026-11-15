import React from 'react';
import {createRoot} from 'react-dom/client';
import {Workbench} from '@data-m/core';
import {shadcnUI} from '@data-m/ui/workbench-ui';
import '@data-m/core/style.css';
import '@data-m/ui/style.css';

createRoot(document.getElementById('root')!).render(
  <React.StrictMode><Workbench ui={shadcnUI} variant="shadcn"/></React.StrictMode>
);
