#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
project="$repo_dir/src/Elyra/Elyra.csproj"
keystore="${ELYRA_ANDROID_KEYSTORE:-$HOME/.android/debug.keystore}"
alias_name="${ELYRA_ANDROID_KEY_ALIAS:-androiddebugkey}"
store_pass="${ELYRA_ANDROID_STORE_PASSWORD:-android}"
key_pass="${ELYRA_ANDROID_KEY_PASSWORD:-android}"
output="$repo_dir/artifacts/android/elyra-1.5.0-arm64.apk"

if [[ ! -f "$keystore" ]]; then
    echo "Android-Signierschlüssel fehlt: $keystore" >&2
    exit 1
fi

dotnet clean "$project" -f net10.0-android -c Release -r android-arm64 -v:q
publish_args=(
    "$project" -f net10.0-android -c Release -r android-arm64
    -p:AndroidKeyStore=true
    "-p:AndroidSigningKeyStore=$keystore"
    "-p:AndroidSigningKeyAlias=$alias_name"
    "-p:AndroidSigningStorePass=$store_pass"
    "-p:AndroidSigningKeyPass=$key_pass"
)
dotnet publish "${publish_args[@]}"

apk="$repo_dir/src/Elyra/bin/Release/net10.0-android/android-arm64/publish/com.mastl.elyra-Signed.apk"
entries="$(unzip -Z1 "$apk")"
for library in libvlc.so libc++_shared.so; do
    if ! grep -Fxq "lib/arm64-v8a/$library" <<< "$entries"; then
        echo "APK enthält lib/arm64-v8a/$library nicht" >&2
        exit 1
    fi
done

mkdir -p "$(dirname "$output")"
cp "$apk" "$output"
echo "APK bereit: $output"
