//! Windows handle ownership is the safety boundary; path checks alone are insufficient.
use super::*;
use sha2::{Digest, Sha256};
use std::fs::{self, File, OpenOptions};
use std::io::{Read, Seek, SeekFrom};
use std::path::Component;

#[derive(Debug, Clone, PartialEq, Eq)]
pub(super) struct Identity {
    pub id: String,
    pub size: i64,
    pub mtime: String,
}

pub(super) struct Directories {
    _handles: Vec<File>,
    _pins: Vec<File>,
}

impl Directories {
    pub fn hold(path: &Path, create: bool) -> Result<Self, TreeError> {
        if !path.is_absolute() {
            return Err(failure("UNSAFE_PATH"));
        }
        let mut current = PathBuf::new();
        let mut handles = Vec::new();
        for component in path.components() {
            match component {
                Component::Prefix(_) => {
                    current.push(component.as_os_str());
                    continue;
                }
                Component::RootDir => current.push(component.as_os_str()),
                Component::Normal(name) => {
                    let parent = handles.last().ok_or_else(|| failure("UNSAFE_PATH"))?;
                    let name = name.to_str().ok_or_else(|| failure("UNSAFE_PATH"))?;
                    handles.push(relative_directory(parent, name, create)?);
                    current.push(component.as_os_str());
                    continue;
                }
                _ => return Err(failure("UNSAFE_PATH")),
            }
            let handle = open_directory(&current)?;
            handles.push(handle);
        }
        let result = Self {
            _handles: handles,
            _pins: Vec::new(),
        };
        result.validate()?;
        Ok(result)
    }
    pub fn leaf(&self) -> Result<&File, TreeError> {
        self._handles.last().ok_or_else(|| failure("UNSAFE_PATH"))
    }
    pub fn validate(&self) -> Result<(), TreeError> {
        for handle in &self._handles {
            plain_directory(handle)?;
        }
        Ok(())
    }
    pub fn pin(&mut self) -> Result<(), TreeError> {
        let file = pin_directory(self.leaf()?)?;
        self._pins.push(file);
        self.validate()
    }
}

#[cfg(windows)]
fn plain_directory(file: &File) -> Result<(), TreeError> {
    use std::os::windows::fs::MetadataExt;
    let metadata = file.metadata().map_err(|_| failure("UNSAFE_PATH"))?;
    if !metadata.is_dir() || metadata.file_attributes() & 0x400 != 0 {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(())
}
#[cfg(not(windows))]
fn plain_directory(_: &File) -> Result<(), TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}
#[cfg(windows)]
fn pin_directory(parent: &File) -> Result<File, TreeError> {
    let file = relative::open(parent, ".data-m-directory-pin", 0x80000000, 1, 3, false)?;
    if identity(&file)?.size != 0 {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(file)
}
#[cfg(not(windows))]
fn pin_directory(_: &File) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}
#[cfg(windows)]
pub(super) fn database_pin(parent: &Directories, name: &str) -> Result<File, TreeError> {
    relative::open(parent.leaf()?, name, 0x80000000, 1, 1, false)
}
#[cfg(not(windows))]
pub(super) fn database_pin(_: &Directories, _: &str) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
mod relative {
    use super::*;
    use std::{
        ffi::c_void,
        os::windows::io::{AsRawHandle, FromRawHandle},
    };
    type Handle = *mut c_void;
    #[repr(C)]
    struct UnicodeString {
        length: u16,
        maximum_length: u16,
        buffer: *mut u16,
    }
    #[repr(C)]
    struct ObjectAttributes {
        length: u32,
        root: Handle,
        name: *mut UnicodeString,
        attributes: u32,
        security: *mut c_void,
        qos: *mut c_void,
    }
    #[repr(C)]
    struct IoStatus {
        status: usize,
        information: usize,
    }
    #[link(name = "ntdll")]
    extern "system" {
        fn NtCreateFile(
            handle: *mut Handle,
            access: u32,
            attributes: *mut ObjectAttributes,
            status: *mut IoStatus,
            allocation: *const i64,
            file_attributes: u32,
            share: u32,
            disposition: u32,
            options: u32,
            ea: *const c_void,
            ea_length: u32,
        ) -> i32;
        fn NtSetInformationFile(
            handle: Handle,
            status: *mut IoStatus,
            information: *const c_void,
            length: u32,
            class: u32,
        ) -> i32;
    }
    pub fn open(
        parent: &File,
        name: &str,
        access: u32,
        share: u32,
        disposition: u32,
        directory: bool,
    ) -> Result<File, TreeError> {
        if name.is_empty()
            || name.chars().any(|c| matches!(c, '\\' | '/' | ':' | '\0'))
            || matches!(name, "." | "..")
        {
            return Err(failure("UNSAFE_PATH"));
        }
        let mut wide: Vec<u16> = name.encode_utf16().collect();
        let bytes = u16::try_from(wide.len() * 2).map_err(|_| failure("UNSAFE_PATH"))?;
        let mut string = UnicodeString {
            length: bytes,
            maximum_length: bytes,
            buffer: wide.as_mut_ptr(),
        };
        let mut attributes = ObjectAttributes {
            length: std::mem::size_of::<ObjectAttributes>() as u32,
            root: parent.as_raw_handle() as _,
            name: &mut string,
            attributes: 0x1000 | 0x40,
            security: std::ptr::null_mut(),
            qos: std::ptr::null_mut(),
        };
        let mut status = IoStatus {
            status: 0,
            information: 0,
        };
        let mut handle = std::ptr::null_mut();
        // OBJ_DONT_REPARSE plus a held RootDirectory avoids reparsing a string
        // path if a directory is converted in place after its initial validation.
        let result = unsafe {
            NtCreateFile(
                &mut handle,
                access | 0x00100000,
                &mut attributes,
                &mut status,
                std::ptr::null(),
                if directory { 0x10 } else { 0x80 },
                share,
                disposition,
                0x00200000 | 0x20 | if directory { 0x1 } else { 0x40 },
                std::ptr::null(),
                0,
            )
        };
        if result < 0 || handle.is_null() {
            return Err(failure(if disposition == 1 {
                "SOURCE_UNAVAILABLE"
            } else {
                "DISK_WRITE_FAILED"
            }));
        }
        // Successful NtCreateFile transfers ownership of exactly this handle.
        let file = unsafe { File::from_raw_handle(handle as _) };
        if directory {
            use std::os::windows::fs::MetadataExt;
            let info = file.metadata().map_err(|_| failure("UNSAFE_PATH"))?;
            if !info.is_dir() || info.file_attributes() & 0x400 != 0 {
                return Err(failure("UNSAFE_PATH"));
            }
        } else {
            identity(&file)?;
        }
        Ok(file)
    }
    pub fn link(source: &File, objects: &File, name: &str) -> Result<(), TreeError> {
        if name.len() != 64
            || !name
                .bytes()
                .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
        {
            return Err(failure("UNSAFE_PATH"));
        }
        #[repr(C)]
        struct Link {
            flags: u32,
            root: Handle,
            length: u32,
            name: [u16; 1],
        }
        let wide: Vec<u16> = name.encode_utf16().collect();
        let offset = std::mem::offset_of!(Link, name);
        let length = offset + wide.len() * 2;
        let mut buffer = vec![0u64; (length + 7) / 8];
        let link = buffer.as_mut_ptr() as *mut Link;
        // Aligned, zero-initialized storage; ReplaceIfExists/flags stays zero.
        unsafe {
            (*link).root = objects.as_raw_handle() as _;
            (*link).length = (wide.len() * 2) as u32;
            std::ptr::copy_nonoverlapping(
                wide.as_ptr(),
                (link as *mut u8).add(offset) as *mut u16,
                wide.len(),
            );
            let mut status = IoStatus {
                status: 0,
                information: 0,
            };
            let result = NtSetInformationFile(
                source.as_raw_handle() as _,
                &mut status,
                link as _,
                length as u32,
                11,
            );
            // STATUS_OBJECT_NAME_COLLISION means verify/reuse the existing object.
            if result < 0 && result as u32 != 0xc0000035 {
                return Err(failure("DISK_WRITE_FAILED"));
            }
        }
        Ok(())
    }
}

#[cfg(windows)]
fn relative_directory(parent: &File, name: &str, create: bool) -> Result<File, TreeError> {
    relative::open(parent, name, 0x81, 3, if create { 3 } else { 1 }, true)
        .map_err(|_| failure("UNSAFE_PATH"))
}
#[cfg(not(windows))]
fn relative_directory(_: &File, _: &str, _: bool) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
pub(super) fn stage_writer(repo: &Repository, name: &str) -> Result<File, TreeError> {
    relative::open(repo._directories[2].leaf()?, name, 0xc0000000, 1, 2, false)
}
#[cfg(windows)]
pub(super) fn stage_pin(repo: &Repository, name: &str) -> Result<File, TreeError> {
    relative::open(repo._directories[2].leaf()?, name, 0x80010000, 1, 1, false)
}
#[cfg(not(windows))]
pub(super) fn stage_writer(_: &Repository, _: &str) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}
#[cfg(not(windows))]
pub(super) fn stage_pin(_: &Repository, _: &str) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
pub(super) fn publish(repo: &Repository, stage: File, hash: &str) -> Result<(), TreeError> {
    relative::link(&stage, repo._directories[3].leaf()?, hash)
}
#[cfg(not(windows))]
pub(super) fn publish(_: &Repository, _: File, _: &str) -> Result<(), TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
fn open_directory(path: &Path) -> Result<File, TreeError> {
    use std::os::windows::fs::OpenOptionsExt;
    use windows_sys::Win32::Storage::FileSystem::*;
    let file = OpenOptions::new()
        .read(true)
        .access_mode(FILE_LIST_DIRECTORY | FILE_READ_ATTRIBUTES)
        .share_mode(FILE_SHARE_READ | FILE_SHARE_WRITE)
        .custom_flags(FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT)
        .open(path)
        .map_err(|_| failure("UNSAFE_PATH"))?;
    use std::os::windows::fs::MetadataExt;
    let metadata = file.metadata().map_err(|_| failure("UNSAFE_PATH"))?;
    if !metadata.is_dir() || metadata.file_attributes() & FILE_ATTRIBUTE_REPARSE_POINT != 0 {
        return Err(failure("UNSAFE_PATH"));
    }
    Ok(file)
}

#[cfg(not(windows))]
fn open_directory(_: &Path) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
pub(super) fn identity(file: &File) -> Result<Identity, TreeError> {
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::Storage::FileSystem::*;
    let mut info = std::mem::MaybeUninit::<BY_HANDLE_FILE_INFORMATION>::zeroed();
    // The valid File owns this handle and the API initializes the structure on success.
    if unsafe { GetFileInformationByHandle(file.as_raw_handle() as _, info.as_mut_ptr()) } == 0 {
        return Err(failure("SOURCE_UNAVAILABLE"));
    }
    let info = unsafe { info.assume_init() };
    if info.dwFileAttributes & (FILE_ATTRIBUTE_REPARSE_POINT | FILE_ATTRIBUTE_DIRECTORY) != 0 {
        return Err(failure("UNSAFE_PATH"));
    }
    let size = ((info.nFileSizeHigh as u64) << 32) | info.nFileSizeLow as u64;
    let size = i64::try_from(size).map_err(|_| failure("INVALID_REQUEST"))?;
    let mtime = ((info.ftLastWriteTime.dwHighDateTime as u64) << 32)
        | info.ftLastWriteTime.dwLowDateTime as u64;
    Ok(Identity {
        id: format!(
            "{:08x}{:08x}{:08x}",
            info.dwVolumeSerialNumber, info.nFileIndexHigh, info.nFileIndexLow
        ),
        size,
        mtime: mtime.to_string(),
    })
}

#[cfg(not(windows))]
pub(super) fn identity(_: &File) -> Result<Identity, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

#[cfg(windows)]
fn regular(path: &Path, write: bool, create: bool, share: u32) -> Result<File, TreeError> {
    use std::os::windows::fs::OpenOptionsExt;
    use windows_sys::Win32::Storage::FileSystem::FILE_FLAG_OPEN_REPARSE_POINT;
    let file = OpenOptions::new()
        .read(true)
        .write(write)
        .create_new(create)
        .share_mode(share)
        .custom_flags(FILE_FLAG_OPEN_REPARSE_POINT)
        .open(path)
        .map_err(|_| {
            failure(if write {
                "DISK_WRITE_FAILED"
            } else {
                "SOURCE_UNAVAILABLE"
            })
        })?;
    identity(&file)?;
    Ok(file)
}

#[cfg(not(windows))]
fn regular(_: &Path, _: bool, _: bool, _: u32) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

pub(super) fn readonly(path: &Path) -> Result<File, TreeError> {
    regular(path, false, false, 1)
}
pub(super) fn staging_writer(path: &Path) -> Result<File, TreeError> {
    regular(path, true, true, 1)
}

#[cfg(windows)]
pub(super) fn repository_lock(directory: &Directories) -> Result<File, TreeError> {
    relative::open(directory.leaf()?, "import.lock", 0xc0000000, 0, 3, false)
        .map_err(|_| failure("REPOSITORY_BUSY"))
}

#[cfg(not(windows))]
pub(super) fn repository_lock(_: &Directories) -> Result<File, TreeError> {
    Err(failure("UNSUPPORTED_PLATFORM"))
}

pub(super) struct Source {
    pub file: File,
    pub info: Identity,
    pub path: PathBuf,
    _directories: Directories,
}

pub(super) fn source(path: &Path, repository: &Path) -> Result<Source, TreeError> {
    if !path.is_absolute()
        || path
            .components()
            .any(|c| matches!(c, Component::ParentDir | Component::CurDir))
    {
        return Err(failure("UNSAFE_PATH"));
    }
    let name = path
        .file_name()
        .and_then(|n| n.to_str())
        .ok_or_else(|| failure("UNSAFE_PATH"))?;
    required_name(Some(name))?;
    if name.chars().any(|c| matches!(c, ':' | '/' | '\\')) {
        return Err(failure("UNSAFE_PATH"));
    }
    let directories =
        Directories::hold(path.parent().ok_or_else(|| failure("UNSAFE_PATH"))?, false)?;
    // Share READ only, so write/delete/replacement attempts fail for the entire copy.
    #[cfg(windows)]
    let file = relative::open(directories.leaf()?, name, 0x80000000, 1, 1, false)?;
    #[cfg(not(windows))]
    let file = readonly(path)?;
    directories.validate()?;
    let canonical = fs::canonicalize(path).map_err(|_| failure("SOURCE_UNAVAILABLE"))?;
    let root = fs::canonicalize(repository).map_err(|_| failure("UNSAFE_PATH"))?;
    let normalized = canonical.to_string_lossy().to_lowercase();
    let root_normalized = root.to_string_lossy().to_lowercase();
    if normalized == root_normalized || normalized.starts_with(&(root_normalized + "\\")) {
        return Err(failure("UNSAFE_PATH"));
    }
    let info = identity(&file)?;
    Ok(Source {
        file,
        info,
        path: canonical,
        _directories: directories,
    })
}

pub(super) fn digest(file: &mut File) -> Result<(String, i64), TreeError> {
    file.seek(SeekFrom::Start(0))
        .map_err(|_| failure("SOURCE_UNAVAILABLE"))?;
    let mut sha = Sha256::new();
    let mut count = 0_i64;
    let mut buffer = vec![0_u8; 1024 * 1024];
    loop {
        let length = file
            .read(&mut buffer)
            .map_err(|_| failure("SOURCE_UNAVAILABLE"))?;
        if length == 0 {
            break;
        }
        sha.update(&buffer[..length]);
        count = count
            .checked_add(length as i64)
            .ok_or_else(|| failure("INVALID_REQUEST"))?;
    }
    Ok((format!("{:x}", sha.finalize()), count))
}

pub(super) fn object(repo: &Repository, hash: &str, size: i64) -> Result<File, TreeError> {
    if hash.len() != 64
        || !hash
            .bytes()
            .all(|b| b.is_ascii_digit() || (b'a'..=b'f').contains(&b))
    {
        return Err(failure("BLOB_INTEGRITY_FAILED"));
    }
    #[cfg(windows)]
    let mut file = relative::open(repo._directories[3].leaf()?, hash, 0x80000000, 1, 1, false)
        .map_err(|_| failure("BLOB_INTEGRITY_FAILED"))?;
    #[cfg(not(windows))]
    let mut file =
        readonly(&repo.objects.join(hash)).map_err(|_| failure("BLOB_INTEGRITY_FAILED"))?;
    let (actual, length) = digest(&mut file).map_err(|_| failure("BLOB_INTEGRITY_FAILED"))?;
    if actual != hash || length != size {
        return Err(failure("BLOB_INTEGRITY_FAILED"));
    }
    // Caller must hold this deny-write/delete handle through SQL registration.
    Ok(file)
}
