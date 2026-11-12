#!/bin/bash
# Makes a Linux type-check of the engine as if for Windows (the engine can only be built on Windows).
#   tools/dev/setup-check.sh DIR     makes DIR (needs Rust; fetches the crates once)
#   then: cp engine/src/*.rs DIR/src/ && (cd DIR && RUSTC_WRAPPER=$PWD/rustc-wrap.sh DOCS_RS=1 cargo check --release --all-targets)
# windows-future is reduced to an empty crate and windows-strings has its three Windows-only lines
# replaced, so that the windows crates compile with --cfg windows on Linux. Type-checking only.
set -e
D="${1:?give a folder}"
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
mkdir -p "$D/src"
cp "$REPO"/engine/src/*.rs "$D/src/"
cp "$HERE/rustc-wrap.sh" "$D/" && chmod +x "$D/rustc-wrap.sh"
sed '/^\[dependencies\]/,$!d' "$REPO/engine/Cargo.toml" > /tmp/deps.$$
{ sed '/^\[dependencies\]/,$d' "$REPO/engine/Cargo.toml"; cat /tmp/deps.$$; printf '\n[patch.crates-io]\nwindows-future = { path = "stub-wf" }\nwindows-strings = { path = "stub-ws" }\n'; } > "$D/Cargo.toml"
rm /tmp/deps.$$
(cd "$D" && cargo fetch -q 2>/dev/null || true)
REG=$(ls -d ~/.cargo/registry/src/*/ | head -1)
WF=$(ls -d "$REG"windows-future-0.3.* | sort -V | tail -1); WS=$(ls -d "$REG"windows-strings-0.5.* | sort -V | tail -1)
rm -rf "$D/stub-wf" "$D/stub-ws"; cp -r "$WF" "$D/stub-wf"; cp -r "$WS" "$D/stub-ws"
echo '#![no_std]' > "$D/stub-wf/src/lib.rs"
H="$D/stub-ws/src/hstring.rs"
sed -i 's/std::os::windows::ffi::OsStringExt::from_wide(self)/{ let _ = self; unimplemented!() }/' "$H"
sed -i 's/std::os::windows::ffi::OsStrExt::encode_wide(value),/value.to_string_lossy().encode_utf16().collect::<Vec<u16>>().into_iter(),/' "$H"
sed -i 's/\.eq(std::os::windows::ffi::OsStrExt::encode_wide(other))/.eq(other.to_string_lossy().encode_utf16())/' "$H"
echo "ready: cd $D && RUSTC_WRAPPER=\$PWD/rustc-wrap.sh DOCS_RS=1 cargo check --release --all-targets"
