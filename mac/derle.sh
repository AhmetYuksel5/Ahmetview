#!/bin/bash
# Ahmetview.app'i derler ve Ahmetview-mac.zip'e koyar. GitHub'ın Mac
# makinesinde çalışıyor; Xcode kurulu herhangi bir Mac'te de çalışır.
# Kullanım: ./derle.sh [sürüm numarası]
set -euo pipefail
cd "$(dirname "$0")"
SURUM="${1:-1}"

rm -rf yapi && mkdir -p yapi
# Hem Apple Silicon hem Intel Mac'lerde çalışsın diye iki ayrı derleme
# tek dosyada birleştiriliyor.
for mimari in arm64 x86_64; do
  swiftc -O -swift-version 5 -target "$mimari-apple-macos12.0" \
    -o "yapi/Ahmetview-$mimari" main.swift
done
lipo -create -output yapi/Ahmetview yapi/Ahmetview-arm64 yapi/Ahmetview-x86_64

PAKET=yapi/Ahmetview.app
mkdir -p "$PAKET/Contents/MacOS" "$PAKET/Contents/Resources"
cp yapi/Ahmetview "$PAKET/Contents/MacOS/"
cp Info.plist "$PAKET/Contents/"
cp Ahmetview.icns "$PAKET/Contents/Resources/"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString 0.$SURUM" "$PAKET/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $SURUM" "$PAKET/Contents/Info.plist"

# Kimliksiz (ad-hoc) imza: Apple Silicon imzasız uygulamayı hiç çalıştırmıyor.
# Apple'ın onayı değil; "bilinmeyen geliştirici" uyarısını kaldırmaz.
codesign --force --deep --sign - "$PAKET"
codesign --verify --verbose "$PAKET"

ditto -c -k --keepParent "$PAKET" yapi/Ahmetview-mac.zip
ls -la yapi/Ahmetview-mac.zip "$PAKET/Contents/MacOS/Ahmetview"
