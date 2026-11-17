# AirGlance on Linux

The Linux archive produced by `packaging/scripts/pack.sh` is named
`AirGlance-<version>-linux-<arch>.tar.gz` and contains:

```text
AirGlance
airglance.desktop
icons/airglance-16.png
icons/airglance-24.png
icons/airglance-32.png
icons/airglance-48.png
icons/airglance-64.png
icons/airglance-128.png
icons/airglance-256.png
icons/airglance-512.png
```

The NativeAOT publish may also place shared native libraries next to the
`AirGlance` executable. Keep those files in the same directory as the binary.

## User-local installation

```sh
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications"
install -m 0755 AirGlance "$HOME/.local/bin/AirGlance"
install -m 0644 airglance.desktop "$HOME/.local/share/applications/airglance.desktop"

for icon in icons/airglance-*.png; do
    size="${icon##*/airglance-}"
    size="${size%.png}"
    install -Dm0644 "$icon" \
        "$HOME/.local/share/icons/hicolor/${size}x${size}/apps/airglance.png"
done
```

Make sure `$HOME/.local/bin` is on `PATH`; `airglance.desktop` uses
`Exec=AirGlance %f`. If it is not on `PATH`, update the `Exec` line to the
absolute path of the binary. Refresh caches if the desktop environment does not
pick up the entry immediately:

```sh
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
gtk-update-icon-cache -f -t "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
```

## System-wide installation

Use the system directories if the application should be available to every
user:

```sh
sudo install -m 0755 AirGlance /usr/local/bin/AirGlance
sudo install -m 0644 airglance.desktop /usr/share/applications/airglance.desktop

for icon in icons/airglance-*.png; do
    size="${icon##*/airglance-}"
    size="${size%.png}"
    sudo install -Dm0644 "$icon" \
        "/usr/share/icons/hicolor/${size}x${size}/apps/airglance.png"
done

sudo update-desktop-database /usr/share/applications 2>/dev/null || true
sudo gtk-update-icon-cache -f -t /usr/share/icons/hicolor 2>/dev/null || true
```

The desktop file uses `Icon=airglance`; the installed file must therefore be
named `airglance.png` in the hicolor theme, as shown above.

## MIME associations are not claimed

The `MimeType=` line only advertises the formats AirGlance can open. The
portable build does not install a MIME defaults file, does not run
`xdg-mime default`, and does not claim to be the default image viewer. As a
result, AirGlance appears as a choice for the listed types but the user's
current default application is left untouched. A user who wants AirGlance as
the default can opt in explicitly with their desktop's default-app settings or
with `xdg-mime default airglance.desktop image/jpeg` (and the other MIME types).
