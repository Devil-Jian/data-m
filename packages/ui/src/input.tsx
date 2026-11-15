import * as React from 'react';
import type { InputProps } from '@data-m/core';
export function InputField({value,onChange,placeholder,ariaLabel}:InputProps){
  return <input aria-label={ariaLabel||placeholder||'输入'} value={value} placeholder={placeholder}
    onChange={e=>onChange(e.target.value)}
    className="h-8 min-w-[160px] rounded-md border border-slate-200 bg-white px-3 text-[11px] text-slate-700 outline-none placeholder:text-slate-400 focus:border-blue-400 focus:ring-2 focus:ring-blue-100"/>;
}
