#!/bin/bash

# Define paths and file names
LOG_DIR="/home/kushal/src/dotnet/MyAdventure/docs/llm/vendor/desktoplogs"
TIMESTAMP=$(date +"%Y-%m-%d-%H-%M-%S")
LOG_FILE="$LOG_DIR/$TIMESTAMP.txt"

# Ensure the log directory exists
mkdir -p "$LOG_DIR"

# Navigate to the project root
cd /home/kushal/src/dotnet/MyAdventure

cat desktop.sh

# Run the build pipeline
time dotnet clean
time dotnet restore
time dotnet build
time dotnet test
time dotnet list package
time dotnet list package --outdated
time dotnet format
cat export.sh
time sh export.sh

# Run the desktop app with OpenTelemetry variables mapped to Sentry
MYADVENTURE_VERBOSE=1 \
OTEL_EXPORTER_OTLP_ENDPOINT="https://de.sentry.io/api/4511444969390160/opentelemetry" \
OTEL_EXPORTER_OTLP_HEADERS="x-sentry-auth=Sentry sentry_key=fe6ae5ee15285c313b8171bb7a5a4ad0,sentry_version=7" \
dotnet run --project src/MyAdventure.Desktop > "$LOG_FILE" 2>&1
