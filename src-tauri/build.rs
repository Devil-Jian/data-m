use std::{env, fs, path::PathBuf};

fn main() {
    let manifest_dir = PathBuf::from(
        env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR must be set by Cargo"),
    );
    let icon_dir = manifest_dir.join("icons");
    let icon_path = icon_dir.join("icon.ico");

    if !icon_path.exists() {
        fs::create_dir_all(&icon_dir).expect("failed to create Tauri icon directory");
        fs::write(&icon_path, default_windows_icon())
            .expect("failed to generate default Windows icon");
    }

    tauri_build::build();
}

fn default_windows_icon() -> Vec<u8> {
    const SIZE: usize = 16;
    const PIXEL_BYTES: usize = SIZE * SIZE * 4;
    const MASK_BYTES: usize = SIZE * 4;
    const IMAGE_BYTES: usize = 40 + PIXEL_BYTES + MASK_BYTES;

    let mut dib = Vec::with_capacity(IMAGE_BYTES);
    push_u32(&mut dib, 40);
    push_i32(&mut dib, SIZE as i32);
    push_i32(&mut dib, (SIZE * 2) as i32);
    push_u16(&mut dib, 1);
    push_u16(&mut dib, 32);
    push_u32(&mut dib, 0);
    push_u32(&mut dib, PIXEL_BYTES as u32);
    push_i32(&mut dib, 0);
    push_i32(&mut dib, 0);
    push_u32(&mut dib, 0);
    push_u32(&mut dib, 0);

    let glyph = [
        "11110", "10001", "10001", "10001", "10001", "10001", "11110",
    ];

    for y in (0..SIZE).rev() {
        for x in 0..SIZE {
            let is_d = (4..11).contains(&y)
                && (5..10).contains(&x)
                && glyph[y - 4].as_bytes()[x - 5] == b'1';
            let (red, green, blue, alpha) = if is_d {
                (255, 255, 255, 255)
            } else if x == 12 && y == 12 {
                (77, 197, 166, 255)
            } else {
                (24, 59, 99, 255)
            };
            dib.extend_from_slice(&[blue, green, red, alpha]);
        }
    }

    for _ in 0..SIZE {
        dib.extend_from_slice(&[0, 0, 0, 0]);
    }

    let mut icon = Vec::with_capacity(22 + IMAGE_BYTES);
    push_u16(&mut icon, 0);
    push_u16(&mut icon, 1);
    push_u16(&mut icon, 1);
    icon.extend_from_slice(&[SIZE as u8, SIZE as u8, 0, 0]);
    push_u16(&mut icon, 1);
    push_u16(&mut icon, 32);
    push_u32(&mut icon, IMAGE_BYTES as u32);
    push_u32(&mut icon, 22);
    icon.extend_from_slice(&dib);
    icon
}

fn push_u16(target: &mut Vec<u8>, value: u16) {
    target.extend_from_slice(&value.to_le_bytes());
}

fn push_u32(target: &mut Vec<u8>, value: u32) {
    target.extend_from_slice(&value.to_le_bytes());
}

fn push_i32(target: &mut Vec<u8>, value: i32) {
    target.extend_from_slice(&value.to_le_bytes());
}
