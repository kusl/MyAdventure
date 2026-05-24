#!/bin/bash

# Exit immediately if any command fails
set -e

# Define paths and file names
LOG_DIR="/home/kushal/src/dotnet/MyAdventure/docs/llm/vendor/desktoplogs"
TIMESTAMP=$(date +"%Y-%m-%d-%H-%M-%S")
LOG_FILE="$LOG_DIR/$TIMESTAMP.txt"

# Ensure the log directory exists
mkdir -p "$LOG_DIR"

# Navigate to the project root
cd /home/kushal/src/dotnet/MyAdventure

# Run the build pipeline
time dotnet clean
time dotnet restore
time dotnet build
time dotnet test
time dotnet list package
time dotnet list package --outdated
time dotnet format
time sh export.sh

# Run the desktop app and pipe the output to the timestamped file
echo "Starting MyAdventure.Desktop... Logging to $LOG_FILE"
MYADVENTURE_VERBOSE=1 \
SENTRY_DSN='https://fe6ae5ee15285c313b8171bb7a5a4ad0@o4511444968079360.ingest.de.sentry.io/4511444969390160' \
dotnet run --project src/MyAdventure.Desktop > "$LOG_FILE" 2>&1
