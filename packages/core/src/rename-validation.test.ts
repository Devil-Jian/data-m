import {describe,expect,it} from 'vitest';
import {fileExtension,filenameStemLength,renameValidationError} from './rename-validation';

describe('Windows filename validation for preview-only rename',()=>{
  it('selects only the stem and preserves multipart extensions',()=>{
    expect(fileExtension('sample.tif')).toBe('.tif');
    expect(filenameStemLength('sample.tif')).toBe(6);
    expect(fileExtension('study.nii.gz')).toBe('.gz');
    expect(filenameStemLength('study.nii.gz')).toBe(9);
    expect(fileExtension('.gitignore')).toBe('');
    expect(fileExtension('NO_EXTENSION')).toBe('');
  });
  it('rejects path separators, wildcards, control characters and empty names',()=>{
    for(const candidate of ['', '   ', '.', '..','a/b.tif','a\\b.tif','a?.tif',
      'a*.tif','a|b.tif','a:b.tif','bad\nname.tif']){
      expect(renameValidationError(candidate,'original.tif',[])).not.toBeNull();
    }
  });
  it('rejects trailing dots/spaces, overly long and reserved Windows device names',()=>{
    for(const name of ['sample.tif.','sample.tif ','CON.tif','NUL.tif','prn.TIF',
      'LPT9.tif','COM1.tif','x'.repeat(252)+'.tif']){
      expect(renameValidationError(name,'original.tif',[])).not.toBeNull();
    }
  });
  it('prevents extension changes and case-insensitive sibling conflicts',()=>{
    expect(renameValidationError('sample.csv','sample.tif',[])).toMatch(/扩展名/);
    expect(renameValidationError('CONTROL_REP2.TIF','Control_Rep1.tif',['Control_Rep2.tif'])).toMatch(/同名文件/);
    expect(renameValidationError('sample 2.tif','sample.tif',['other.tif'])).toBeNull();
    expect(renameValidationError('sample.tif','sample.tif',['sample.tif'])).toBeNull();
  });
  it('accepts Unicode names and source filenames unchanged',()=>{
    expect(renameValidationError('肝脏组织 图像_01.tif','sample.tif',[])).toBeNull();
    expect(renameValidationError('Control_Rep1.tif','Control_Rep1.tif',[])).toBeNull();
  });
});
