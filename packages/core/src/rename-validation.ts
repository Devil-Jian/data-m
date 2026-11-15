/** Pure conservative Windows filename rules for a P1 preview-only rename.
 * This does not authorize a filesystem modification.
 */
export function fileExtension(name:string):string{
  const index=name.lastIndexOf('.');
  return index>0?name.slice(index):'';
}
export function filenameStemLength(name:string):number{
  return name.length-fileExtension(name).length;
}
export function renameValidationError(
  proposed:string,
  original:string,
  siblingFilenames:readonly string[]
):string|null{
  if(proposed.length===0||proposed.trim().length===0)return '文件名不能为空';
  if(proposed==='.'||proposed==='..')return '不能使用特殊目录名称';
  if(proposed.length>255)return '文件名不能超过 255 个字符';
  if(/[<>:"/\\|?*\u0000-\u001f]/.test(proposed))return '文件名包含 Windows 不允许的字符';
  if(/[. ]$/.test(proposed))return '文件名不能以空格或句点结尾';
  // Windows forbids reserved device stems, including CON.txt and COM1.tif.
  const stem=proposed.split('.')[0].replace(/[ .]+$/g,'').toUpperCase();
  if(/^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$/.test(stem))
    return '文件名为 Windows 保留设备名称';
  if(fileExtension(proposed).toLowerCase()!==fileExtension(original).toLowerCase())
    return '预览模式不允许修改文件扩展名';
  const name=proposed.toLocaleLowerCase('en-US');
  if(proposed.toLocaleLowerCase('en-US')!==original.toLocaleLowerCase('en-US')&&
    siblingFilenames.some(sibling=>sibling.toLocaleLowerCase('en-US')===name))
    return '同一阶段与容器中已存在同名文件';
  return null;
}
