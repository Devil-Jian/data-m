import * as React from 'react';
import * as SelectPrimitive from '@radix-ui/react-select';
import { Check, ChevronDown } from 'lucide-react';
import type { SelectProps } from '@data-m/core';

export function SelectField({value,onChange,options,ariaLabel}:SelectProps) {
  return <SelectPrimitive.Root value={value} onValueChange={onChange}>
    <SelectPrimitive.Trigger aria-label={ariaLabel||'选择'} className="inline-flex h-8 max-w-[265px] min-w-[112px] items-center justify-between gap-2 rounded-md border border-slate-200 bg-white px-3 text-[11px] text-slate-700 outline-none focus:ring-2 focus:ring-blue-200">
      <SelectPrimitive.Value placeholder="请选择"/>
      <SelectPrimitive.Icon><ChevronDown size={13}/></SelectPrimitive.Icon>
    </SelectPrimitive.Trigger>
    <SelectPrimitive.Portal>
      <SelectPrimitive.Content position="popper" sideOffset={5} className="z-[120] max-h-[300px] min-w-[190px] overflow-hidden rounded-md border border-slate-200 bg-white p-1 shadow-lg">
        <SelectPrimitive.Viewport>
          {options.map(o=><SelectPrimitive.Item value={o.value} key={o.value}
            className="relative flex cursor-pointer select-none items-center rounded-sm py-1.5 pl-7 pr-2 text-[11px] text-slate-700 outline-none data-[highlighted]:bg-blue-50">
            <SelectPrimitive.ItemIndicator className="absolute left-2"><Check size={13}/></SelectPrimitive.ItemIndicator>
            <SelectPrimitive.ItemText>{o.label}</SelectPrimitive.ItemText>
          </SelectPrimitive.Item>)}
        </SelectPrimitive.Viewport>
      </SelectPrimitive.Content>
    </SelectPrimitive.Portal>
  </SelectPrimitive.Root>;
}
