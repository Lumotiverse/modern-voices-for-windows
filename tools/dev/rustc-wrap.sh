#!/bin/bash
# Type-check only: compile the windows-rs crates and the engine as if for Windows.
rustc="$1"; shift
name=""; prev=""
for a in "$@"; do [ "$prev" = "--crate-name" ] && name="$a"; prev="$a"; done
case "$name" in
  windows|windows_core|windows_future|windows_strings|windows_result|windows_collections|windows_numerics|windows_threading|windows_implement|windows_interface|modern_voices)
    exec "$rustc" "$@" --cfg windows -A explicit_builtin_cfgs_in_flags ;;
  *) exec "$rustc" "$@" ;;
esac
