#!/bin/bash
cd ~/CriterionArt
jprm plugin build . --output build/ --version 1.0.0.12
sudo rm -rf "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12"
sudo mkdir -p "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12"
sudo unzip -o build/criterion-collection-art_1.0.0.12.zip -d "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12"

# jprm can't package the native SkiaSharp binary correctly — copy it in manually
sudo cp bin/Release/net10.0/runtimes/linux-x64/native/libSkiaSharp.so \
  "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12/"

sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12"
sudo systemctl restart jellyfin
sudo journalctl -u jellyfin -n 100 --no-pager | grep -iE "plugin|criterion"