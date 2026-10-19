#!/bin/zsh
set -euo pipefail

root_dir="$(cd "$(dirname "$0")/.." && pwd)"
project="$root_dir/ImageCare.UI.Avalonia/ImageCare.UI.Avalonia.csproj"
artifact_dir="$root_dir/artifacts/macos-arm64"
publish_dir="$artifact_dir/publish"
app_dir="$artifact_dir/ImageCare.app"

rm -rf "$publish_dir" "$app_dir"

dotnet restore "$project" --runtime osx-arm64
dotnet publish "$project" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  --no-restore \
  --output "$publish_dir"

mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"
cp -R "$publish_dir/." "$app_dir/Contents/MacOS/"
cp "$root_dir/ImageCare.UI.Avalonia/Platforms/macOS/Info.plist" "$app_dir/Contents/Info.plist"
cp "$root_dir/ImageCare.UI.Avalonia/Assets/imagecare-logo.icns" "$app_dir/Contents/Resources/imagecare-logo.icns"
chmod +x "$app_dir/Contents/MacOS/ImageCare"

codesign --force --deep --sign - "$app_dir"

echo "$app_dir"
