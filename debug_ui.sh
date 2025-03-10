#!/bin/bash

# Script to debug the CronPlusUI application

echo "Starting CronPlusUI debug script..."
echo "Current directory: $(pwd)"

# Check if the AppConfig.json file exists
cd Publish/linux/cronplusui
echo "Checking for AppConfig.json..."
if [ -f "AppConfig.json" ]; then
    echo "AppConfig.json exists with content:"
    cat AppConfig.json
else
    echo "ERROR: AppConfig.json does not exist!"
fi

# Check if the Config.json file exists
echo "Checking for Config.json..."
if [ -f "Config.json" ]; then
    echo "Config.json exists with content:"
    cat Config.json
else
    echo "ERROR: Config.json does not exist!"
fi

# Check file permissions
echo "Checking file permissions..."
ls -la

# Run the application with verbose output
echo "Running CronPlusUI with verbose output..."
export AVALONIA_LOG_LEVEL=Debug
export DOTNET_ENVIRONMENT=Development

# Run with strace to capture system calls
echo "Running with strace to capture any errors..."
strace -f -e trace=file ./CronPlusUI 2>&1 | grep -i "error\|fail\|exception" > ui_strace.log &
PID=$!

# Wait a few seconds
echo "Waiting for application to start..."
sleep 5

# Check if the process is still running
if ps -p $PID > /dev/null; then
    echo "Application started successfully (PID: $PID)"
    echo "Check ui_strace.log for any errors"
else
    echo "Application failed to start or crashed"
    echo "Check ui_strace.log for errors"
fi

echo "Debug script completed."
