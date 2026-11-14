import {describe,expect,it} from 'vitest';
import {dropReducer,formatDropSize,initialDropState} from './drop-preview';
import type {DropPreview} from './drop-preview';

const preview:DropPreview={
  entries:[
    {name:'Raw_Rep1.tif',kind:'file',sizeBytes:1024,eligible:true,warning:null},
    {name:'Untitled Group',kind:'directory',sizeBytes:null,eligible:true,warning:null},
    {name:'shortcut',kind:'link',sizeBytes:null,eligible:false,warning:'符号链接暂不支持导入'}
  ],
  totalReceived:3,omittedCount:0,eligibleCount:2
};

describe('Windows native drop preview (read-only)',()=>{
  it('hover, leave, inspect, success and dismiss never store OS paths',()=>{
    const over=dropReducer(initialDropState,{type:'hover'});
    expect(over.kind).toBe('over');
    expect(dropReducer(over,{type:'leave'})).toEqual(initialDropState);
    const checking=dropReducer(over,{type:'start',requestId:1,count:3});
    expect(checking).toEqual({kind:'inspecting',requestId:1,count:3});
    const ready=dropReducer(checking,{type:'success',requestId:1,preview});
    expect(ready.kind).toBe('ready');
    expect(JSON.stringify(ready)).not.toContain('C:\\Users\\');
    expect(dropReducer(ready,{type:'dismiss'})).toEqual(initialDropState);
  });
  it('a stale async completion cannot overwrite a newer drop inspection',()=>{
    const first=dropReducer(initialDropState,{type:'start',requestId:12,count:3});
    const second=dropReducer(first,{type:'start',requestId:13,count:2});
    expect(dropReducer(second,{type:'success',requestId:12,preview})).toEqual(second);
    expect(dropReducer(second,{type:'fail',requestId:12})).toEqual(second);
    expect(dropReducer(second,{type:'success',requestId:13,preview}).kind).toBe('ready');
  });
  it('dismissal prevents async completion from reopening the preview',()=>{
    const checking=dropReducer(initialDropState,{type:'start',requestId:7,count:2});
    expect(dropReducer(dropReducer(checking,{type:'dismiss'}),{
      type:'success',requestId:7,preview
    })).toEqual(initialDropState);
  });
  it('native drag-over and leave cannot discard the completed preview',()=>{
    const ready=dropReducer(
      dropReducer(initialDropState,{type:'start',requestId:1,count:3}),
      {type:'success',requestId:1,preview}
    );
    expect(dropReducer(ready,{type:'hover'})).toEqual(ready);
    expect(dropReducer(ready,{type:'leave'})).toEqual(ready);
  });
  it('read failures have a safe message without exposing source paths',()=>{
    const checking=dropReducer(initialDropState,{type:'start',requestId:1,count:3});
    const error=dropReducer(checking,{type:'fail',requestId:1});
    expect(error).toEqual({
      kind:'error',message:'无法读取拖入项目的信息；原始文件未修改'
    });
  });
  it('formats file sizes without confusing a directory with a zero-byte file',()=>{
    expect(formatDropSize(null)).toBe('—');
    expect(formatDropSize(0)).toBe('0 B');
    expect(formatDropSize(1024)).toBe('1.0 KB');
    expect(formatDropSize(1048576)).toBe('1.0 MB');
    expect(formatDropSize(1073741824)).toBe('1.00 GB');
  });
});
