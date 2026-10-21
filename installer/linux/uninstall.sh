#!/bin/sh
set -eu

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/filee/application"
desktop_file="$data_dir/applications/com.filee.app.desktop"
python3 - "$data_dir" "$install_dir" <<'PY'
import pathlib
import sys

root, target = map(pathlib.Path, sys.argv[1:])
expected = root.resolve() / 'filee' / 'application'
if target.is_symlink() or target.resolve() != expected:
    raise SystemExit('Refusing to remove an installation outside its expected directory.')
if (target / '.filee-package').read_text(encoding='utf-8').strip() != 'com.filee.app':
    raise SystemExit('The directory is not an installation created by Bingling.')
PY
"$install_dir/Bingling" --uninstall-cleanup
if [ -L "$HOME/.local/bin/bingling" ] && [ "$(readlink "$HOME/.local/bin/bingling")" = "$install_dir/bingling-cli" ]; then
    rm -- "$HOME/.local/bin/bingling"
fi
if [ -f "$desktop_file" ] && grep -q '^X-Filee-Managed=true$' "$desktop_file"; then
    rm -- "$desktop_file"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'Bingling was removed. Your settings and conversion results were kept.'
