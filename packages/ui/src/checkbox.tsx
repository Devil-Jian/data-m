import * as React from 'react';
import * as CheckboxPrimitive from '@radix-ui/react-checkbox';
import { Check } from 'lucide-react';
import type { CheckProps } from '@data-m/core';
export function CheckboxField({checked,onChange,label}:CheckProps) {
  return <label className="inline-flex items-center gap-1.5 text-xs text-slate-600">
    <CheckboxPrimitive.Root checked={checked} onCheckedChange={v=>onChange(v===true)}
      aria-label={label||'选择项目'}
      className="inline-flex h-3.5 w-3.5 shrink-0 items-center justify-center rounded-[3px] border border-slate-300 bg-white text-white data-[state=checked]:border-blue-600 data-[state=checked]:bg-blue-600">
      <CheckboxPrimitive.Indicator><Check size={11} strokeWidth={3}/></CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>{label&&<span>{label}</span>}
  </label>;
}
