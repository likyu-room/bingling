#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
install_dir="$HOME/Applications/Bingling.app"
bin_dir="$HOME/.local/bin"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run uninstall.sh first."
    exit 1
fi
if [ -e "$bin_dir/bingling" ] || [ -L "$bin_dir/bingling" ]; then
    printf '%s\n' "$bin_dir/bingling already exists. Remove or rename it before installing Bingling."
    exit 1
fi
test -x "$source_dir/Bingling.app/Contents/MacOS/Bingling"
mkdir -p "$HOME/Applications" "$bin_dir"
/usr/bin/ditto "$source_dir/Bingling.app" "$install_dir"
ln -s "$install_dir/Contents/MacOS/bingling-cli" "$bin_dir/bingling"
printf '%s\n' "Installed $install_dir" "CLI: $bin_dir/bingling (add $bin_dir to PATH if needed)."
printf '%s\n' 'Open Bingling from Applications. Grant Accessibility and Finder Automation access when requested.'
