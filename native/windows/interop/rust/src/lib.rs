// Matches native_channels_probe.h. No allocation, retained pointers or panics.
#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetCurrentProcessId() -> u32;
}

#[unsafe(no_mangle)]
pub extern "C" fn nc_abi_version() -> u32 {
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn nc_language() -> u32 {
    2
}

#[unsafe(no_mangle)]
pub extern "C" fn nc_process_id() -> u32 {
    // SAFETY: This Win32 call has no pointer arguments or preconditions.
    unsafe { GetCurrentProcessId() }
}

/// # Safety
/// A non-null result must point to a writable, aligned i64 for this call.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn nc_add(a: i64, b: i64, result: *mut i64) -> i32 {
    if result.is_null() {
        return 1;
    }
    match a.checked_add(b) {
        Some(value) => {
            unsafe { result.write(value) };
            0
        }
        None => 2,
    }
}

/// # Safety
/// For nonzero length, input/output must be valid separate buffers for the
/// supplied length/capacity. Neither pointer is retained after returning.
#[unsafe(no_mangle)]
pub unsafe extern "C" fn nc_transform(
    input: *const u8,
    length: u32,
    output: *mut u8,
    capacity: u32,
) -> i32 {
    if capacity < length || (length != 0 && (input.is_null() || output.is_null())) {
        return 1;
    }
    for i in 0..length as usize {
        unsafe { output.add(i).write(input.add(i).read() ^ 0xa5) };
    }
    0
}
