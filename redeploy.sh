#!/bin/bash
cd ~/CriterionArt
jprm plugin build . --output build/ --version 1.0.0.0
sudo rm -rf "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.0"
sudo mkdir -p "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.0"
sudo unzip -o build/criterion-collection-art_1.0.0.0.zip -d "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.0"
sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.0"
sudo systemctl restart jellyfin
journalctl -u jellyfin -n 100 --no-pager | grep -iE "plugin|criterion"