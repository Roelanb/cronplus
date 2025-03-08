#!/bin/bash

# Script to publish CronPlus applications for both Linux and Windows
# Creates separate folders for each OS in the Publish directory

# Clean up existing publish directory
echo "Cleaning up existing publish directory..."
rm -rf Publish

# Create base directories
mkdir -p Publish/linux/cronplusui
mkdir -p Publish/windows/cronplusui
mkdir -p Publish/linux/cronplusservice
mkdir -p Publish/windows/cronplusservice

echo "Publishing CronPlus applications for Linux and Windows..."

# Publish service app for Linux
echo "Publishing cronplusservice for Linux..."
dotnet publish ./cronplusservice/cronplus.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -o ./Publish/linux/cronplusservice || {
        echo "Error publishing cronplusservice for Linux, but continuing..."
    }

# Publish service app for Windows
echo "Publishing cronplusservice for Windows..."
dotnet publish ./cronplusservice/cronplus.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -o ./Publish/windows/cronplusservice || {
        echo "Error publishing cronplusservice for Windows, but continuing..."
    }

# Publish UI app for Linux
echo "Publishing cronplusui for Linux..."
dotnet publish ./cronplusui/CronPlusUI.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -o ./Publish/linux/cronplusui || {
        echo "Error publishing cronplusui for Linux, but continuing..."
    }

# Publish UI app for Windows
echo "Publishing cronplusui for Windows..."
dotnet publish ./cronplusui/CronPlusUI.csproj \
    -c Release \
    -r win-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:PublishReadyToRun=true \
    -o ./Publish/windows/cronplusui || {
        echo "Error publishing cronplusui for Windows, but continuing..."
    }

# Copy default config files
echo "Copying default configuration files..."
cp ./cronplusui/AppConfig.json ./Publish/linux/cronplusui/ || echo "Warning: Could not copy AppConfig.json to Linux build"
cp ./cronplusui/AppConfig.json ./Publish/windows/cronplusui/ || echo "Warning: Could not copy AppConfig.json to Windows build"

# Create README files with instructions
echo "Creating README files..."
cat > ./Publish/linux/README.txt << EOL
CronPlus for Linux
==================

This package contains two applications:
1. cronplusservice - The service that runs scheduled tasks
2. cronplusui - The UI application to manage the service

To run the UI application:
1. Navigate to the cronplusui directory
2. Make the application executable: chmod +x CronPlusUI
3. Run the application: ./CronPlusUI

To run the service directly:
1. Navigate to the cronplusservice directory
2. Make the service executable: chmod +x cronplus
3. Run the service: ./cronplus
EOL

cat > ./Publish/windows/README.txt << EOL
CronPlus for Windows
===================

This package contains two applications:
1. cronplusservice - The service that runs scheduled tasks
2. cronplusui - The UI application to manage the service

To run the UI application:
1. Navigate to the cronplusui directory
2. Double-click on CronPlusUI.exe

To run the service directly:
1. Navigate to the cronplusservice directory
2. Double-click on cronplus.exe
EOL

echo "Publishing complete!"
echo "Linux binaries: ./Publish/linux/"
echo "Windows binaries: ./Publish/windows/"
