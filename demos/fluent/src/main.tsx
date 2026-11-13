import React from 'react';
import { createRoot } from 'react-dom/client';
import {
  Badge, Button, Checkbox, Dropdown, FluentProvider, Input, Option, webLightTheme
} from '@fluentui/react-components';
import { Workbench } from '@data-m/core';
import type { UIKit } from '@data-m/core';
import '@data-m/core/style.css';
import './style.css';

const ui: UIKit={
  Button:({children,onClick,variant='secondary',small,disabled,title})=>
    <Button onClick={onClick} disabled={disabled} title={title}
      size={small?'small':'medium'}
      appearance={variant==='primary'?'primary':variant==='ghost'?'subtle':variant==='danger'?'outline':'secondary'}>
      {children}
    </Button>,
  Select:({value,onChange,options,ariaLabel})=>
    <Dropdown size="small" aria-label={ariaLabel||'选择'}
      value={options.find(o=>o.value===value)?.label||''}
      selectedOptions={[value]}
      onOptionSelect={(_,data)=>{if(data.optionValue)onChange(data.optionValue)}}>
      {options.map(o=><Option key={o.value} value={o.value}>{o.label}</Option>)}
    </Dropdown>,
  Input:({value,onChange,placeholder,ariaLabel})=>
    <Input size="small" aria-label={ariaLabel||placeholder||'输入'}
      value={value} placeholder={placeholder} onChange={(_,data)=>onChange(data.value)}/>,
  Check:({checked,onChange,label})=>
    <Checkbox label={label||''} aria-label={label||'选择项目'} checked={checked}
      onChange={(_,data)=>onChange(data.checked===true)}/>,
  Badge:({children,tone='neutral'})=>
    <Badge appearance="tint" shape="rounded" size="small"
      color={tone==='blue'?'brand':tone==='green'?'success':tone==='amber'?'warning':'subtle'}>
      {children}
    </Badge>
};
createRoot(document.getElementById('root')!).render(
  <React.StrictMode><FluentProvider theme={webLightTheme} className="fluent-root">
    <Workbench ui={ui} variant="fluent"/>
  </FluentProvider></React.StrictMode>
);
