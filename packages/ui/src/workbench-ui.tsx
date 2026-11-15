import React from 'react';
import type {UIKit} from '@data-m/core';
import {Button,SelectField,CheckboxField,InputField,BadgeField} from './index';

/** Single UI adapter shared by the official desktop app and legacy demo. */
export const shadcnUI:UIKit={
  Button:({children,onClick,variant='secondary',small=false,disabled,title})=>
    <Button onClick={onClick} variant={variant} small={small} disabled={disabled} title={title}>{children}</Button>,
  Select:SelectField,
  Input:InputField,
  Check:CheckboxField,
  Badge:BadgeField
};
