import React from 'react';
import { createRoot } from 'react-dom/client';
import { Workbench } from '@data-m/core';
import type { UIKit } from '@data-m/core';
import { Button } from './components/ui/button';
import { SelectField } from './components/ui/select';
import { CheckboxField } from './components/ui/checkbox';
import { InputField } from './components/ui/input';
import { BadgeField } from './components/ui/badge';
import '@data-m/core/style.css';
import './style.css';

const ui: UIKit = {
  Button: ({ children, onClick, variant='secondary', small=false, disabled, title }) =>
    <Button onClick={onClick} variant={variant} small={small} disabled={disabled} title={title}>{children}</Button>,
  Select: SelectField,
  Input: InputField,
  Check: CheckboxField,
  Badge: BadgeField
};
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><Workbench ui={ui} variant="shadcn" /></React.StrictMode>
);
