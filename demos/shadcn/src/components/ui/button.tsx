import * as React from 'react';
import { Slot } from '@radix-ui/react-slot';
import { cva, type VariantProps } from 'class-variance-authority';
import { cn } from './utils';

const buttonVariants=cva(
  'inline-flex shrink-0 items-center justify-center gap-1.5 whitespace-nowrap rounded-md text-[11px] font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 disabled:pointer-events-none disabled:opacity-50',
  {variants:{
    variant:{
      primary:'bg-blue-600 text-white hover:bg-blue-700 shadow-sm',
      secondary:'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50',
      ghost:'bg-transparent text-slate-600 hover:bg-slate-100',
      danger:'bg-red-50 text-red-700 hover:bg-red-100'
    },
    small:{true:'h-7 px-2.5',false:'h-8 px-3'}
  },defaultVariants:{variant:'secondary',small:false}}
);
export interface ButtonPrimitiveProps extends React.ButtonHTMLAttributes<HTMLButtonElement>, VariantProps<typeof buttonVariants>{
  asChild?:boolean;
}
export const Button=React.forwardRef<HTMLButtonElement,ButtonPrimitiveProps>(
  ({className,variant,small,asChild=false,type='button',...props},ref)=>{
    const Comp=asChild?Slot:'button';
    return <Comp ref={ref} type={type} className={cn(buttonVariants({variant,small,className}))} data-variant={variant||'secondary'} {...props}/>;
  }
);
Button.displayName='Button';
