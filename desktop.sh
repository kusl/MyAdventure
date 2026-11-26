#!/bin/bash

cd /home/kushal/src/dotnet/MyAdventure

LOG_DIR="docs/llm/vendor/desktoplogs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/$(date +"%Y-%m-%d-%H-%M-%S").txt"

cat desktop.sh

time dotnet clean
time dotnet restore
time dotnet build
time dotnet test
time dotnet list package
time dotnet list package --outdated
time dotnet format
cat export.sh
time sh export.sh

MYADVENTURE_VERBOSE=1 dotnet run --project src/MyAdventure.Desktop > "$LOG_FILE" 2>&1
