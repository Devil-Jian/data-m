import * as React from 'react';
import type { BadgeProps } from '@data-m/core';
const tones={
  blue:'bg-blue-50 text-blue-700 border-blue-100',
  green:'bg-emerald-50 text-emerald-700 border-emerald-100',
  amber:'bg-amber-50 text-amber-700 border-amber-100',
  neutral:'bg-slate-100 text-slate-600 border-slate-200'
};
export function BadgeField({children,tone='neutral'}:BadgeProps){
  return <span className={'inline-flex items-center rounded-sm border px-1.5 py-0.5 text-[9px] font-semibold '+tones[tone]}>{children}</span>;
}
