import React from 'react';
import { createRoot } from 'react-dom/client';
import { Badge, Button, Checkbox, MantineProvider, Select, TextInput } from '@mantine/core';
import { Workbench } from '@data-m/core';
import type { UIKit } from '@data-m/core';
import '@mantine/core/styles.css';
import '@data-m/core/style.css';
import './style.css';

const ui: UIKit={
  Button:({children,onClick,variant='secondary',small,disabled,title})=>
    <Button onClick={onClick} title={title} disabled={disabled} size={small?'compact-xs':'xs'}
      radius="sm" variant={variant==='primary'?'filled':variant==='ghost'?'subtle':variant==='danger'?'light':'default'}
      color={variant==='danger'?'red':'blue'}>{children}</Button>,
  Select:({value,onChange,options,ariaLabel})=>
    <Select aria-label={ariaLabel||'选择'} size="xs" radius="sm" value={value}
      allowDeselect={false} onChange={v=>{if(v!==null)onChange(v)}}
      data={options.map(o=>({value:o.value,label:o.label}))} comboboxProps={{withinPortal:true}}/>,
  Input:({value,onChange,placeholder,ariaLabel})=>
    <TextInput aria-label={ariaLabel||placeholder||'输入'} size="xs" radius="sm"
      placeholder={placeholder} value={value} onChange={e=>onChange(e.currentTarget.value)}/>,
  Check:({checked,onChange,label})=><Checkbox label={label} aria-label={label||'选择项目'}
    size="xs" checked={checked} onChange={e=>onChange(e.currentTarget.checked)}/>,
  Badge:({children,tone='neutral'})=><Badge size="xs" radius="sm" variant="light"
    color={tone==='green'?'teal':tone==='amber'?'yellow':tone==='blue'?'blue':'gray'}>{children}</Badge>
};
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><MantineProvider defaultColorScheme="light">
    <Workbench ui={ui} variant="mantine"/>
  </MantineProvider></React.StrictMode>
);
