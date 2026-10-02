#!/bin/bash
cd ~/CriterionArt

# Build
jprm plugin build . --output build/ --version 1.0.0.12

# Stop Jellyfin
sudo systemctl stop jellyfin

# Clean old versions
sudo rm -rf "/var/lib/jellyfin/plugins/Criterion Collection Art_"*

# Extract
unzip -o build/criterion-collection-art_1.0.0.12.zip -d /tmp/criterion-deploy

# Install with proper ownership
sudo mkdir -p "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12"
sudo cp /tmp/criterion-deploy/*.dll "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12/"
sudo cp /tmp/criterion-deploy/meta.json "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12/"  # Don't forget meta.json!
sudo chown -R jellyfin:jellyfin "/var/lib/jellyfin/plugins/Criterion Collection Art_1.0.0.12/"

# Start Jellyfin
sudo systemctl start jellyfin